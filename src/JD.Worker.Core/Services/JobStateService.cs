using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class JobStateService
{
    private readonly IJobStore _jobStore;
    private readonly JobStateMachine _stateMachine;

    public JobStateService(IJobStore jobStore, JobStateMachine stateMachine)
    {
        _jobStore = jobStore;
        _stateMachine = stateMachine;
    }

    public async Task<JobRecord?> TransitionAsync(
        JobRecord job,
        JobState targetState,
        string message,
        CancellationToken cancellationToken)
    {
        var transition = _stateMachine.Transition(job, targetState, message);
        if (!transition.Success || transition.Event is null)
        {
            return null;
        }

        await _jobStore.AppendEventAsync(transition.Event, cancellationToken);
        await _jobStore.SaveAsync(transition.Job, cancellationToken);
        return transition.Job;
    }

    public async Task<JobRecord?> TransitionAsync(
        string jobId,
        JobState targetState,
        string message,
        CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetAsync(jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        return await TransitionAsync(job, targetState, message, cancellationToken);
    }

    public async Task<JobRecord?> MoveToAsync(
        string jobId,
        JobState targetState,
        string message,
        CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetAsync(jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        if (job.State == JobState.Leased &&
            (targetState == JobState.Succeeded ||
             targetState == JobState.Failed ||
             targetState == JobState.Canceled ||
             targetState == JobState.DeadLettered))
        {
            job = await TransitionAsync(job, JobState.Running, "Execution started.", cancellationToken);
            if (job is null)
            {
                return null;
            }
        }

        return await TransitionAsync(job, targetState, message, cancellationToken);
    }
}

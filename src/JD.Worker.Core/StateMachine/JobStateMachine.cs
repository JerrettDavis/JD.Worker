using JD.Worker.Abstractions;
using PatternKit.Behavioral.State;

namespace JD.Worker.Core;

public sealed class JobStateMachine
{
    private static readonly StateMachine<JobState, JobState> Machine = BuildMachine();

    public TransitionResult Transition(JobRecord job, JobState targetState, string? message = null)
    {
        var nextState = job.State;
        if (!Machine.TryTransition(ref nextState, targetState))
        {
            return TransitionResult.Failure(job, $"Invalid transition from {job.State} to {targetState}.");
        }

        var updated = job with
        {
            State = nextState,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        var @event = new JobEvent
        {
            JobId = job.JobId,
            Attempt = job.Attempt,
            FromState = job.State,
            ToState = nextState,
            TimestampUtc = updated.UpdatedUtc,
            Message = message
        };

        return TransitionResult.FromSuccess(updated, @event);
    }

    private static StateMachine<JobState, JobState> BuildMachine() =>
        StateMachine<JobState, JobState>.Create()
            .InState(JobState.Received, state => state
                .When(IsAccepted).Permit(JobState.Accepted).End()
                .When(IsRejected).Permit(JobState.Rejected).End())
            .InState(JobState.Accepted, state => state
                .When(IsLeased).Permit(JobState.Leased).End())
            .InState(JobState.Leased, state => state
                .When(IsRunning).Permit(JobState.Running).End()
                .When(IsAccepted).Permit(JobState.Accepted).End())
            .InState(JobState.Running, state => state
                .When(IsSucceeded).Permit(JobState.Succeeded).End()
                .When(IsFailed).Permit(JobState.Failed).End()
                .When(IsCanceled).Permit(JobState.Canceled).End()
                .When(IsDeadLettered).Permit(JobState.DeadLettered).End())
            .InState(JobState.Succeeded, state => state
                .When(IsFinalized).Permit(JobState.Finalized).End())
            .InState(JobState.Failed, state => state
                .When(IsFinalized).Permit(JobState.Finalized).End())
            .InState(JobState.Canceled, state => state
                .When(IsFinalized).Permit(JobState.Finalized).End())
            .InState(JobState.DeadLettered, state => state
                .When(IsFinalized).Permit(JobState.Finalized).End())
            .Build();

    private static bool IsAccepted(in JobState state) => state == JobState.Accepted;
    private static bool IsRejected(in JobState state) => state == JobState.Rejected;
    private static bool IsLeased(in JobState state) => state == JobState.Leased;
    private static bool IsRunning(in JobState state) => state == JobState.Running;
    private static bool IsSucceeded(in JobState state) => state == JobState.Succeeded;
    private static bool IsFailed(in JobState state) => state == JobState.Failed;
    private static bool IsCanceled(in JobState state) => state == JobState.Canceled;
    private static bool IsDeadLettered(in JobState state) => state == JobState.DeadLettered;
    private static bool IsFinalized(in JobState state) => state == JobState.Finalized;
}

public sealed record TransitionResult(bool Success, JobRecord Job, JobEvent? Event, string? Error)
{
    public static TransitionResult Failure(JobRecord job, string error) =>
        new(false, job, null, error);

    public static TransitionResult FromSuccess(JobRecord job, JobEvent @event) =>
        new(true, job, @event, null);
}

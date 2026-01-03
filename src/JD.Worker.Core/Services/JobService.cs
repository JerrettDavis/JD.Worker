using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using Microsoft.Extensions.Logging;

namespace JD.Worker.Core;

public sealed class JobService : IJobService
{
    private readonly IJobStore _jobStore;
    private readonly JobStateMachine _stateMachine;
    private readonly IJobScheduler _scheduler;
    private readonly ILogger<JobService> _logger;

    public JobService(
        IJobStore jobStore,
        JobStateMachine stateMachine,
        IJobScheduler scheduler,
        ILogger<JobService> logger)
    {
        _jobStore = jobStore;
        _stateMachine = stateMachine;
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task<JobRecord> SubmitAsync(
        JobEnvelope envelope,
        string? source,
        CancellationToken cancellationToken)
    {
        var record = new JobRecord
        {
            JobId = envelope.Envelope.JobId,
            Attempt = envelope.Envelope.Attempt,
            State = JobState.Received,
            Envelope = envelope,
            Source = source,
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        var transition = _stateMachine.Transition(record, JobState.Accepted, "Job accepted by API");
        if (transition.Success && transition.Event is not null)
        {
            record = transition.Job;
            await _jobStore.AppendEventAsync(transition.Event, cancellationToken);
        }

        await _jobStore.SaveAsync(record, cancellationToken);
        await _scheduler.EnqueueAsync(record.JobId, cancellationToken);
        _logger.LogInformation("Job {JobId} accepted with state {State}.", record.JobId, record.State);
        return record;
    }

    public Task<IReadOnlyList<JobRecord>> ListAsync(JobState? state, CancellationToken cancellationToken) =>
        _jobStore.ListAsync(state, cancellationToken);
}

using System;
using System.Collections.Generic;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class JobStateMachine
{
    private static readonly HashSet<(JobState From, JobState To)> AllowedTransitions = new()
    {
        (JobState.Received, JobState.Accepted),
        (JobState.Received, JobState.Rejected),
        (JobState.Accepted, JobState.Leased),
        (JobState.Leased, JobState.Running),
        (JobState.Leased, JobState.Accepted),
        (JobState.Running, JobState.Succeeded),
        (JobState.Running, JobState.Failed),
        (JobState.Running, JobState.Canceled),
        (JobState.Running, JobState.DeadLettered),
        (JobState.Succeeded, JobState.Finalized),
        (JobState.Failed, JobState.Finalized),
        (JobState.Canceled, JobState.Finalized),
        (JobState.DeadLettered, JobState.Finalized)
    };

    public TransitionResult Transition(JobRecord job, JobState targetState, string? message = null)
    {
        if (!AllowedTransitions.Contains((job.State, targetState)))
        {
            return TransitionResult.Failure(job, $"Invalid transition from {job.State} to {targetState}.");
        }

        var updated = job with
        {
            State = targetState,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        var @event = new JobEvent
        {
            JobId = job.JobId,
            Attempt = job.Attempt,
            FromState = job.State,
            ToState = targetState,
            TimestampUtc = updated.UpdatedUtc,
            Message = message
        };

        return TransitionResult.FromSuccess(updated, @event);
    }
}

public sealed record TransitionResult(bool Success, JobRecord Job, JobEvent? Event, string? Error)
{
    public static TransitionResult Failure(JobRecord job, string error) =>
        new(false, job, null, error);

    public static TransitionResult FromSuccess(JobRecord job, JobEvent @event) =>
        new(true, job, @event, null);
}

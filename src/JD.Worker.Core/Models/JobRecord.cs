using System;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;

namespace JD.Worker.Core;

public sealed record JobRecord
{
    public string JobId { get; init; } = string.Empty;
    public int Attempt { get; init; } = 1;
    public JobState State { get; init; } = JobState.Received;
    public JobEnvelope Envelope { get; init; } = new();
    public string? Source { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record JobEvent
{
    public string JobId { get; init; } = string.Empty;
    public int Attempt { get; init; }
    public JobState FromState { get; init; }
    public JobState ToState { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? Message { get; init; }
}

using System;
using System.Collections.Generic;

namespace JD.Worker.Core;

public enum WorkerStatus
{
    Unknown,
    Idle,
    Running,
    Busy
}

public sealed record WorkerRegistration
{
    public string WorkerId { get; init; } = string.Empty;
    public string? Pool { get; init; }
    public IReadOnlyDictionary<string, string>? Labels { get; init; }
    public WorkerStatus Status { get; init; } = WorkerStatus.Unknown;
    public string? CurrentJobId { get; init; }
    public DateTimeOffset LastHeartbeatUtc { get; init; } = DateTimeOffset.UtcNow;
}

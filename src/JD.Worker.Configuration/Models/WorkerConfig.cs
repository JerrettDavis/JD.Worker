using System;
using System.Collections.Generic;

namespace JD.Worker.Configuration;

public sealed class WorkerConfig
{
    public WorkerSettings Worker { get; set; } = new();
    public List<CncConnectorConfig> Cnc { get; set; } = new();
    public PolicyConfig? Policy { get; set; }
}

public sealed class WorkerSettings
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, object>? Labels { get; set; }
    public List<PoolSettings> Pools { get; set; } = new();
    public WorkspaceSettings? Workspace { get; set; }
    public SandboxMode Sandbox { get; set; } = SandboxMode.None;
}

public sealed class PoolSettings
{
    public string Name { get; set; } = string.Empty;
    public int Concurrency { get; set; }
}

public sealed class WorkspaceSettings
{
    public string Root { get; set; } = string.Empty;
    public CleanupPolicy Cleanup { get; set; } = CleanupPolicy.Immediate;
}

public enum CleanupPolicy
{
    Immediate,
    RetainOnFailure,
    TimeBased,
    Manual
}

public enum SandboxMode
{
    None,
    WorkspaceOnly,
    Container
}

public sealed class CncConnectorConfig
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object?>? Settings { get; set; }
}

public sealed class PolicyConfig
{
    public List<string> AllowedStepTypes { get; set; } = new();
}

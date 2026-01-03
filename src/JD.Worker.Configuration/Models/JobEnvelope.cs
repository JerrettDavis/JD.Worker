using System;
using System.Collections.Generic;
using JD.Worker.Abstractions;

namespace JD.Worker.Configuration;

public sealed class JobEnvelope : IJobEnvelope
{
    public JobEnvelopeMetadata Envelope { get; set; } = new();
    public JobPayload Payload { get; set; } = new();

    string IJobEnvelope.JobId => Envelope.JobId;
    int IJobEnvelope.Attempt => Envelope.Attempt;
    DateTimeOffset IJobEnvelope.CreatedUtc => Envelope.CreatedUtc;
    string? IJobEnvelope.Signature => Envelope.Signature;
    string? IJobEnvelope.SignatureAlgorithm => Envelope.SignatureAlgorithm;
    string? IJobEnvelope.KeyId => Envelope.KeyId;
    IJobPayload IJobEnvelope.Payload => Payload;
}

public sealed class JobEnvelopeMetadata
{
    public string JobId { get; set; } = string.Empty;
    public int Attempt { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? Signature { get; set; }
    public string? SignatureAlgorithm { get; set; }
    public string? KeyId { get; set; }
}

public sealed class JobPayload : IJobPayload
{
    public string? RequestedPool { get; set; }
    public JobRequirements? Requirements { get; set; }
    public List<StepDefinition> Steps { get; set; } = new();
    public List<ArtifactRule>? Artifacts { get; set; }
    public TimeSpan? Timeout { get; set; }
    public RetryConfig? Retry { get; set; }
    public List<ResultSink>? ResultSinks { get; set; }

    IJobRequirements? IJobPayload.Requirements => Requirements;
    IReadOnlyList<IStepDefinition> IJobPayload.Steps => Steps;
    IReadOnlyList<IArtifactRule>? IJobPayload.Artifacts => Artifacts;
    IRetryConfig? IJobPayload.Retry => Retry;
    IReadOnlyList<IResultSink>? IJobPayload.ResultSinks => ResultSinks;
}

public sealed class JobRequirements : IJobRequirements
{
    public List<string>? Labels { get; set; }
    public string? Os { get; set; }
    public string? Arch { get; set; }
    public bool DockerRequired { get; set; }

    IReadOnlyList<string>? IJobRequirements.Labels => Labels;
}

public sealed class StepDefinition : IStepDefinition
{
    public string Name { get; set; } = string.Empty;
    public StepType Type { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string>? Environment { get; set; }
    public TimeSpan? Timeout { get; set; }
    public RetryConfig? Retry { get; set; }
    public List<OutputCapture>? Outputs { get; set; }
    public List<string>? Arguments { get; set; }
    public string? Image { get; set; }
    public string? Dockerfile { get; set; }
    public string? BuildContext { get; set; }
    public Dictionary<string, string>? BuildArgs { get; set; }
    public List<string>? RunArgs { get; set; }

    IReadOnlyDictionary<string, string>? IStepDefinition.Environment => Environment;
    IReadOnlyList<string>? IStepDefinition.Arguments => Arguments;
    IRetryConfig? IStepDefinition.Retry => Retry;
    IReadOnlyList<IOutputCapture>? IStepDefinition.Outputs => Outputs;
    string? IStepDefinition.Image => Image;
    string? IStepDefinition.Dockerfile => Dockerfile;
    string? IStepDefinition.BuildContext => BuildContext;
    IReadOnlyDictionary<string, string>? IStepDefinition.BuildArgs => BuildArgs;
    IReadOnlyList<string>? IStepDefinition.RunArgs => RunArgs;
}

public sealed class RetryConfig : IRetryConfig
{
    public int MaxAttempts { get; set; }
    public TimeSpan InitialDelay { get; set; }
    public TimeSpan MaxDelay { get; set; }
    public BackoffStrategy Backoff { get; set; }
}

public sealed class ArtifactRule : IArtifactRule
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Store { get; set; } = string.Empty;
}

public sealed class OutputCapture : IOutputCapture
{
    public string Key { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
}

public sealed class ResultSink : IResultSink
{
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object?>? Settings { get; set; }
}

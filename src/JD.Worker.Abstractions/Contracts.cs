using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JD.Worker.Abstractions;

#region Job Contracts

/// <summary>
/// Represents the state of a job in its lifecycle.
/// </summary>
public enum JobState
{
    /// <summary>Job received but not yet validated.</summary>
    Received,

    /// <summary>Job passed validation and accepted for processing.</summary>
    Accepted,

    /// <summary>Job rejected due to policy or signature failure.</summary>
    Rejected,

    /// <summary>Lease acquired, ready for execution.</summary>
    Leased,

    /// <summary>Job is currently executing.</summary>
    Running,

    /// <summary>Job completed successfully.</summary>
    Succeeded,

    /// <summary>Job failed during execution.</summary>
    Failed,

    /// <summary>Job was cancelled before completion.</summary>
    Canceled,

    /// <summary>Job exceeded retries and was moved to dead letter.</summary>
    DeadLettered,

    /// <summary>Job results published and cleanup complete.</summary>
    Finalized
}

/// <summary>
/// Represents a signed job envelope containing metadata and payload.
/// </summary>
public interface IJobEnvelope
{
    /// <summary>Unique identifier for this job.</summary>
    string JobId { get; }

    /// <summary>Attempt number (1-based).</summary>
    int Attempt { get; }

    /// <summary>UTC timestamp when the job was created.</summary>
    DateTimeOffset CreatedUtc { get; }

    /// <summary>Cryptographic signature over the payload.</summary>
    string? Signature { get; }

    /// <summary>Algorithm used for signature (e.g., "HMAC-SHA256").</summary>
    string? SignatureAlgorithm { get; }

    /// <summary>Identifier of the key used for signing.</summary>
    string? KeyId { get; }

    /// <summary>The job payload containing steps and configuration.</summary>
    IJobPayload Payload { get; }
}

/// <summary>
/// The payload of a job containing execution instructions.
/// </summary>
public interface IJobPayload
{
    /// <summary>Target pool for job execution.</summary>
    string? RequestedPool { get; }

    /// <summary>Requirements that workers must meet.</summary>
    IJobRequirements? Requirements { get; }

    /// <summary>Steps to execute in sequence.</summary>
    IReadOnlyList<IStepDefinition> Steps { get; }

    /// <summary>Artifact upload rules.</summary>
    IReadOnlyList<IArtifactRule>? Artifacts { get; }

    /// <summary>Total job timeout.</summary>
    TimeSpan? Timeout { get; }

    /// <summary>Retry configuration.</summary>
    IRetryConfig? Retry { get; }

    /// <summary>Destinations for job results.</summary>
    IReadOnlyList<IResultSink>? ResultSinks { get; }
}

/// <summary>
/// Requirements a worker must meet to execute the job.
/// </summary>
public interface IJobRequirements
{
    /// <summary>Required worker labels.</summary>
    IReadOnlyList<string>? Labels { get; }

    /// <summary>Required operating system.</summary>
    string? Os { get; }

    /// <summary>Required CPU architecture.</summary>
    string? Arch { get; }

    /// <summary>Whether Docker is required.</summary>
    bool DockerRequired { get; }
}

/// <summary>
/// Result of job execution.
/// </summary>
public interface IJobResult
{
    /// <summary>Job identifier.</summary>
    string JobId { get; }

    /// <summary>Attempt number.</summary>
    int Attempt { get; }

    /// <summary>Final state of the job.</summary>
    JobState State { get; }

    /// <summary>Completion timestamp.</summary>
    DateTimeOffset CompletedUtc { get; }

    /// <summary>Duration of execution.</summary>
    TimeSpan Duration { get; }

    /// <summary>Error message if failed.</summary>
    string? ErrorMessage { get; }

    /// <summary>Results from individual steps.</summary>
    IReadOnlyList<IStepResult> StepResults { get; }

    /// <summary>Uploaded artifact references.</summary>
    IReadOnlyList<IArtifactReference>? Artifacts { get; }
}

#endregion

#region Step Contracts

/// <summary>
/// Types of steps that can be executed.
/// </summary>
public enum StepType
{
    /// <summary>Execute a shell command.</summary>
    Shell,

    /// <summary>Execute a process directly.</summary>
    Process,

    /// <summary>Execute PowerShell script.</summary>
    PowerShell,

    /// <summary>Execute .NET CLI command.</summary>
    DotNet,

    /// <summary>Execute in a Docker container.</summary>
    Docker
}

/// <summary>
/// Definition of a step to execute.
/// </summary>
public interface IStepDefinition
{
    /// <summary>Step name for logging and identification.</summary>
    string Name { get; }

    /// <summary>Type of step.</summary>
    StepType Type { get; }

      /// <summary>Command or script to execute.</summary>
      string Command { get; }

      /// <summary>Arguments passed to the command.</summary>
      IReadOnlyList<string>? Arguments { get; }

      /// <summary>Working directory relative to workspace.</summary>
      string? WorkingDirectory { get; }

    /// <summary>Environment variables to set.</summary>
    IReadOnlyDictionary<string, string>? Environment { get; }

    /// <summary>Maximum execution time.</summary>
    TimeSpan? Timeout { get; }

    /// <summary>Step-level retry configuration.</summary>
    IRetryConfig? Retry { get; }

      /// <summary>Output capture patterns.</summary>
      IReadOnlyList<IOutputCapture>? Outputs { get; }

      /// <summary>Container image for docker steps.</summary>
      string? Image { get; }

      /// <summary>Dockerfile path used for docker build.</summary>
      string? Dockerfile { get; }

      /// <summary>Build context path used for docker build.</summary>
      string? BuildContext { get; }

      /// <summary>Build arguments passed to docker build.</summary>
      IReadOnlyDictionary<string, string>? BuildArgs { get; }

      /// <summary>Additional arguments passed to docker run.</summary>
      IReadOnlyList<string>? RunArgs { get; }
  }

/// <summary>
/// Runs steps of a specific type.
/// </summary>
public interface IStepRunner
{
    /// <summary>Step types this runner handles.</summary>
    IReadOnlySet<StepType> SupportedTypes { get; }

    /// <summary>Execute a step.</summary>
    /// <param name="step">Step definition to execute.</param>
    /// <param name="context">Workspace context for execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of step execution.</returns>
    Task<IStepResult> ExecuteAsync(
        IStepDefinition step,
        IWorkspaceContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of step execution.
/// </summary>
public interface IStepResult
{
    /// <summary>Step name.</summary>
    string StepName { get; }

    /// <summary>Whether the step succeeded.</summary>
    bool Success { get; }

    /// <summary>Exit code if applicable.</summary>
    int? ExitCode { get; }

    /// <summary>Error message if failed.</summary>
    string? ErrorMessage { get; }

    /// <summary>Execution duration.</summary>
    TimeSpan Duration { get; }

    /// <summary>Captured outputs.</summary>
    IReadOnlyDictionary<string, string>? Outputs { get; }
}

#endregion

#region Connector Contracts

/// <summary>
/// Declares the capabilities of a CnC connector.
/// </summary>
public sealed record ConnectorCapabilities
{
    /// <summary>Connector can accept pushed jobs.</summary>
    public bool SupportsPush { get; init; }

    /// <summary>Connector can poll for jobs.</summary>
    public bool SupportsPull { get; init; }

    /// <summary>Connector supports lease acquisition.</summary>
    public bool SupportsLeases { get; init; }

    /// <summary>Connector supports ack/nack semantics.</summary>
    public bool SupportsAckNack { get; init; }

    /// <summary>Connector supports dead letter queues.</summary>
    public bool SupportsDeadLetter { get; init; }

    /// <summary>Connector supports heartbeat/keepalive.</summary>
    public bool SupportsHeartbeat { get; init; }

    /// <summary>Maximum message size in bytes.</summary>
    public long MaxMessageSize { get; init; }

    /// <summary>Default visibility timeout if applicable.</summary>
    public TimeSpan? DefaultVisibilityTimeout { get; init; }
}

/// <summary>
/// Handle to an acquired lease.
/// </summary>
public sealed record LeaseHandle(
    string JobId,
    string LeaseId,
    DateTimeOffset AcquiredUtc,
    DateTimeOffset ExpiresUtc)
{
    /// <summary>Whether the lease is still valid.</summary>
    public bool IsValid => DateTimeOffset.UtcNow < ExpiresUtc;
}

/// <summary>
/// Command and control connector for job transport.
/// </summary>
public interface ICncConnector : IAsyncDisposable
{
    /// <summary>Connector name for configuration.</summary>
    string Name { get; }

    /// <summary>Declared capabilities.</summary>
    ConnectorCapabilities Capabilities { get; }

    /// <summary>Poll for available jobs.</summary>
    IAsyncEnumerable<IJobEnvelope> PollAsync(CancellationToken cancellationToken);

    /// <summary>Start listening for pushed jobs.</summary>
    Task StartListeningAsync(
        Func<IJobEnvelope, Task> handler,
        CancellationToken cancellationToken);

    /// <summary>Attempt to acquire a lease on a job.</summary>
    Task<LeaseHandle?> TryAcquireLeaseAsync(
        string jobId,
        TimeSpan duration,
        CancellationToken cancellationToken);

    /// <summary>Renew an existing lease.</summary>
    Task RenewLeaseAsync(
        LeaseHandle lease,
        TimeSpan extension,
        CancellationToken cancellationToken);

    /// <summary>Release a lease without completing the job.</summary>
    Task ReleaseLeaseAsync(
        LeaseHandle lease,
        CancellationToken cancellationToken);

    /// <summary>Acknowledge successful job completion.</summary>
    Task AcknowledgeAsync(
        string jobId,
        CancellationToken cancellationToken);

    /// <summary>Negative acknowledge (requeue or dead letter).</summary>
    Task NegativeAcknowledgeAsync(
        string jobId,
        bool requeue,
        CancellationToken cancellationToken);

    /// <summary>Publish job result.</summary>
    Task PublishResultAsync(
        IJobResult result,
        CancellationToken cancellationToken);
}

/// <summary>
/// Log entry emitted during job execution.
/// </summary>
public sealed record JobLogEntry(
    string JobId,
    int Attempt,
    DateTimeOffset TimestampUtc,
    LogLevel Level,
    string Message,
    string? WorkerId);

/// <summary>
/// Optional connector interface for reporting job state changes.
/// </summary>
public interface IJobStatusReporter
{
    /// <summary>Notify the CnC plane that a job has started running.</summary>
    Task ReportJobStartedAsync(
        string jobId,
        string? workerId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional connector interface for publishing job log output.
/// </summary>
public interface IJobLogPublisher
{
    /// <summary>Publish a log line for a job.</summary>
    Task PublishLogAsync(
        JobLogEntry entry,
        CancellationToken cancellationToken);
}

/// <summary>
/// Factory for creating connectors from configuration.
/// </summary>
public interface IConnectorFactory
{
    /// <summary>Connector type name (e.g., "Local", "Http", "AzureServiceBus").</summary>
    string TypeName { get; }

    /// <summary>Create a connector from configuration.</summary>
    ICncConnector Create(IReadOnlyDictionary<string, object?> settings);
}

#endregion

#region Secret Contracts

/// <summary>
/// Reference to a secret value.
/// </summary>
public sealed record SecretReference(string Provider, string Key);

/// <summary>
/// Provider for secret values.
/// </summary>
public interface ISecretProvider
{
    /// <summary>Provider name for configuration.</summary>
    string Name { get; }

    /// <summary>Retrieve a secret value.</summary>
    ValueTask<string?> GetSecretAsync(string key, CancellationToken cancellationToken);

    /// <summary>Check if a secret exists.</summary>
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}

#endregion

#region Artifact Contracts

/// <summary>
/// Descriptor for an artifact file.
/// </summary>
public sealed record ArtifactDescriptor(
    string Name,
    string Path,
    long SizeBytes,
    string ContentType);

/// <summary>
/// Reference to an uploaded artifact.
/// </summary>
public interface IArtifactReference
{
    /// <summary>Artifact name.</summary>
    string Name { get; }

    /// <summary>Store where uploaded.</summary>
    string Store { get; }

    /// <summary>URI or path to artifact.</summary>
    string Location { get; }

    /// <summary>Size in bytes.</summary>
    long SizeBytes { get; }
}

/// <summary>
/// Store for artifact upload/download.
/// </summary>
public interface IArtifactStore
{
    /// <summary>Store name for configuration.</summary>
    string Name { get; }

    /// <summary>Upload an artifact.</summary>
    Task<IArtifactReference> UploadAsync(
        string jobId,
        int attempt,
        ArtifactDescriptor descriptor,
        Stream content,
        CancellationToken cancellationToken);

    /// <summary>Download an artifact.</summary>
    Task<Stream> DownloadAsync(
        IArtifactReference reference,
        CancellationToken cancellationToken);
}

#endregion

#region Workspace Contracts

/// <summary>
/// Context for step execution within a workspace.
/// </summary>
public interface IWorkspaceContext
{
    /// <summary>Job identifier.</summary>
    string JobId { get; }

    /// <summary>Attempt number.</summary>
    int Attempt { get; }

    /// <summary>Root working directory.</summary>
    string WorkDir { get; }

    /// <summary>Artifacts output directory.</summary>
    string ArtifactsDir { get; }

    /// <summary>Logs directory.</summary>
    string LogsDir { get; }

    /// <summary>Sink for writing logs.</summary>
    ILogSink LogSink { get; }

    /// <summary>Resolve a secret value.</summary>
    ValueTask<string?> ResolveSecretAsync(SecretReference reference, CancellationToken cancellationToken);

    /// <summary>Set an output value for downstream steps.</summary>
    void SetOutput(string key, string value);

    /// <summary>Get an output value from previous steps.</summary>
    string? GetOutput(string key);
}

/// <summary>
/// Sink for log output with redaction.
/// </summary>
public interface ILogSink
{
    /// <summary>Write a log line.</summary>
    ValueTask WriteAsync(LogLevel level, string message, CancellationToken cancellationToken);

    /// <summary>Write stdout content.</summary>
    ValueTask WriteStdOutAsync(string content, CancellationToken cancellationToken);

    /// <summary>Write stderr content.</summary>
    ValueTask WriteStdErrAsync(string content, CancellationToken cancellationToken);
}

/// <summary>
/// Log severity levels.
/// </summary>
public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

#endregion

#region Supporting Types

/// <summary>
/// Retry configuration.
/// </summary>
public interface IRetryConfig
{
    /// <summary>Maximum retry attempts.</summary>
    int MaxAttempts { get; }

    /// <summary>Initial delay between retries.</summary>
    TimeSpan InitialDelay { get; }

    /// <summary>Maximum delay between retries.</summary>
    TimeSpan MaxDelay { get; }

    /// <summary>Backoff strategy.</summary>
    BackoffStrategy Backoff { get; }
}

/// <summary>
/// Backoff strategy for retries.
/// </summary>
public enum BackoffStrategy
{
    /// <summary>Fixed delay between retries.</summary>
    Fixed,

    /// <summary>Linear increase in delay.</summary>
    Linear,

    /// <summary>Exponential increase in delay.</summary>
    Exponential
}

/// <summary>
/// Rule for artifact upload.
/// </summary>
public interface IArtifactRule
{
    /// <summary>Artifact name.</summary>
    string Name { get; }

    /// <summary>Glob pattern for files to include.</summary>
    string Path { get; }

    /// <summary>Target store name.</summary>
    string Store { get; }
}

/// <summary>
/// Output capture configuration.
/// </summary>
public interface IOutputCapture
{
    /// <summary>Output key name.</summary>
    string Key { get; }

    /// <summary>Regex pattern to extract value.</summary>
    string Pattern { get; }
}

/// <summary>
/// Destination for job results.
/// </summary>
public interface IResultSink
{
    /// <summary>Sink type (e.g., "http", "connector").</summary>
    string Type { get; }
}

#endregion

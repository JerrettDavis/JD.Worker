using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using Microsoft.Extensions.Logging;
using WorkerLogLevel = JD.Worker.Abstractions.LogLevel;

namespace JD.Worker.Core;

public sealed class JobExecutor : IJobExecutor
{
    private readonly IJobStore _jobStore;
    private readonly JobStateMachine _stateMachine;
    private readonly StepExecutor _stepExecutor;
    private readonly WorkspaceManager _workspaceManager;
    private readonly WorkerConfig _config;
    private readonly IConnectorRegistry _connectorRegistry;
    private readonly WorkerStatusTracker _statusTracker;
    private readonly ILogger<JobExecutor> _logger;

    public JobExecutor(
        IJobStore jobStore,
        JobStateMachine stateMachine,
        StepExecutor stepExecutor,
        WorkspaceManager workspaceManager,
        WorkerConfig config,
        IConnectorRegistry connectorRegistry,
        WorkerStatusTracker statusTracker,
        ILogger<JobExecutor> logger)
    {
        _jobStore = jobStore;
        _stateMachine = stateMachine;
        _stepExecutor = stepExecutor;
        _workspaceManager = workspaceManager;
        _config = config;
        _connectorRegistry = connectorRegistry;
        _statusTracker = statusTracker;
        _logger = logger;
    }

    public async Task ExecuteAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetAsync(jobId, cancellationToken);
        if (job is null)
        {
            _logger.LogWarning("Job {JobId} not found in store.", jobId);
            return;
        }

        var pool = ResolvePool(job);

        job = await TransitionAsync(job, JobState.Leased, "Lease acquired.", cancellationToken);
        if (job is null)
        {
            return;
        }

        job = await TransitionAsync(job, JobState.Running, "Execution started.", cancellationToken);
        if (job is null)
        {
            return;
        }

        await ReportJobStartedAsync(job, cancellationToken);
        var cleanupPolicy = _config.Worker.Workspace?.Cleanup ?? CleanupPolicy.Immediate;
        var finalState = JobState.Succeeded;
        var finalMessage = "Job completed successfully.";
        var startedUtc = DateTimeOffset.UtcNow;
        var stepResults = new List<IStepResult>();
        WorkspaceHandle? workspace = null;

        try
        {
            _statusTracker.SetStatus(WorkerStatus.Running);
            _statusTracker.SetCurrentJobId(job.JobId);
            var logPublisher = GetLogPublisher(job);
            workspace = await _workspaceManager.CreateAsync(
                job.JobId,
                job.Attempt,
                pool,
                cancellationToken,
                logPublisher,
                _config.Worker.Id);

            var stepIndex = 0;
            foreach (var step in job.Envelope.Payload.Steps)
            {
                stepIndex++;
                var stepLabel = GetStepLabel(step, stepIndex - 1);
                await PublishStepMarkerAsync(
                    logPublisher,
                    job,
                    stepLabel,
                    isStart: true,
                    success: null,
                    cancellationToken);

                var result = await _stepExecutor.ExecuteAsync(step, workspace.Context, cancellationToken);
                stepResults.Add(result);
                await PublishStepMarkerAsync(
                    logPublisher,
                    job,
                    stepLabel,
                    isStart: false,
                    success: result.Success,
                    cancellationToken);

                if (!result.Success)
                {
                    finalState = JobState.Failed;
                    finalMessage = result.ErrorMessage ?? $"Step '{result.StepName}' failed.";
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            finalState = JobState.Canceled;
            finalMessage = "Job canceled.";
        }
        catch (Exception ex)
        {
            finalState = JobState.Failed;
            finalMessage = ex.Message;
        }
        finally
        {
            if (workspace is not null)
            {
                await _workspaceManager.CleanupAsync(workspace.Paths, cleanupPolicy, finalState, cancellationToken);
            }

            _statusTracker.SetCurrentJobId(null);
            _statusTracker.SetStatus(WorkerStatus.Idle);
        }

        var completedUtc = DateTimeOffset.UtcNow;
        var duration = completedUtc - startedUtc;
        job = await TransitionAsync(job, finalState, finalMessage, cancellationToken);
        if (job is null)
        {
            return;
        }

        await PublishResultAsync(job, finalState, completedUtc, duration, finalMessage, stepResults, cancellationToken);
        await PublishOutcomeAsync(job, finalState, cancellationToken);

        await TransitionAsync(job, JobState.Finalized, "Job finalized.", cancellationToken);
    }

    private string ResolvePool(JobRecord job)
    {
        var pools = _config.Worker.Pools;
        var requested = job.Envelope.Payload.RequestedPool;
        if (!string.IsNullOrWhiteSpace(requested) &&
            pools.Any(pool => string.Equals(pool.Name, requested, StringComparison.OrdinalIgnoreCase)))
        {
            return requested;
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            _logger.LogWarning("Requested pool {Pool} not found; using default.", requested);
        }

        return pools.FirstOrDefault()?.Name ?? "default";
    }

    private async Task<JobRecord?> TransitionAsync(
        JobRecord job,
        JobState targetState,
        string message,
        CancellationToken cancellationToken)
    {
        var transition = _stateMachine.Transition(job, targetState, message);
        if (!transition.Success || transition.Event is null)
        {
            _logger.LogWarning("Job {JobId} invalid transition from {From} to {To}.", job.JobId, job.State, targetState);
            return null;
        }

        await _jobStore.AppendEventAsync(transition.Event, cancellationToken);
        await _jobStore.SaveAsync(transition.Job, cancellationToken);
        return transition.Job;
    }

    private async Task PublishOutcomeAsync(JobRecord job, JobState finalState, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.Source))
        {
            return;
        }

        if (!_connectorRegistry.TryGet(job.Source, out var connector))
        {
            _logger.LogWarning("Connector {Connector} not found for job {JobId}.", job.Source, job.JobId);
            return;
        }

        if (!connector.Capabilities.SupportsAckNack)
        {
            return;
        }

        try
        {
            if (finalState == JobState.Succeeded)
            {
                await connector.AcknowledgeAsync(job.JobId, cancellationToken);
            }
            else
            {
                await connector.NegativeAcknowledgeAsync(job.JobId, requeue: false, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish outcome for job {JobId} via connector {Connector}.", job.JobId, job.Source);
        }
    }

    private async Task PublishResultAsync(
        JobRecord job,
        JobState finalState,
        DateTimeOffset completedUtc,
        TimeSpan duration,
        string? finalMessage,
        IReadOnlyList<IStepResult> stepResults,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.Source))
        {
            return;
        }

        if (!_connectorRegistry.TryGet(job.Source, out var connector))
        {
            return;
        }

        try
        {
            var errorMessage = finalState == JobState.Succeeded ? null : finalMessage;
            var result = new JobResult(
                job.JobId,
                job.Attempt,
                finalState,
                completedUtc,
                duration,
                errorMessage,
                stepResults,
                Artifacts: null);
            await connector.PublishResultAsync(result, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish result for job {JobId} via connector {Connector}.", job.JobId, job.Source);
        }
    }

    private async Task ReportJobStartedAsync(JobRecord job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.Source))
        {
            return;
        }

        if (!_connectorRegistry.TryGet(job.Source, out var connector))
        {
            return;
        }

        if (connector is not IJobStatusReporter reporter)
        {
            return;
        }

        try
        {
            await reporter.ReportJobStartedAsync(job.JobId, _config.Worker.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report job start for {JobId} via connector {Connector}.", job.JobId, job.Source);
        }
    }

    private IJobLogPublisher? GetLogPublisher(JobRecord job)
    {
        if (string.IsNullOrWhiteSpace(job.Source))
        {
            return null;
        }

        return _connectorRegistry.TryGet(job.Source, out var connector)
            ? connector as IJobLogPublisher
            : null;
    }

    private async Task PublishStepMarkerAsync(
        IJobLogPublisher? publisher,
        JobRecord job,
        string stepLabel,
        bool isStart,
        bool? success,
        CancellationToken cancellationToken)
    {
        if (publisher is null)
        {
            return;
        }

        var marker = isStart
            ? $"[step:start] {stepLabel}"
            : $"[step:end] {stepLabel} {(success == true ? "success" : "failed")}";
        var level = success == false ? WorkerLogLevel.Error : WorkerLogLevel.Information;
        await PublishJobLogAsync(publisher, job, level, marker, cancellationToken);
    }

    private async Task PublishJobLogAsync(
        IJobLogPublisher publisher,
        JobRecord job,
        WorkerLogLevel level,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = new JobLogEntry(
                job.JobId,
                job.Attempt,
                DateTimeOffset.UtcNow,
                level,
                message,
                _config.Worker.Id);
            await publisher.PublishLogAsync(entry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish job log marker for {JobId}.", job.JobId);
        }
    }

    private static string GetStepLabel(StepDefinition step, int index) =>
        string.IsNullOrWhiteSpace(step.Name) ? $"step-{index + 1:00}" : step.Name;
}

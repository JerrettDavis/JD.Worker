using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using WorkerLogLevel = JD.Worker.Abstractions.LogLevel;
using Microsoft.Extensions.Logging;

namespace JD.Worker.Core;

public sealed class WorkspaceManagerOptions
{
    public string RootPath { get; set; } = "workspaces";
}

public sealed record WorkspacePaths(
    string Root,
    string WorkDir,
    string ArtifactsDir,
    string LogsDir);

public sealed record WorkspaceHandle(
    IWorkspaceContext Context,
    WorkspacePaths Paths);

public sealed class WorkspaceManager
{
    private readonly WorkspaceManagerOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SecretRedactor _redactor;

    public WorkspaceManager(
        WorkspaceManagerOptions options,
        ILoggerFactory loggerFactory,
        SecretRedactor redactor)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _redactor = redactor;
    }

    public async Task<WorkspaceHandle> CreateAsync(
        string jobId,
        int attempt,
        string pool,
        CancellationToken cancellationToken,
        IJobLogPublisher? logPublisher = null,
        string? workerId = null)
    {
        var paths = BuildPaths(jobId, attempt, pool);

        Directory.CreateDirectory(paths.WorkDir);
        Directory.CreateDirectory(paths.ArtifactsDir);
        Directory.CreateDirectory(paths.LogsDir);

        ILogSink logSink = new WorkspaceLogSink(
            _loggerFactory.CreateLogger<WorkspaceLogSink>(),
            _redactor,
            jobId,
            attempt);
        if (logPublisher is not null)
        {
            logSink = new JobLogSink(logSink, _redactor, jobId, attempt, workerId, logPublisher);
        }

        var context = new WorkspaceContext(
            jobId,
            attempt,
            paths,
            logSink,
            _redactor,
            secretResolver: null);

        await logSink.WriteAsync(
            WorkerLogLevel.Information,
            $"Workspace prepared at {paths.Root}.",
            cancellationToken);

        return new WorkspaceHandle(context, paths);
    }

    public Task CleanupAsync(
        WorkspacePaths paths,
        CleanupPolicy policy,
        JobState finalState,
        CancellationToken cancellationToken)
    {
        if (policy == CleanupPolicy.Immediate || (policy == CleanupPolicy.RetainOnFailure && finalState == JobState.Succeeded))
        {
            if (Directory.Exists(paths.Root))
            {
                Directory.Delete(paths.Root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    private WorkspacePaths BuildPaths(string jobId, int attempt, string pool)
    {
        var root = Path.Combine(_options.RootPath, pool, jobId, attempt.ToString());
        return new WorkspacePaths(
            root,
            Path.Combine(root, "work"),
            Path.Combine(root, "artifacts"),
            Path.Combine(root, "logs"));
    }
}

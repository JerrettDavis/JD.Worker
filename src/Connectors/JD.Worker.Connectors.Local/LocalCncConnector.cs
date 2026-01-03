using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using Microsoft.Extensions.Logging;

namespace JD.Worker.Connectors.Local;

public sealed class LocalCncConnector : ICncConnector
{
    private static readonly string[] SupportedExtensions = [".json", ".yml", ".yaml"];
    private readonly LocalConnectorOptions _options;
    private readonly ILogger<LocalCncConnector> _logger;
    private readonly ConcurrentDictionary<string, LocalJobHandle> _handles = new();

    internal LocalCncConnector(LocalConnectorOptions options, ILogger<LocalCncConnector> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string Name => _options.Name;

    public ConnectorCapabilities Capabilities { get; } = new()
    {
        SupportsPull = true,
        SupportsAckNack = true
    };

    public async IAsyncEnumerable<IJobEnvelope> PollAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureDirectories();

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var file in EnumerateInboxFiles())
            {
                var handle = TryReserve(file);
                if (handle is null)
                {
                    continue;
                }

                var envelope = await TryReadEnvelopeAsync(handle, cancellationToken);
                if (envelope is null)
                {
                    MoveToFailed(handle, "invalid");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(envelope.Envelope.JobId))
                {
                    MoveToFailed(handle, "missing-jobid");
                    continue;
                }

                if (!_handles.TryAdd(envelope.Envelope.JobId, handle))
                {
                    MoveToFailed(handle, "duplicate-jobid");
                    continue;
                }

                yield return envelope;
            }

            await Task.Delay(_options.PollInterval, cancellationToken);
        }
    }

    public Task StartListeningAsync(Func<IJobEnvelope, Task> handler, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Local connector does not support push listeners.");
        return Task.CompletedTask;
    }

    public Task<LeaseHandle?> TryAcquireLeaseAsync(string jobId, TimeSpan duration, CancellationToken cancellationToken) =>
        Task.FromResult<LeaseHandle?>(null);

    public Task RenewLeaseAsync(LeaseHandle lease, TimeSpan extension, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ReleaseLeaseAsync(LeaseHandle lease, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task AcknowledgeAsync(string jobId, CancellationToken cancellationToken)
    {
        if (!_handles.TryRemove(jobId, out var handle))
        {
            return Task.CompletedTask;
        }

        MoveToOutbox(handle, "completed");
        return Task.CompletedTask;
    }

    public Task NegativeAcknowledgeAsync(string jobId, bool requeue, CancellationToken cancellationToken)
    {
        if (!_handles.TryRemove(jobId, out var handle))
        {
            return Task.CompletedTask;
        }

        if (requeue)
        {
            MoveToInbox(handle);
        }
        else
        {
            MoveToOutbox(handle, "failed");
        }

        return Task.CompletedTask;
    }

    public Task PublishResultAsync(IJobResult result, CancellationToken cancellationToken)
    {
        if (_handles.TryGetValue(result.JobId, out var handle))
        {
            WriteResultFile(handle, result);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_options.InboxPath);
        Directory.CreateDirectory(_options.OutboxPath);
    }

    private IEnumerable<string> EnumerateInboxFiles()
    {
        if (!Directory.Exists(_options.InboxPath))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.EnumerateFiles(_options.InboxPath)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }

    private LocalJobHandle? TryReserve(string path)
    {
        var processingPath = path + ".processing";
        try
        {
            File.Move(path, processingPath);
            return new LocalJobHandle(path, processingPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<JobEnvelope?> TryReadEnvelopeAsync(LocalJobHandle handle, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(handle.ProcessingPath, cancellationToken);
            var parser = CreateParser(handle.OriginalPath);
            var result = parser.Parse<JobEnvelope>(content);
            if (!result.IsSuccess)
            {
                _logger.LogWarning("Failed to parse job envelope from {Path}.", handle.OriginalPath);
                return null;
            }

            return result.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read job envelope from {Path}.", handle.OriginalPath);
            return null;
        }
    }

    private static IConfigParser CreateParser(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".yml" or ".yaml" ? new YamlConfigParser() : new JsonConfigParser();
    }

    private void MoveToOutbox(LocalJobHandle handle, string bucket)
    {
        var destinationDir = Path.Combine(_options.OutboxPath, bucket);
        Directory.CreateDirectory(destinationDir);
        var destinationPath = GetUniquePath(destinationDir, Path.GetFileName(handle.OriginalPath));
        File.Move(handle.ProcessingPath, destinationPath, overwrite: false);
    }

    private void MoveToFailed(LocalJobHandle handle, string reason)
    {
        var destinationDir = Path.Combine(_options.OutboxPath, "failed");
        Directory.CreateDirectory(destinationDir);
        var fileName = $"{Path.GetFileName(handle.OriginalPath)}.{reason}";
        var destinationPath = GetUniquePath(destinationDir, fileName);
        File.Move(handle.ProcessingPath, destinationPath, overwrite: false);
    }

    private void MoveToInbox(LocalJobHandle handle)
    {
        Directory.CreateDirectory(_options.InboxPath);
        var destinationPath = GetUniquePath(_options.InboxPath, Path.GetFileName(handle.OriginalPath));
        File.Move(handle.ProcessingPath, destinationPath, overwrite: false);
    }

    private void WriteResultFile(LocalJobHandle handle, IJobResult result)
    {
        var destinationDir = Path.Combine(_options.OutboxPath, "results");
        Directory.CreateDirectory(destinationDir);
        var destinationPath = GetUniquePath(destinationDir, $"{result.JobId}.result.txt");
        File.WriteAllText(destinationPath, $"{result.State} {result.ErrorMessage}");
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var index = 1;
        while (true)
        {
            var attempt = Path.Combine(directory, $"{name}-{index}{ext}");
            if (!File.Exists(attempt))
            {
                return attempt;
            }

            index++;
        }
    }

    private sealed record LocalJobHandle(string OriginalPath, string ProcessingPath);
}

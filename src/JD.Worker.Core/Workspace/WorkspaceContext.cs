using System;
using System.Collections.Concurrent;
using System.Threading;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class WorkspaceContext : IWorkspaceContext
{
    private readonly ConcurrentDictionary<string, string> _outputs = new();
    private readonly SecretRedactor _redactor;
    private readonly Func<SecretReference, CancellationToken, ValueTask<string?>>? _secretResolver;

    public WorkspaceContext(
        string jobId,
        int attempt,
        WorkspacePaths paths,
        ILogSink logSink,
        SecretRedactor redactor,
        Func<SecretReference, CancellationToken, ValueTask<string?>>? secretResolver)
    {
        JobId = jobId;
        Attempt = attempt;
        WorkDir = paths.WorkDir;
        ArtifactsDir = paths.ArtifactsDir;
        LogsDir = paths.LogsDir;
        LogSink = logSink;
        _redactor = redactor;
        _secretResolver = secretResolver;
    }

    public string JobId { get; }
    public int Attempt { get; }
    public string WorkDir { get; }
    public string ArtifactsDir { get; }
    public string LogsDir { get; }
    public ILogSink LogSink { get; }

    public async ValueTask<string?> ResolveSecretAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        if (_secretResolver is null)
        {
            return null;
        }

        var secret = await _secretResolver(reference, cancellationToken);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            _redactor.RegisterSecret(secret);
        }

        return secret;
    }

    public void SetOutput(string key, string value) => _outputs[key] = value;

    public string? GetOutput(string key) => _outputs.TryGetValue(key, out var value) ? value : null;
}

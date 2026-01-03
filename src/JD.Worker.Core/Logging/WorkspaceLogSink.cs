using System.Threading;
using JD.Worker.Abstractions;
using Microsoft.Extensions.Logging;
using WorkerLogLevel = JD.Worker.Abstractions.LogLevel;

namespace JD.Worker.Core;

public sealed class WorkspaceLogSink : ILogSink
{
    private readonly ILogger _logger;
    private readonly SecretRedactor _redactor;
    private readonly string _prefix;

    public WorkspaceLogSink(ILogger logger, SecretRedactor redactor, string jobId, int attempt)
    {
        _logger = logger;
        _redactor = redactor;
        _prefix = $"[{jobId}:{attempt}]";
    }

    public ValueTask WriteAsync(WorkerLogLevel level, string message, CancellationToken cancellationToken)
    {
        var redacted = _redactor.Redact(message);
        _logger.Log(Map(level), "{Prefix} {Message}", _prefix, redacted);
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteStdOutAsync(string content, CancellationToken cancellationToken) =>
        WriteAsync(WorkerLogLevel.Information, content, cancellationToken);

    public ValueTask WriteStdErrAsync(string content, CancellationToken cancellationToken) =>
        WriteAsync(WorkerLogLevel.Error, content, cancellationToken);

    private static Microsoft.Extensions.Logging.LogLevel Map(WorkerLogLevel level) => level switch
    {
        WorkerLogLevel.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
        WorkerLogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
        WorkerLogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
        WorkerLogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
        WorkerLogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
        WorkerLogLevel.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
        _ => Microsoft.Extensions.Logging.LogLevel.Information
    };
}

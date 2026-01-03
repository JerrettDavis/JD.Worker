using System;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class JobLogSink : ILogSink
{
    private readonly ILogSink _inner;
    private readonly SecretRedactor _redactor;
    private readonly string _jobId;
    private readonly int _attempt;
    private readonly string? _workerId;
    private readonly IJobLogPublisher? _publisher;

    public JobLogSink(
        ILogSink inner,
        SecretRedactor redactor,
        string jobId,
        int attempt,
        string? workerId,
        IJobLogPublisher? publisher)
    {
        _inner = inner;
        _redactor = redactor;
        _jobId = jobId;
        _attempt = attempt;
        _workerId = workerId;
        _publisher = publisher;
    }

    public async ValueTask WriteAsync(LogLevel level, string message, CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(level, message, cancellationToken);

        if (_publisher is null)
        {
            return;
        }

        try
        {
            var redacted = _redactor.Redact(message);
            var entry = new JobLogEntry(_jobId, _attempt, DateTimeOffset.UtcNow, level, redacted, _workerId);
            await _publisher.PublishLogAsync(entry, cancellationToken);
        }
        catch (Exception)
        {
            // Ignore log publishing failures to avoid impacting job execution.
        }
    }

    public ValueTask WriteStdOutAsync(string content, CancellationToken cancellationToken) =>
        WriteAsync(LogLevel.Information, content, cancellationToken);

    public ValueTask WriteStdErrAsync(string content, CancellationToken cancellationToken) =>
        WriteAsync(LogLevel.Error, content, cancellationToken);
}

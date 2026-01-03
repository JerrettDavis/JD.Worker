using System.Threading;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class NullLogSink : ILogSink
{
    public ValueTask WriteAsync(LogLevel level, string message, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask WriteStdOutAsync(string content, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask WriteStdErrAsync(string content, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace JD.Worker.Core;

public sealed class InMemoryJobScheduler : IJobScheduler
{
    private readonly ConcurrentQueue<string> _queue = new();

    public Task EnqueueAsync(string jobId, CancellationToken cancellationToken)
    {
        _queue.Enqueue(jobId);
        return Task.CompletedTask;
    }

    public Task<string?> DequeueAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_queue.TryDequeue(out var jobId) ? jobId : null);
    }
}

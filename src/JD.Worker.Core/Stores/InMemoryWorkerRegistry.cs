using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JD.Worker.Core;

public sealed class InMemoryWorkerRegistry : IWorkerRegistry
{
    private readonly ConcurrentDictionary<string, WorkerRegistration> _workers = new();
    private readonly TimeSpan _offlineThreshold = TimeSpan.FromSeconds(20);

    public Task<IReadOnlyList<WorkerRegistration>> ListAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var workers = _workers.Values
            .Select(worker => IsOffline(worker, now)
                ? worker with { Status = WorkerStatus.Unknown, CurrentJobId = null }
                : worker)
            .OrderByDescending(worker => worker.LastHeartbeatUtc)
            .ToList();
        return Task.FromResult((IReadOnlyList<WorkerRegistration>)workers);
    }

    public Task RegisterAsync(WorkerRegistration registration, CancellationToken cancellationToken)
    {
        _workers[registration.WorkerId] = registration;
        return Task.CompletedTask;
    }

    private bool IsOffline(WorkerRegistration worker, DateTimeOffset now) =>
        now - worker.LastHeartbeatUtc > _offlineThreshold;
}

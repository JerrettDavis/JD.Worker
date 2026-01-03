using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class JobLeaseService
{
    private readonly IJobStore _jobStore;
    private readonly JobStateService _stateService;
    private readonly ConcurrentDictionary<string, LeaseHandle> _leases = new();
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public JobLeaseService(IJobStore jobStore, JobStateService stateService)
    {
        _jobStore = jobStore;
        _stateService = stateService;
    }

    public async Task<LeaseHandle?> TryAcquireAsync(string jobId, TimeSpan duration, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await CleanupExpiredAsync(cancellationToken);

            if (_leases.TryGetValue(jobId, out var existing) && existing.IsValid)
            {
                return null;
            }

            var job = await _jobStore.GetAsync(jobId, cancellationToken);
            if (job is null)
            {
                return null;
            }

            var leased = await _stateService.TransitionAsync(job, JobState.Leased, "Lease acquired.", cancellationToken);
            if (leased is null)
            {
                return null;
            }

            var lease = new LeaseHandle(jobId, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(duration));
            _leases[jobId] = lease;
            return lease;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<LeaseHandle?> TryAcquireNextAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await CleanupExpiredAsync(cancellationToken);

            var jobs = await _jobStore.ListAsync(JobState.Accepted, cancellationToken);
            foreach (var job in jobs)
            {
                if (_leases.TryGetValue(job.JobId, out var existing) && existing.IsValid)
                {
                    continue;
                }

                var leased = await _stateService.TransitionAsync(job, JobState.Leased, "Lease acquired.", cancellationToken);
                if (leased is null)
                {
                    continue;
                }

                var lease = new LeaseHandle(job.JobId, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(duration));
                _leases[job.JobId] = lease;
                return lease;
            }

            return null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<LeaseHandle?> RenewAsync(LeaseHandle lease, TimeSpan extension, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (!_leases.TryGetValue(lease.JobId, out var existing) || existing.LeaseId != lease.LeaseId)
            {
                return null;
            }

            var updated = existing with { ExpiresUtc = DateTimeOffset.UtcNow.Add(extension) };
            _leases[lease.JobId] = updated;
            return updated;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> ReleaseAsync(LeaseHandle lease, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (!_leases.TryGetValue(lease.JobId, out var existing) || existing.LeaseId != lease.LeaseId)
            {
                return false;
            }

            _leases.TryRemove(lease.JobId, out _);
            var job = await _jobStore.GetAsync(lease.JobId, cancellationToken);
            if (job is null)
            {
                return false;
            }

            return await _stateService.TransitionAsync(job, JobState.Accepted, "Lease released.", cancellationToken) is not null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task ClearAsync(string jobId, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            _leases.TryRemove(jobId, out _);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        foreach (var lease in _leases.Values)
        {
            if (!lease.IsValid)
            {
                _leases.TryRemove(lease.JobId, out _);

                var job = await _jobStore.GetAsync(lease.JobId, cancellationToken);
                if (job is not null && job.State == JobState.Leased)
                {
                    await _stateService.TransitionAsync(job, JobState.Accepted, "Lease expired.", cancellationToken);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JD.Worker.Core;

public sealed class JobSchedulerService : BackgroundService
{
    private readonly IJobScheduler _scheduler;
    private readonly IJobStore _store;
    private readonly IJobExecutor _executor;
    private readonly WorkerConfig _config;
    private readonly ILogger<JobSchedulerService> _logger;
    private readonly Dictionary<string, SemaphoreSlim> _poolLimiters;
    private readonly string _defaultPool;
    private readonly TimeSpan _idleDelay = TimeSpan.FromMilliseconds(250);

    public JobSchedulerService(
        IJobScheduler scheduler,
        IJobStore store,
        IJobExecutor executor,
        WorkerConfig config,
        ILogger<JobSchedulerService> logger)
    {
        _scheduler = scheduler;
        _store = store;
        _executor = executor;
        _config = config;
        _logger = logger;

        _poolLimiters = BuildPoolLimiters(config);
        _defaultPool = config.Worker.Pools.FirstOrDefault()?.Name ?? "default";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var jobId = await _scheduler.DequeueAsync(stoppingToken);
            if (jobId is null)
            {
                await Task.Delay(_idleDelay, stoppingToken);
                continue;
            }

            var job = await _store.GetAsync(jobId, stoppingToken);
            if (job is null)
            {
                _logger.LogWarning("Dequeued job {JobId} but it was missing from the store.", jobId);
                continue;
            }

            var pool = ResolvePool(job);
            var limiter = _poolLimiters.TryGetValue(pool, out var poolLimiter) ? poolLimiter : _poolLimiters[_defaultPool];
            await limiter.WaitAsync(stoppingToken);

            _ = RunJobAsync(jobId, limiter, stoppingToken);
        }
    }

    private async Task RunJobAsync(string jobId, SemaphoreSlim limiter, CancellationToken stoppingToken)
    {
        try
        {
            await _executor.ExecuteAsync(jobId, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed with an unexpected error.", jobId);
        }
        finally
        {
            limiter.Release();
        }
    }

    private string ResolvePool(JobRecord job)
    {
        var requested = job.Envelope.Payload.RequestedPool;
        if (!string.IsNullOrWhiteSpace(requested) &&
            _poolLimiters.ContainsKey(requested))
        {
            return requested;
        }

        return _defaultPool;
    }

    private static Dictionary<string, SemaphoreSlim> BuildPoolLimiters(WorkerConfig config)
    {
        var pools = config.Worker.Pools;
        if (pools.Count == 0)
        {
            return new Dictionary<string, SemaphoreSlim>
            {
                ["default"] = new SemaphoreSlim(1, 1)
            };
        }

        var map = new Dictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        foreach (var pool in pools)
        {
            var concurrency = pool.Concurrency > 0 ? pool.Concurrency : 1;
            map[pool.Name] = new SemaphoreSlim(concurrency, concurrency);
        }

        return map;
    }
}

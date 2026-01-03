using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new();
    private readonly ConcurrentQueue<JobEvent> _events = new();

    public Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    public Task<IReadOnlyList<JobRecord>> ListAsync(JobState? state, CancellationToken cancellationToken)
    {
        IEnumerable<JobRecord> jobs = _jobs.Values;
        if (state is not null)
        {
            jobs = jobs.Where(job => job.State == state);
        }

        return Task.FromResult((IReadOnlyList<JobRecord>)jobs.OrderByDescending(j => j.UpdatedUtc).ToList());
    }

    public Task SaveAsync(JobRecord job, CancellationToken cancellationToken)
    {
        _jobs[job.JobId] = job;
        return Task.CompletedTask;
    }

    public Task AppendEventAsync(JobEvent @event, CancellationToken cancellationToken)
    {
        _events.Enqueue(@event);
        return Task.CompletedTask;
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class InMemoryLogStore : ILogStore
{
    private readonly ConcurrentQueue<JobLogEntry> _entries = new();
    private readonly int _maxEntries;

    public InMemoryLogStore(int maxEntries = 5000)
    {
        _maxEntries = maxEntries;
    }

    public Task AppendAsync(JobLogEntry entry, CancellationToken cancellationToken)
    {
        _entries.Enqueue(entry);
        Trim();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JobLogEntry>> QueryAsync(LogQuery query, CancellationToken cancellationToken)
    {
        var snapshot = _entries.ToArray();
        IEnumerable<JobLogEntry> filtered = snapshot;

        if (!string.IsNullOrWhiteSpace(query.JobId))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.JobId, query.JobId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.WorkerId))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.WorkerId, query.WorkerId, StringComparison.OrdinalIgnoreCase));
        }

        if (query.SinceUtc.HasValue)
        {
            filtered = filtered.Where(entry => entry.TimestampUtc >= query.SinceUtc.Value);
        }

        var ordered = filtered.OrderBy(entry => entry.TimestampUtc).ToList();
        if (query.Take.HasValue && query.Take.Value > 0)
        {
            ordered = ordered.TakeLast(query.Take.Value).ToList();
        }

        return Task.FromResult((IReadOnlyList<JobLogEntry>)ordered);
    }

    private void Trim()
    {
        while (_entries.Count > _maxEntries && _entries.TryDequeue(out _))
        {
        }
    }
}

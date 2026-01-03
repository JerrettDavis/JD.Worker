using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed record LogQuery(
    string? JobId,
    string? WorkerId,
    DateTimeOffset? SinceUtc,
    int? Take);

public interface ILogStore
{
    Task AppendAsync(JobLogEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobLogEntry>> QueryAsync(LogQuery query, CancellationToken cancellationToken);
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public interface IJobStore
{
    Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobRecord>> ListAsync(JobState? state, CancellationToken cancellationToken);
    Task SaveAsync(JobRecord job, CancellationToken cancellationToken);
    Task AppendEventAsync(JobEvent @event, CancellationToken cancellationToken);
}

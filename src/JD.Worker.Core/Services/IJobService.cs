using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;

namespace JD.Worker.Core;

public interface IJobService
{
    Task<JobRecord> SubmitAsync(JobEnvelope envelope, string? source, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobRecord>> ListAsync(JobState? state, CancellationToken cancellationToken);
}

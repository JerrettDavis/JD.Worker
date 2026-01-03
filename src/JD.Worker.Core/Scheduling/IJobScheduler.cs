using System.Threading;
using System.Threading.Tasks;

namespace JD.Worker.Core;

public interface IJobScheduler
{
    Task EnqueueAsync(string jobId, CancellationToken cancellationToken);
    Task<string?> DequeueAsync(CancellationToken cancellationToken);
}

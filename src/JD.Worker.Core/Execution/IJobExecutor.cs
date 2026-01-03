using System.Threading;
using System.Threading.Tasks;

namespace JD.Worker.Core;

public interface IJobExecutor
{
    Task ExecuteAsync(string jobId, CancellationToken cancellationToken);
}

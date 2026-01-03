using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JD.Worker.Core;

public interface IWorkerRegistry
{
    Task<IReadOnlyList<WorkerRegistration>> ListAsync(CancellationToken cancellationToken);
    Task RegisterAsync(WorkerRegistration registration, CancellationToken cancellationToken);
}

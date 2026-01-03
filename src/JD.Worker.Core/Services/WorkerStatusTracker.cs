using System.Threading;

namespace JD.Worker.Core;

public sealed class WorkerStatusTracker
{
    private int _status = (int)WorkerStatus.Idle;
    private string? _currentJobId;

    public WorkerStatus CurrentStatus => (WorkerStatus)Volatile.Read(ref _status);
    public string? CurrentJobId => Volatile.Read(ref _currentJobId);

    public void SetStatus(WorkerStatus status) => Volatile.Write(ref _status, (int)status);

    public void SetCurrentJobId(string? jobId) => Volatile.Write(ref _currentJobId, jobId);
}

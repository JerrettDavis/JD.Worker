namespace JD.Worker.Orchestrator.ApiService;

public sealed record LeaseRequest(string WorkerId, int LeaseSeconds);

public sealed record LeaseRenewRequest(string LeaseId, int ExtensionSeconds);

public sealed record LeaseReleaseRequest(string LeaseId);

public sealed record NegativeAckRequest(bool Requeue);
public sealed record JobStartRequest(string? WorkerId);

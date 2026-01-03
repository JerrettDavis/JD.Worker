namespace JD.Worker.Connectors.Http;

internal sealed record LeaseRequest(string WorkerId, int LeaseSeconds);

internal sealed record LeaseRenewRequest(string LeaseId, int ExtensionSeconds);

internal sealed record LeaseReleaseRequest(string LeaseId);

internal sealed record NegativeAckRequest(bool Requeue);
internal sealed record JobStartRequest(string? WorkerId);

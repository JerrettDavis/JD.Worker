using System.Text.Json.Serialization;
using JD.Worker.Abstractions;

namespace JD.Worker.Connectors.Http;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LeaseHandle))]
[JsonSerializable(typeof(LeaseRequest))]
[JsonSerializable(typeof(LeaseRenewRequest))]
[JsonSerializable(typeof(LeaseReleaseRequest))]
[JsonSerializable(typeof(NegativeAckRequest))]
[JsonSerializable(typeof(JobStartRequest))]
[JsonSerializable(typeof(JobLogEntry))]
internal sealed partial class HttpConnectorSerializerContext : JsonSerializerContext
{
}

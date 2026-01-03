using System.Text.Json.Serialization;

namespace JD.Worker.Core;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkerRegistration))]
internal sealed partial class WorkerRuntimeJsonContext : JsonSerializerContext
{
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JD.Worker.Configuration;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkerConfig))]
[JsonSerializable(typeof(JobEnvelope))]
[JsonSerializable(typeof(List<JobEnvelope>))]
public sealed partial class ConfigSerializerContext : JsonSerializerContext
{
}

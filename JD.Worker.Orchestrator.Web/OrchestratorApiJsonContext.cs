using System.Collections.Generic;
using System.Text.Json.Serialization;
using JD.Worker.Abstractions;
using JD.Worker.Configuration;
using JD.Worker.Core;

namespace JD.Worker.Orchestrator.Web;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JobRecord))]
[JsonSerializable(typeof(List<JobRecord>))]
[JsonSerializable(typeof(WorkerRegistration))]
[JsonSerializable(typeof(List<WorkerRegistration>))]
[JsonSerializable(typeof(JobEnvelope))]
[JsonSerializable(typeof(JobLogEntry))]
[JsonSerializable(typeof(List<JobLogEntry>))]
public sealed partial class OrchestratorApiJsonContext : JsonSerializerContext
{
}

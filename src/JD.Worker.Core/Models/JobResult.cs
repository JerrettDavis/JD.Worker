using System;
using System.Collections.Generic;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed record JobResult(
    string JobId,
    int Attempt,
    JobState State,
    DateTimeOffset CompletedUtc,
    TimeSpan Duration,
    string? ErrorMessage,
    IReadOnlyList<IStepResult> StepResults,
    IReadOnlyList<IArtifactReference>? Artifacts) : IJobResult;

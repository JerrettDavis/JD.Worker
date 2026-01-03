using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed record StepResult(
    string StepName,
    bool Success,
    int? ExitCode,
    string? ErrorMessage,
    TimeSpan Duration,
    IReadOnlyDictionary<string, string>? Outputs) : IStepResult;

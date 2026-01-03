using System;
using System.Collections.Generic;
using System.Linq;
using JD.Worker.Abstractions;

namespace JD.Worker.Configuration;

public interface IConfigValidator
{
    ValidationResult Validate(WorkerConfig config);
}

public sealed class ConfigValidationPipeline : IConfigValidator
{
    public ValidationResult Validate(WorkerConfig config)
    {
        if (config is null)
        {
            return ValidationResult.Failure([
                new ValidationError("Configuration is required.", ValidationSeverity.Error)
            ]);
        }

        var errors = new List<ValidationError>();

        if (config.Worker is null)
        {
            errors.Add(new ValidationError("Worker settings are required.", ValidationSeverity.Error));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(config.Worker.Id))
            {
                errors.Add(new ValidationError("Worker id is required.", ValidationSeverity.Error));
            }

            if (config.Worker.Pools is null || config.Worker.Pools.Count == 0)
            {
                errors.Add(new ValidationError("At least one pool must be configured.", ValidationSeverity.Error));
            }
            else
            {
                var duplicates = config.Worker.Pools
                    .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                    .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                foreach (var duplicate in duplicates)
                {
                    errors.Add(new ValidationError($"Duplicate pool name '{duplicate}'.", ValidationSeverity.Error));
                }

                foreach (var pool in config.Worker.Pools)
                {
                    if (string.IsNullOrWhiteSpace(pool.Name))
                    {
                        errors.Add(new ValidationError("Pool name is required.", ValidationSeverity.Error));
                    }

                    if (pool.Concurrency <= 0)
                    {
                        errors.Add(new ValidationError($"Pool '{pool.Name}' must have concurrency greater than zero.", ValidationSeverity.Error));
                    }
                }
            }
        }

        if (config.Cnc is null || config.Cnc.Count == 0)
        {
            errors.Add(new ValidationError("At least one CnC connector must be configured.", ValidationSeverity.Error));
        }

        if (config.Policy?.AllowedStepTypes is { Count: > 0 })
        {
            foreach (var stepType in config.Policy.AllowedStepTypes)
            {
                if (!ParsingHelpers.TryParseNormalizedEnum<StepType>(stepType, out _))
                {
                    errors.Add(new ValidationError($"Invalid step type '{stepType}'.", ValidationSeverity.Error));
                }
            }
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}

public sealed record ValidationResult(bool IsValid, IReadOnlyList<ValidationError> Errors)
{
    public static ValidationResult Success() => new(true, Array.Empty<ValidationError>());

    public static ValidationResult Failure(IReadOnlyList<ValidationError> errors) =>
        new(false, errors);
}

public sealed record ValidationError(string Message, ValidationSeverity Severity);

public enum ValidationSeverity
{
    Error,
    Warning,
    Info
}

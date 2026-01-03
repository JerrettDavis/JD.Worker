using System;
using System.Collections.Generic;
using JD.Worker.Abstractions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JD.Worker.Configuration;

public sealed class YamlConfigParser : IConfigParser
{
    private readonly IDeserializer _deserializer = CreateDeserializer();

    public ParseResult<T> Parse<T>(string content) where T : class
    {
        var errors = new List<ParseError>();
        var expanded = ParsingHelpers.InterpolateEnvironmentVariables(content, errors);

        try
        {
            var value = _deserializer.Deserialize<T>(expanded);
            if (value is null)
            {
                errors.Add(new ParseError("Parsed value was null.", null, null));
            }

            return errors.Count == 0
                ? ParseResult<T>.Success(value!)
                : ParseResult<T>.Failure(errors);
        }
        catch (YamlException ex)
        {
            var line = checked((int)ex.Start.Line) + 1;
            var column = checked((int)ex.Start.Column) + 1;
            errors.Add(new ParseError(ex.Message, line, column));
            return ParseResult<T>.Failure(errors);
        }
    }

    public ParseResult<JobEnvelope> ParseJobEnvelope(string content) =>
        Parse<JobEnvelope>(content);

    private static IDeserializer CreateDeserializer()
    {
        return new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new TimeSpanYamlTypeConverter())
            .WithTypeConverter(new NormalizedEnumYamlTypeConverter<StepType>())
            .WithTypeConverter(new NormalizedEnumYamlTypeConverter<BackoffStrategy>())
            .WithTypeConverter(new NormalizedEnumYamlTypeConverter<CleanupPolicy>())
            .WithTypeConverter(new NormalizedEnumYamlTypeConverter<SandboxMode>())
            .IgnoreUnmatchedProperties()
            .Build();
    }
}

internal sealed class TimeSpanYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(TimeSpan) || type == typeof(TimeSpan?);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer _)
    {
        var scalar = parser.Consume<Scalar>();
        if (string.IsNullOrWhiteSpace(scalar.Value))
        {
            return type == typeof(TimeSpan?) ? null : default(TimeSpan);
        }

        if (!ParsingHelpers.TryParseDuration(scalar.Value!, out var value))
        {
            throw new YamlException(scalar.Start, scalar.End, $"Invalid duration value '{scalar.Value}'.");
        }

        return value;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer _)
    {
        if (value is null)
        {
            emitter.Emit(new Scalar("null"));
            return;
        }

        emitter.Emit(new Scalar(((TimeSpan)value).ToString()));
    }
}

internal sealed class NormalizedEnumYamlTypeConverter<TEnum> : IYamlTypeConverter
    where TEnum : struct, Enum
{
    public bool Accepts(Type type) => type == typeof(TEnum) || type == typeof(TEnum?);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer _)
    {
        var scalar = parser.Consume<Scalar>();
        if (string.IsNullOrWhiteSpace(scalar.Value))
        {
            return type == typeof(TEnum?) ? null : default(TEnum);
        }

        if (ParsingHelpers.TryParseNormalizedEnum(scalar.Value!, out TEnum parsed))
        {
            return parsed;
        }

        throw new YamlException(scalar.Start, scalar.End, $"Invalid {typeof(TEnum).Name} value '{scalar.Value}'.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer _)
    {
        if (value is null)
        {
            emitter.Emit(new Scalar("null"));
            return;
        }

        emitter.Emit(new Scalar(value.ToString() ?? string.Empty));
    }
}

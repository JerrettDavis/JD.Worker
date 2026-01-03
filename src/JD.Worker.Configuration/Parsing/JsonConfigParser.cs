using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using JD.Worker.Abstractions;

namespace JD.Worker.Configuration;

public sealed class JsonConfigParser : IConfigParser
{
    private readonly JsonSerializerOptions _options = CreateOptions();

    public ParseResult<T> Parse<T>(string content) where T : class
    {
        var errors = new List<ParseError>();
        var expanded = ParsingHelpers.InterpolateEnvironmentVariables(content, errors);

        try
        {
            var value = JsonSerializer.Deserialize<T>(expanded, _options);
            if (value is null)
            {
                errors.Add(new ParseError("Parsed value was null.", null, null));
            }

            return errors.Count == 0
                ? ParseResult<T>.Success(value!)
                : ParseResult<T>.Failure(errors);
        }
        catch (JsonException ex)
        {
            errors.Add(new ParseError(ex.Message, ToLine(ex.LineNumber), ToColumn(ex.BytePositionInLine)));
            return ParseResult<T>.Failure(errors);
        }
    }

    public ParseResult<JobEnvelope> ParseJobEnvelope(string content) =>
        Parse<JobEnvelope>(content);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        options.Converters.Add(new TimeSpanJsonConverterFactory());
        options.Converters.Add(new NormalizedEnumJsonConverter<StepType>());
        options.Converters.Add(new NormalizedEnumJsonConverter<BackoffStrategy>());
        options.Converters.Add(new NormalizedEnumJsonConverter<CleanupPolicy>());
        options.Converters.Add(new NormalizedEnumJsonConverter<SandboxMode>());

        return options;
    }

    private static int? ToLine(long? lineNumber) =>
        lineNumber.HasValue ? (int)lineNumber.Value + 1 : null;

    private static int? ToColumn(long? columnNumber) =>
        columnNumber.HasValue ? (int)columnNumber.Value + 1 : null;
}

internal sealed class TimeSpanJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(TimeSpan) || typeToConvert == typeof(TimeSpan?);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return typeToConvert == typeof(TimeSpan)
            ? new TimeSpanJsonConverter()
            : new NullableTimeSpanJsonConverter();
    }
}

internal sealed class TimeSpanJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Duration values must be strings.");
        }

        var text = reader.GetString();
        if (text is null || !ParsingHelpers.TryParseDuration(text, out var value))
        {
            throw new JsonException($"Invalid duration value '{text}'.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

internal sealed class NullableTimeSpanJsonConverter : JsonConverter<TimeSpan?>
{
    public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var converter = new TimeSpanJsonConverter();
        return converter.Read(ref reader, typeof(TimeSpan), options);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString());
    }
}

internal sealed class NormalizedEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (text is not null && ParsingHelpers.TryParseNormalizedEnum(text, out TEnum parsed))
            {
                return parsed;
            }

            throw new JsonException($"Invalid {typeof(TEnum).Name} value '{text}'.");
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            if (Enum.IsDefined(typeof(TEnum), number))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            }
        }

        throw new JsonException($"Invalid {typeof(TEnum).Name} value.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

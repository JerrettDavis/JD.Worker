using System;
using System.Collections.Generic;

namespace JD.Worker.Configuration;

public interface IConfigParser
{
    ParseResult<T> Parse<T>(string content) where T : class;
    ParseResult<JobEnvelope> ParseJobEnvelope(string content);
}

public sealed record ParseResult<T>(bool IsSuccess, T? Value, IReadOnlyList<ParseError> Errors)
{
    public static ParseResult<T> Success(T value) =>
        new(true, value, Array.Empty<ParseError>());

    public static ParseResult<T> Failure(IReadOnlyList<ParseError> errors) =>
        new(false, default, errors);
}

public sealed record ParseError(string Message, int? Line, int? Column);

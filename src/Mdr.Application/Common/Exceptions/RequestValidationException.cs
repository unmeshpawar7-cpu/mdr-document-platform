namespace Mdr.Application.Common.Exceptions;

/// <summary>One field-level problem with a request.</summary>
public sealed record FieldError(string Field, string Message, string? Code = null);

/// <summary>Thrown when a request is syntactically valid but its content is rejected. Mapped to HTTP 400.</summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(string message, IReadOnlyList<FieldError> errors)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyList<FieldError> Errors { get; }
}

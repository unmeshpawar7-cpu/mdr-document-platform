namespace Mdr.Application.Common.Exceptions;

/// <summary>Thrown when a request conflicts with existing state (duplicates, concurrency). Mapped to HTTP 409.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

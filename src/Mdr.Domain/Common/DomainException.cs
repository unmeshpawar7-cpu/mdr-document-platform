namespace Mdr.Domain.Common;

/// <summary>
/// Raised when a domain invariant is violated. The <see cref="Code"/> is a stable,
/// machine-readable identifier that the API surfaces to clients.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

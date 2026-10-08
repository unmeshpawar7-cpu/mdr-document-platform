namespace Mdr.Application.Common.Exceptions;

/// <summary>Thrown when credentials are invalid. Mapped to HTTP 401 with a generic message.</summary>
public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException()
        : base("Invalid username or password.")
    {
    }
}

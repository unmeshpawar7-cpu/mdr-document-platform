using Mdr.Application.Common.Abstractions;

namespace Mdr.Api.Infrastructure;

/// <summary>Resolves the caller from the JWT principal; outside a request it reports <c>system</c>.</summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public string UserName
    {
        get
        {
            var identity = _accessor.HttpContext?.User.Identity;
            return identity is not null && identity.IsAuthenticated && !string.IsNullOrEmpty(identity.Name)
                ? identity.Name
                : "system";
        }
    }

    public bool IsAuthenticated => _accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public string? CorrelationId => _accessor.HttpContext?.TraceIdentifier;
}

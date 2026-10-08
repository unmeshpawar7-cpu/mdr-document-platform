namespace Mdr.Application.Common.Abstractions;

/// <summary>The caller of the current operation. Background jobs run as <c>system</c>.</summary>
public interface ICurrentUser
{
    string UserName { get; }

    bool IsAuthenticated { get; }

    string? CorrelationId { get; }
}

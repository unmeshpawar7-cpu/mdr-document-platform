namespace Mdr.Api.Contracts;

/// <summary>Success envelope: <c>{ "success": true, "data": {...}, "message": null }</c>.</summary>
public sealed record ApiResponse<T>(bool Success, T? Data, string? Message);

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null) => new(true, data, message);
}

/// <summary>One field-level error inside an error envelope.</summary>
public sealed record ApiErrorItem(string Field, string Message, string? Code = null);

/// <summary>Error envelope: <c>{ "success": false, "message": "...", "errors": [...], "traceId": "..." }</c>.</summary>
public sealed record ApiErrorResponse(bool Success, string Message, IReadOnlyList<ApiErrorItem> Errors, string? TraceId)
{
    public static ApiErrorResponse Create(string message, string? traceId, IReadOnlyList<ApiErrorItem>? errors = null) =>
        new(false, message, errors ?? Array.Empty<ApiErrorItem>(), traceId);
}

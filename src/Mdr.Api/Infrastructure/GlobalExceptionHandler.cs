using FluentValidation;
using Mdr.Api.Contracts;
using Mdr.Application.Common.Exceptions;
using Mdr.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Mdr.Api.Infrastructure;

/// <summary>Maps exceptions to the error envelope with the correct status code. Unexpected errors never leak details.</summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        int status;
        string message;
        IReadOnlyList<ApiErrorItem> errors = Array.Empty<ApiErrorItem>();

        switch (exception)
        {
            case RequestValidationException e:
                status = StatusCodes.Status400BadRequest;
                message = e.Message;
                errors = e.Errors.Select(x => new ApiErrorItem(x.Field, x.Message, x.Code)).ToList();
                break;
            case ValidationException e:
                status = StatusCodes.Status400BadRequest;
                message = "Validation failed.";
                errors = e.Errors.Select(x => new ApiErrorItem(ToCamelCase(x.PropertyName), x.ErrorMessage, x.ErrorCode)).ToList();
                break;
            case AuthenticationFailedException e:
                status = StatusCodes.Status401Unauthorized;
                message = e.Message;
                break;
            case NotFoundException e:
                status = StatusCodes.Status404NotFound;
                message = e.Message;
                break;
            case ConflictException e:
                status = StatusCodes.Status409Conflict;
                message = e.Message;
                errors = new[] { new ApiErrorItem(string.Empty, e.Message, e.Code) };
                break;
            case DbUpdateConcurrencyException:
                status = StatusCodes.Status409Conflict;
                message = "The resource was modified by another request. Reload it and retry.";
                break;
            case DomainException e:
                status = StatusCodes.Status422UnprocessableEntity;
                message = e.Message;
                errors = new[] { new ApiErrorItem(string.Empty, e.Message, e.Code) };
                break;
            case BadHttpRequestException e:
                status = e.StatusCode;
                message = e.StatusCode == StatusCodes.Status413PayloadTooLarge ? "The request body is too large." : "The request could not be read.";
                break;
            case InvalidDataException:
                status = StatusCodes.Status400BadRequest;
                message = "The multipart request could not be read (it may exceed the size limit).";
                break;
            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                status = 499;
                message = "The client closed the request.";
                break;
            default:
                status = StatusCodes.Status500InternalServerError;
                message = "An unexpected error occurred. Quote the traceId when reporting it.";
                break;
        }

        if (status >= 500)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogInformation("Request failed with {Status}: {Message}", status, message);
        }

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            ApiErrorResponse.Create(message, httpContext.TraceIdentifier, errors),
            cancellationToken);
        return true;
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}

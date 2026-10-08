using Mdr.Api.Contracts;
using Mdr.Api.Infrastructure;
using Mdr.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Mdr.Api.Controllers;

/// <summary>Issues JWT access tokens.</summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService) => _authService = authService;

    /// <summary>Exchanges a username and password for a bearer token.</summary>
    /// <remarks>
    /// Demo users exist only in the Development environment, e.g.
    /// <c>{ "userName": "controller", "password": "Controller!Demo2026" }</c>.
    /// </remarks>
    /// <response code="200">Token issued.</response>
    /// <response code="400">Missing username or password.</response>
    /// <response code="401">Invalid credentials.</response>
    /// <response code="429">Too many attempts.</response>
    [HttpPost("token")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> IssueToken([FromBody] TokenRequest request, CancellationToken cancellationToken)
    {
        var token = await _authService.IssueTokenAsync(request, cancellationToken);
        return Ok(ApiResponse.Ok(token));
    }
}

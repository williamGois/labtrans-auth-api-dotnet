using System.Security.Claims;
using AuthApi.Dtos;
using AuthApi.Exceptions;
using AuthApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var user = await authService.RegisterAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Me), new { id = user.Id }, user);
        }
        catch (DuplicateEmailException exception)
        {
            return Conflict(new ErrorResponse(exception.Message));
        }
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.LoginAsync(request, cancellationToken));
        }
        catch (InvalidCredentialsException exception)
        {
            return Unauthorized(new ErrorResponse(exception.Message));
        }
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(rawUserId, out var userId))
        {
            return Unauthorized(new ErrorResponse("Token sem identificador de usuario valido."));
        }

        var user = await authService.GetUserAsync(userId, cancellationToken);
        return user is null
            ? Unauthorized(new ErrorResponse("Usuario do token nao foi encontrado."))
            : Ok(user);
    }
}

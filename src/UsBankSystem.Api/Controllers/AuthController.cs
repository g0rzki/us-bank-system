using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UsBankSystem.Api.Models.Auth;
using UsBankSystem.Api.Services;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Route("auth")]
[Tags("Auth")]
[AllowAnonymous]
public class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await authService.RegisterAsync(request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var token = await authService.LoginAsync(request);
        return Ok(token);
    }
}

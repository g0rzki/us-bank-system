using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Route("auth")]
[Tags("Auth")]
public class MeController : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        return Ok(new { id, email });
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Route("accounts")]
[Tags("Accounts")]
public class AccountsController(AccountService accountService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var (success, error, result) = await accountService.CreateAsync(userId, request);
        if (!success)
            return BadRequest(new { message = error });

        return StatusCode(StatusCodes.Status201Created, result);
    }
}

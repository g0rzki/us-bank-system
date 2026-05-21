using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Route("accounts")]
[Tags("Accounts")]
public class AccountsController(AccountService accountService, TransactionService transactionService, JuniorService juniorService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<AccountResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll()
    {
        var userId = UserId();
        return Ok(await accountService.GetAllAsync(userId));
    }

    [HttpPost]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest request)
    {
        var userId = UserId();
        var result = await accountService.CreateAsync(userId, request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var userId = UserId();
        return Ok(await accountService.GetByIdAsync(userId, id));
    }

    [HttpGet("{id:guid}/balance")]
    [ProducesResponseType(typeof(BalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBalance(Guid id)
    {
        var userId = UserId();
        return Ok(await accountService.GetBalanceAsync(userId, id));
    }

    [HttpGet("{id:guid}/transactions")]
    [ProducesResponseType(typeof(PagedResponse<TransactionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactions(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = UserId();
        return Ok(await transactionService.GetTransactionsAsync(userId, id, page, pageSize));
    }

    [HttpGet("{id:guid}/junior-accounts")]
    [ProducesResponseType(typeof(List<JuniorAccountResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJuniorAccounts(Guid id)
    {
        var userId = UserId();
        return Ok(await juniorService.GetJuniorAccountsAsync(userId, id));
    }

    [HttpPost("junior")]
    [ProducesResponseType(typeof(JuniorAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateJunior([FromBody] CreateJuniorAccountRequest request)
    {
        var userId = UserId();
        var result = await juniorService.CreateJuniorAsync(userId, request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("junior/{id:guid}/card")]
    [ProducesResponseType(typeof(CardResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddJuniorCard(Guid id, [FromBody] AddJuniorCardRequest request)
    {
        var userId = UserId();
        var result = await juniorService.AddCardAsync(userId, id, request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch("{id:guid}/junior-limit")]
    [ProducesResponseType(typeof(CardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateJuniorLimit(Guid id, [FromBody] UpdateJuniorLimitRequest request)
    {
        var userId = UserId();
        return Ok(await juniorService.UpdateLimitAsync(userId, id, request));
    }

    private Guid UserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
}

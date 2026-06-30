using FinancialStatements.BFF.Models;
using FinancialStatements.BFF.Orchestrators;
using FinancialStatements.Models.DTOs.Request;
using FinancialStatements.Models.DTOs.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialStatements.BFF.Controllers;

[ApiController]
[Route("api/statements")]
[Authorize]
public sealed class StatementController : ControllerBase
{
    private readonly IStatementOrchestrator _orchestrator;
    private readonly ILogger<StatementController> _logger;

    public StatementController(IStatementOrchestrator orchestrator, ILogger<StatementController> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [HttpPost("request")]
    [ProducesResponseType<SecureLinkResponseDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestStatement(
        [FromBody] StatementRequestDto dto,
        CancellationToken ct)
    {
        var userId = User.Identity?.Name
            ?? throw new InvalidOperationException("Authenticated user identity is missing.");

        var connectionId = Request.Headers["X-SignalR-ConnectionId"].ToString();

        var request = new StatementRequest(
            UserId: userId,
            AccountId: dto.AccountId,
            Type: dto.Type,
            From: dto.From,
            To: dto.To,
            ConnectionId: connectionId
        );

        _logger.LogInformation("Statement requested by User={UserId} Type={Type}", userId, dto.Type);

        var link = await _orchestrator.RequestStatementAsync(request, ct);

        return Accepted(link);
    }
}

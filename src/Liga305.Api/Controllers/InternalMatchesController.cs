using Liga305.Domain.Entities;
using Liga305.Infrastructure.BotWorker;
using Liga305.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Liga305.Api.Controllers;

/// <summary>
/// Endpoints called by the bot-worker microservice (bot → API), not the SPA.
/// Protected by the WORKER_SHARED_SECRET header. Never exposed publicly in
/// production — reverse-proxy should block /internal/* from the internet.
/// </summary>
[ApiController]
[Route("internal/matches")]
public class InternalMatchesController(
    Liga305DbContext db,
    IOptions<BotWorkerOptions> workerOptions,
    ILogger<InternalMatchesController> logger) : ControllerBase
{
    [HttpPatch("{id:guid}/dota-match-id")]
    public async Task<IActionResult> SetDotaMatchId(Guid id, [FromBody] SetDotaMatchIdRequest req)
    {
        if (!HasValidSecret()) return Unauthorized();

        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match is null) return NotFound();

        // Idempotent: allow callers to send the same value repeatedly.
        if (match.DotaMatchId == req.DotaMatchId && match.Status == MatchStatus.Live) return NoContent();

        match.DotaMatchId = req.DotaMatchId;
        match.Status = MatchStatus.Live;
        match.StartedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("Match {MatchId} now Live with Dota match ID {DotaMatchId}", id, req.DotaMatchId);
        return NoContent();
    }

    [HttpPost("{id:guid}/abandoned")]
    public async Task<IActionResult> Abandon(Guid id, [FromBody] AbandonRequest? req)
    {
        if (!HasValidSecret()) return Unauthorized();

        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match is null) return NotFound();
        if (match.Status == MatchStatus.Completed) return Conflict(new { error = "already_completed" });
        if (match.Status == MatchStatus.Abandoned) return NoContent();

        match.Status = MatchStatus.Abandoned;
        match.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("Match {MatchId} abandoned (reason={Reason})", id, req?.Reason ?? "unspecified");
        return NoContent();
    }

    public record SetDotaMatchIdRequest(long DotaMatchId);
    public record AbandonRequest(string? Reason);

    private bool HasValidSecret()
    {
        var expected = workerOptions.Value.SharedSecret;
        if (string.IsNullOrEmpty(expected)) return false;
        return Request.Headers.TryGetValue("x-worker-secret", out var provided)
            && string.Equals(provided.ToString(), expected, StringComparison.Ordinal);
    }
}

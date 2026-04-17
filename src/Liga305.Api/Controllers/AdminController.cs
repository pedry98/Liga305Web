using Liga305.Api.Auth;
using Liga305.Domain.Entities;
using Liga305.Infrastructure.BotWorker;
using Liga305.Infrastructure.Matches;
using Liga305.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Liga305.Api.Controllers;

[ApiController]
[Route("admin")]
[Authorize]
public class AdminController(
    Liga305DbContext db,
    CurrentUserAccessor currentUser,
    MatchSettlementService settlement,
    BotWorkerClient bot,
    ILogger<AdminController> logger) : ControllerBase
{
    /// <summary>
    /// Force-cancel a match: destroys the Dota lobby and marks the match Abandoned.
    /// </summary>
    [HttpPost("matches/{id:guid}/cancel")]
    public async Task<IActionResult> CancelMatch(Guid id)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match is null) return NotFound();
        if (match.Status == MatchStatus.Completed) return Conflict(new { error = "already_completed" });

        // Tell the bot to destroy the Dota lobby. Non-fatal if it fails — we still
        // mark the match Abandoned in the DB so it's out of the active set.
        var botOk = await bot.CancelLobbyAsync(id);

        match.Status = MatchStatus.Abandoned;
        match.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("Admin {Admin} cancelled match {MatchId} (botDestroyed={Bot})", me.DisplayName, id, botOk);
        return Ok(new { cancelled = true, botDestroyed = botOk });
    }

    /// <summary>
    /// Dev/test helper: fake-complete a match without needing an actual Dota 2
    /// game. Applies Glicko-2 MMR updates as if OpenDota returned the given result.
    /// </summary>
    [HttpPost("matches/{id:guid}/test-settle")]
    public async Task<IActionResult> TestSettle(Guid id, [FromBody] TestSettleRequest req)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        var ok = await settlement.SettleAsync(id, req.RadiantWin, req.DurationSec ?? 1800);
        if (!ok) return NotFound();
        return Ok(new { settled = true, radiantWin = req.RadiantWin });
    }

    public record TestSettleRequest(bool RadiantWin, int? DurationSec);

    /// <summary>
    /// Manual match-ID paste: if the bot misses the GC's match_id event, an admin
    /// can paste the Dota match ID here. Flips Match to Live so the OpenDota poller
    /// picks it up on its next tick.
    /// </summary>
    [HttpPost("matches/{id:guid}/set-dota-match-id")]
    public async Task<IActionResult> SetDotaMatchId(Guid id, [FromBody] SetDotaMatchIdRequest req)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        if (req.DotaMatchId <= 0) return BadRequest(new { error = "invalid_match_id" });

        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match is null) return NotFound();
        if (match.Status == MatchStatus.Completed) return Conflict(new { error = "already_completed" });

        match.DotaMatchId = req.DotaMatchId;
        match.Status = MatchStatus.Live;
        match.StartedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("Admin {Admin} pasted Dota match ID {DotaMatchId} for match {MatchId}",
            me.DisplayName, req.DotaMatchId, id);
        return Ok(new { dotaMatchId = req.DotaMatchId });
    }

    public record SetDotaMatchIdRequest(long DotaMatchId);

    /// <summary>
    /// Dev/test helper: ensure the active season's queue has at least <paramref name="targetSize"/>
    /// players by adding seeded "TestBot" users. Lets a single admin trigger the full match-formation
    /// flow without recruiting nine other humans.
    /// </summary>
    [HttpPost("queue/fill-bots")]
    public async Task<IActionResult> FillQueueWithBots([FromQuery] int targetSize = 9)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        var season = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive);
        if (season is null) return Conflict(new { error = "no_active_season" });

        targetSize = Math.Clamp(targetSize, 1, 9);

        var currentQueueSize = await db.QueueEntries
            .CountAsync(q => q.SeasonId == season.Id && q.Status == QueueStatus.Queued);

        var needed = Math.Max(0, targetSize - currentQueueSize);
        var added = 0;

        for (int i = 1; i <= 9 && added < needed; i++)
        {
            // Predictable fake SteamID64 — outside the real Steam range so it can't collide.
            var fakeSteamId = $"100000000000000{i:D2}";

            var bot = await db.Users.FirstOrDefaultAsync(u => u.SteamId64 == fakeSteamId);
            if (bot is null)
            {
                bot = new User
                {
                    Id = Guid.NewGuid(),
                    SteamId64 = fakeSteamId,
                    DisplayName = $"TestBot {i:D2}",
                    AvatarUrl = null,
                    CreatedAt = DateTime.UtcNow,
                    HasCustomProfile = true // Don't try to fetch persona from Steam for these
                };
                db.Users.Add(bot);
            }

            var enrolled = await db.SeasonPlayers
                .AnyAsync(sp => sp.SeasonId == season.Id && sp.UserId == bot.Id);
            if (!enrolled)
            {
                db.SeasonPlayers.Add(new SeasonPlayer
                {
                    SeasonId = season.Id,
                    UserId = bot.Id,
                    Mmr = season.StartingMmr + (i - 5) * 25, // Spread MMR around starting value
                    Rd = season.StartingRd,
                    Volatility = season.StartingVolatility,
                    JoinedAt = DateTime.UtcNow
                });
            }

            var alreadyQueued = await db.QueueEntries
                .AnyAsync(q => q.SeasonId == season.Id && q.UserId == bot.Id && q.Status == QueueStatus.Queued);
            if (!alreadyQueued)
            {
                db.QueueEntries.Add(new QueueEntry
                {
                    Id = Guid.NewGuid(),
                    SeasonId = season.Id,
                    UserId = bot.Id,
                    EnqueuedAt = DateTime.UtcNow.AddSeconds(-i),
                    Status = QueueStatus.Queued
                });
                added++;
            }
        }

        if (added > 0) await db.SaveChangesAsync();

        logger.LogInformation("Admin {Admin} filled queue with {Added} test bots (target={Target})", me.DisplayName, added, targetSize);
        return Ok(new { added, queueSize = currentQueueSize + added });
    }

    /// <summary>
    /// Removes any TestBot users from the queue. Useful between dev runs.
    /// </summary>
    [HttpPost("queue/clear-bots")]
    public async Task<IActionResult> ClearTestBotsFromQueue()
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        var botIds = await db.Users
            .Where(u => u.SteamId64.StartsWith("1000000000000000"))
            .Select(u => u.Id)
            .ToListAsync();

        var entries = await db.QueueEntries
            .Where(q => botIds.Contains(q.UserId) && q.Status == QueueStatus.Queued)
            .ToListAsync();

        foreach (var e in entries) e.Status = QueueStatus.Cancelled;
        await db.SaveChangesAsync();

        return Ok(new { cancelled = entries.Count });
    }
}

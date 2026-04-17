using Liga305.Api.Auth;
using Liga305.Api.Contracts;
using Liga305.Domain.Entities;
using Liga305.Infrastructure.BotWorker;
using Liga305.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Liga305.Api.Controllers;

[ApiController]
[Route("matches")]
public class MatchesController(
    Liga305DbContext db,
    CurrentUserAccessor currentUser,
    BotWorkerClient bot,
    ILogger<MatchesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<MatchSummaryDto>> GetAll([FromQuery] Guid? seasonId = null, [FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        var q = db.Matches.AsQueryable();
        if (seasonId is not null) q = q.Where(m => m.SeasonId == seasonId);

        var rows = await q
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .Select(m => new
            {
                m.Id,
                m.SeasonId,
                SeasonName = m.Season.Name,
                m.DotaMatchId,
                Status = m.Status.ToString(),
                m.CreatedAt,
                m.StartedAt,
                m.EndedAt,
                m.DurationSec,
                m.RadiantWin,
                RadiantAvg = m.Players.Where(p => p.Team == Team.Radiant).Average(p => (double?)p.MmrBefore) ?? 0,
                DireAvg = m.Players.Where(p => p.Team == Team.Dire).Average(p => (double?)p.MmrBefore) ?? 0
            })
            .ToListAsync();

        return rows.Select(r => new MatchSummaryDto(
            r.Id, r.SeasonId, r.SeasonName, r.DotaMatchId, r.Status,
            r.CreatedAt, r.StartedAt, r.EndedAt, r.DurationSec, r.RadiantWin,
            (int)Math.Round(r.RadiantAvg), (int)Math.Round(r.DireAvg))).ToList();
    }

    [HttpPost("{id:guid}/launch")]
    [Authorize]
    public async Task<IActionResult> Launch(Guid id)
    {
        var me = await currentUser.GetAsync();
        if (me is null) return Unauthorized();

        var match = await db.Matches
            .Include(m => m.Players)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (match is null) return NotFound();

        var onRoster = match.Players.Any(p => p.UserId == me.Id);
        if (!onRoster && !me.IsAdmin) return Forbid();

        if (match.Status != MatchStatus.Lobby)
        {
            return Conflict(new { error = "not_in_lobby_state", status = match.Status.ToString() });
        }

        var ok = await bot.LaunchMatchAsync(id);
        if (!ok) return StatusCode(502, new { error = "bot_launch_failed" });

        logger.LogInformation("User {User} launched match {MatchId}", me.DisplayName, id);
        return Ok(new { launched = true });
    }

    [HttpPost("{id:guid}/resend-invite")]
    [Authorize]
    public async Task<IActionResult> ResendInvite(Guid id)
    {
        var me = await currentUser.GetAsync();
        if (me is null) return Unauthorized();

        var match = await db.Matches
            .Include(m => m.Players)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (match is null) return NotFound();

        // Only roster players or admins can trigger a resend.
        var onRoster = match.Players.Any(p => p.UserId == me.Id);
        if (!onRoster && !me.IsAdmin) return Forbid();

        // Invites only make sense while the bot is still hosting the lobby.
        if (match.Status != MatchStatus.Lobby)
        {
            return Conflict(new { error = "not_in_lobby_state", status = match.Status.ToString() });
        }

        var invited = await bot.ResendInvitesAsync(id);
        if (invited is null)
        {
            return StatusCode(502, new { error = "bot_unavailable_or_unknown_match" });
        }

        logger.LogInformation("User {User} triggered resend-invite for match {MatchId} ({Invited} invites sent)", me.DisplayName, id, invited);
        return Ok(new { invited });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var m = await db.Matches
            .Include(x => x.Season)
            .Include(x => x.Players).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (m is null) return NotFound();

        var radiantAvg = m.Players.Where(p => p.Team == Team.Radiant).Select(p => p.MmrBefore).DefaultIfEmpty(0).Average();
        var direAvg = m.Players.Where(p => p.Team == Team.Dire).Select(p => p.MmrBefore).DefaultIfEmpty(0).Average();

        // Bot worker hardcodes a 4-minute abandon timer; mirror it here so the
        // SPA can show a live countdown. Keep this in sync with ABANDON_TIMEOUT_SEC.
        DateTime? abandonsAt = (m.Status == MatchStatus.Draft || m.Status == MatchStatus.Lobby)
            ? m.CreatedAt.AddMinutes(4)
            : null;

        return Ok(new MatchDetailDto(
            m.Id, m.SeasonId, m.Season.Name, m.DotaMatchId, m.Status.ToString(),
            m.CreatedAt, m.StartedAt, m.EndedAt, m.DurationSec, m.RadiantWin,
            (int)Math.Round(radiantAvg), (int)Math.Round(direAvg),
            m.LobbyName, m.LobbyPassword, m.BotSteamName,
            abandonsAt,
            m.Players
                .OrderBy(p => p.Team).ThenByDescending(p => p.MmrBefore)
                .Select(p => new MatchPlayerDto(
                    p.UserId, p.User.SteamId64, p.User.DisplayName, p.User.AvatarUrl,
                    p.Team.ToString(),
                    (int)Math.Round(p.MmrBefore),
                    p.MmrAfter is null ? null : (int)Math.Round(p.MmrAfter.Value),
                    p.JoinedLobby, p.Abandoned,
                    p.Kills, p.Deaths, p.Assists))
                .ToList()));
    }
}

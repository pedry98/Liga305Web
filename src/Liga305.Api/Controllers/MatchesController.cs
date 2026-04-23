using System.Text.Json;
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
    IServiceScopeFactory scopeFactory,
    ILogger<MatchesController> logger) : ControllerBase
{
    private const int RosterSize = 10;
    private const int PicksRequired = 8; // 10 - 2 captains

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

        var match = await db.Matches.Include(m => m.Players).FirstOrDefaultAsync(m => m.Id == id);
        if (match is null) return NotFound();

        var onRoster = match.Players.Any(p => p.UserId == me.Id);
        if (!onRoster && !me.IsAdmin) return Forbid();

        if (match.Status != MatchStatus.Lobby)
            return Conflict(new { error = "not_in_lobby_state", status = match.Status.ToString() });

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

        var match = await db.Matches.Include(m => m.Players).FirstOrDefaultAsync(m => m.Id == id);
        if (match is null) return NotFound();

        var onRoster = match.Players.Any(p => p.UserId == me.Id);
        if (!onRoster && !me.IsAdmin) return Forbid();

        if (match.Status != MatchStatus.Lobby)
            return Conflict(new { error = "not_in_lobby_state", status = match.Status.ToString() });

        var invited = await bot.ResendInvitesAsync(id);
        if (invited is null)
            return StatusCode(502, new { error = "bot_unavailable_or_unknown_match" });

        logger.LogInformation("User {User} triggered resend-invite for match {MatchId} ({Invited} invites sent)", me.DisplayName, id, invited);
        return Ok(new { invited });
    }

    /// <summary>
    /// Captain picks a player onto their team. Pick order alternates Radiant, Dire,
    /// Radiant, Dire, ... starting with the Radiant captain. Once all 8 unpicked
    /// players have been chosen, the match transitions to Draft and the bot is
    /// told to create the Dota lobby.
    /// </summary>
    [HttpPost("{id:guid}/pick")]
    [Authorize]
    public async Task<IActionResult> Pick(Guid id, [FromBody] PickPlayerRequest req)
    {
        var me = await currentUser.GetAsync();
        if (me is null) return Unauthorized();

        var match = await db.Matches
            .Include(m => m.Players).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (match is null) return NotFound();
        if (match.Status != MatchStatus.Drafting)
            return Conflict(new { error = "not_in_drafting_state", status = match.Status.ToString() });

        // Whose turn? Picks alternate Radiant→Dire→Radiant... starting with Radiant.
        var picksMade = match.Players.Count(p => p.PickOrder is > 0);
        if (picksMade >= PicksRequired)
            return Conflict(new { error = "all_picks_made" });

        var currentTeam = picksMade % 2 == 0 ? Team.Radiant : Team.Dire;
        var currentCaptainId = currentTeam == Team.Radiant
            ? match.RadiantCaptainUserId
            : match.DireCaptainUserId;
        if (currentCaptainId != me.Id && !me.IsAdmin)
            return Forbid();

        var target = match.Players.FirstOrDefault(p => p.UserId == req.UserId);
        if (target is null) return BadRequest(new { error = "user_not_in_match" });
        if (target.PickOrder is not null) return Conflict(new { error = "user_already_picked" });

        target.Team = currentTeam;
        target.PickOrder = picksMade + 1;
        await db.SaveChangesAsync();

        logger.LogInformation("Match {MatchId}: pick #{Pick} by {Captain} → {Picked} ({Team})",
            id, picksMade + 1, currentCaptainId, target.User.DisplayName, currentTeam);

        // If that was the final pick, transition to Draft and fire the bot lobby creation.
        if (picksMade + 1 >= PicksRequired)
        {
            match.Status = MatchStatus.Draft;
            await db.SaveChangesAsync();

            var lobbyPlayers = match.Players
                .OrderBy(p => p.Team).ThenBy(p => p.PickOrder)
                .Select(p => new BotPlayerSpec(p.User.SteamId64, p.Team.ToString()))
                .ToList();

            _ = Task.Run(() => CreateLobbyForMatchAsync(match.Id, lobbyPlayers));
        }

        // Return the fresh match so the client UI updates immediately.
        return await GetById(id);
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

        // Bot worker hardcodes a 4-minute abandon timer; expose so SPA can show countdown.
        DateTime? abandonsAt = (m.Status == MatchStatus.Drafting || m.Status == MatchStatus.Draft || m.Status == MatchStatus.Lobby)
            ? m.CreatedAt.AddMinutes(4)
            : null;

        // Drafting state: which captain is currently picking?
        Guid? currentPickerUserId = null;
        string? currentPickerTeam = null;
        if (m.Status == MatchStatus.Drafting)
        {
            var picksMade = m.Players.Count(p => p.PickOrder is > 0);
            if (picksMade < PicksRequired)
            {
                var team = picksMade % 2 == 0 ? Team.Radiant : Team.Dire;
                currentPickerUserId = team == Team.Radiant ? m.RadiantCaptainUserId : m.DireCaptainUserId;
                currentPickerTeam = team.ToString();
            }
        }

        var captainIds = new HashSet<Guid?> { m.RadiantCaptainUserId, m.DireCaptainUserId };

        static IReadOnlyList<int>? ParseIntArray(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<List<int>>(json); }
            catch { return null; }
        }

        return Ok(new MatchDetailDto(
            m.Id, m.SeasonId, m.Season.Name, m.DotaMatchId, m.Status.ToString(),
            m.CreatedAt, m.StartedAt, m.EndedAt, m.DurationSec, m.RadiantWin,
            (int)Math.Round(radiantAvg), (int)Math.Round(direAvg),
            m.LobbyName, m.LobbyPassword, m.BotSteamName,
            abandonsAt,
            m.RadiantCaptainUserId,
            m.DireCaptainUserId,
            currentPickerUserId,
            currentPickerTeam,
            ParseIntArray(m.RadiantGoldAdvJson),
            ParseIntArray(m.RadiantXpAdvJson),
            m.Players
                .OrderBy(p => p.PickOrder ?? 999).ThenBy(p => p.Team).ThenByDescending(p => p.MmrBefore)
                .Select(p => new MatchPlayerDto(
                    p.UserId, p.User.SteamId64, p.User.DisplayName, p.User.AvatarUrl,
                    p.Team.ToString(),
                    (int)Math.Round(p.MmrBefore),
                    p.MmrAfter is null ? null : (int)Math.Round(p.MmrAfter.Value),
                    p.JoinedLobby, p.Abandoned,
                    p.Kills, p.Deaths, p.Assists,
                    p.PickOrder,
                    captainIds.Contains(p.UserId),
                    p.PickOrder is not null,
                    p.HeroId, p.LastHits, p.Denies, p.GoldPerMin, p.XpPerMin,
                    p.NetWorth, p.HeroDamage, p.TowerDamage, p.HeroHealing,
                    new int?[] { p.Item0, p.Item1, p.Item2, p.Item3, p.Item4, p.Item5 },
                    new int?[] { p.Backpack0, p.Backpack1, p.Backpack2 },
                    p.ItemNeutral))
                .ToList()));
    }

    /// <summary>
    /// Background task: fires the bot worker to create the Dota lobby once all
    /// picks are in. On success flips Match.Status to Lobby; on failure logs but
    /// leaves the match in Draft so an admin can re-trigger if needed.
    /// </summary>
    private async Task CreateLobbyForMatchAsync(Guid matchId, IReadOnlyList<BotPlayerSpec> players)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var scopedBot = scope.ServiceProvider.GetRequiredService<BotWorkerClient>();
            var scopedDb  = scope.ServiceProvider.GetRequiredService<Liga305DbContext>();
            var scopedLog = scope.ServiceProvider.GetRequiredService<ILogger<MatchesController>>();

            var result = await scopedBot.CreateLobbyAsync(matchId, players);
            var match = await scopedDb.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
            if (match is null)
            {
                scopedLog.LogWarning("Match {MatchId} disappeared before lobby creation completed", matchId);
                return;
            }
            if (result is null)
            {
                scopedLog.LogWarning("Lobby creation returned null for match {MatchId}; leaving Status=Draft", matchId);
                return;
            }

            match.LobbyName = result.LobbyName;
            match.LobbyPassword = result.Password;
            match.BotSteamName = result.BotSteamName;
            match.Status = MatchStatus.Lobby;
            await scopedDb.SaveChangesAsync();
            scopedLog.LogInformation("Match {MatchId} now in Lobby (simulated={Simulated})", matchId, result.Simulated);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background lobby creation failed for match {MatchId}", matchId);
        }
    }
}

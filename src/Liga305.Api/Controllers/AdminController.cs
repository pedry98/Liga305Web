using Liga305.Api.Auth;
using Liga305.Domain.Entities;
using Liga305.Infrastructure.BotWorker;
using Liga305.Infrastructure.Matches;
using Liga305.Infrastructure.OpenDota;
using Liga305.Infrastructure.Persistence;
using Liga305.Infrastructure.Steam;
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
    OpenDotaClient openDota,
    SteamWebApiClient steam,
    ILogger<AdminController> logger) : ControllerBase
{
    /// <summary>
    /// Wipe the league back to a clean slate:
    ///   * destroys all matches (and the cascading MatchPlayers + MmrHistory rows),
    ///   * cancels all queue entries,
    ///   * deletes every "TestBot" user and their season enrollments,
    ///   * resets every remaining player's active-season MMR to the season default
    ///     (1000 by request) with zero W/L/abandons.
    ///
    /// Requires `?confirm=YES` to avoid accidental fat-finger calls.
    /// </summary>
    [HttpPost("reset-league")]
    public async Task<IActionResult> ResetLeague([FromQuery] string? confirm = null)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();
        if (confirm != "YES") return BadRequest(new { error = "missing_confirm", hint = "POST /admin/reset-league?confirm=YES" });

        await using var tx = await db.Database.BeginTransactionAsync();

        // 1. Wipe all matches (cascades into MatchPlayers and MmrHistory via FK).
        var matchIds = await db.Matches.Select(m => m.Id).ToListAsync();
        if (matchIds.Count > 0)
        {
            await db.MmrHistory.Where(h => matchIds.Contains(h.MatchId)).ExecuteDeleteAsync();
            await db.MatchPlayers.Where(mp => matchIds.Contains(mp.MatchId)).ExecuteDeleteAsync();
            await db.QueueEntries.Where(q => q.MatchId != null && matchIds.Contains(q.MatchId.Value)).ExecuteDeleteAsync();
            await db.Matches.Where(m => matchIds.Contains(m.Id)).ExecuteDeleteAsync();
        }

        // 2. Cancel any orphan queue entries (e.g. a real user mid-queue).
        await db.QueueEntries.ExecuteDeleteAsync();

        // 3. Delete TestBot users (their fake SteamIDs start with 1000000000000000)
        //    and any leftover MmrHistory rows referencing them.
        var botUserIds = await db.Users
            .Where(u => u.SteamId64.StartsWith("1000000000000000"))
            .Select(u => u.Id)
            .ToListAsync();
        if (botUserIds.Count > 0)
        {
            await db.MmrHistory.Where(h => botUserIds.Contains(h.UserId)).ExecuteDeleteAsync();
            await db.SeasonPlayers.Where(sp => botUserIds.Contains(sp.UserId)).ExecuteDeleteAsync();
            await db.Users.Where(u => botUserIds.Contains(u.Id)).ExecuteDeleteAsync();
        }

        // 4. Reset every remaining season player to the new starting line.
        const double startingMmr = 1000;
        const double startingRd = 350;
        const double startingVol = 0.06;
        await db.SeasonPlayers.ExecuteUpdateAsync(s => s
            .SetProperty(sp => sp.Mmr, startingMmr)
            .SetProperty(sp => sp.Rd, startingRd)
            .SetProperty(sp => sp.Volatility, startingVol)
            .SetProperty(sp => sp.Wins, 0)
            .SetProperty(sp => sp.Losses, 0)
            .SetProperty(sp => sp.Abandons, 0));

        // 5. Bump the active season's defaults to match (so any new sign-ins start at 1000).
        await db.Seasons.Where(s => s.IsActive).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.StartingMmr, startingMmr));

        await tx.CommitAsync();

        var realUsersCount = await db.Users.CountAsync();
        logger.LogWarning("Admin {Admin} reset the league: {Matches} matches deleted, {Bots} bot users removed, {Real} real users reset", me.DisplayName, matchIds.Count, botUserIds.Count, realUsersCount);
        return Ok(new
        {
            matchesDeleted = matchIds.Count,
            botUsersRemoved = botUserIds.Count,
            realUsersReset = realUsersCount,
            startingMmr
        });
    }

    /// <summary>
    /// Create a new season. If <paramref name="req"/>.MakeActive is true (default),
    /// any currently-active season is ended first, the new one is flagged active,
    /// and every existing user is auto-enrolled at the season's StartingMmr.
    /// </summary>
    [HttpPost("seasons")]
    public async Task<IActionResult> CreateSeason([FromBody] CreateSeasonRequest req)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "name_required" });
        if (req.EndsAt <= req.StartsAt) return BadRequest(new { error = "ends_before_starts" });

        await using var tx = await db.Database.BeginTransactionAsync();

        if (req.MakeActive)
        {
            // End any currently-active season(s) before activating the new one.
            await db.Seasons.Where(s => s.IsActive).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsActive, false)
                .SetProperty(x => x.EndsAt, DateTime.UtcNow));
        }

        var season = new Season
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            StartsAt = DateTime.SpecifyKind(req.StartsAt, DateTimeKind.Utc),
            EndsAt = DateTime.SpecifyKind(req.EndsAt, DateTimeKind.Utc),
            IsActive = req.MakeActive,
            StartingMmr = req.StartingMmr ?? 1000,
            StartingRd = 350,
            StartingVolatility = 0.06,
            CreatedAt = DateTime.UtcNow
        };
        db.Seasons.Add(season);

        if (req.MakeActive)
        {
            // Auto-enroll every existing user (skip TestBots — they get cleared on reset).
            var userIds = await db.Users
                .Where(u => !u.SteamId64.StartsWith("1000000000000000"))
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var uid in userIds)
            {
                db.SeasonPlayers.Add(new SeasonPlayer
                {
                    SeasonId = season.Id,
                    UserId = uid,
                    Mmr = season.StartingMmr,
                    Rd = season.StartingRd,
                    Volatility = season.StartingVolatility,
                    JoinedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogWarning("Admin {Admin} created season {Name} (active={Active})",
            me.DisplayName, season.Name, season.IsActive);

        return Ok(new
        {
            id = season.Id,
            name = season.Name,
            startsAt = season.StartsAt,
            endsAt = season.EndsAt,
            isActive = season.IsActive,
            playerCount = req.MakeActive ? await db.SeasonPlayers.CountAsync(sp => sp.SeasonId == season.Id) : 0,
            matchCount = 0
        });
    }

    public record CreateSeasonRequest(string Name, DateTime StartsAt, DateTime EndsAt, bool MakeActive = true, double? StartingMmr = null);

    /// <summary>End a season early (or close one already past its EndsAt). Marks it inactive.</summary>
    [HttpPost("seasons/{id:guid}/end")]
    public async Task<IActionResult> EndSeason(Guid id)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == id);
        if (season is null) return NotFound();
        if (!season.IsActive) return Conflict(new { error = "already_ended" });

        season.IsActive = false;
        if (season.EndsAt > DateTime.UtcNow) season.EndsAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogWarning("Admin {Admin} ended season {Name}", me.DisplayName, season.Name);
        return Ok(new { id = season.Id, isActive = false, endsAt = season.EndsAt });
    }

    /// <summary>Edit a season's name and/or scheduled dates. Does not change active status.</summary>
    [HttpPatch("seasons/{id:guid}")]
    public async Task<IActionResult> UpdateSeason(Guid id, [FromBody] UpdateSeasonRequest req)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == id);
        if (season is null) return NotFound();

        if (req.Name is { } name)
        {
            if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "name_required" });
            season.Name = name.Trim();
        }
        if (req.StartsAt is { } starts) season.StartsAt = DateTime.SpecifyKind(starts, DateTimeKind.Utc);
        if (req.EndsAt is { } ends) season.EndsAt = DateTime.SpecifyKind(ends, DateTimeKind.Utc);
        if (season.EndsAt <= season.StartsAt) return BadRequest(new { error = "ends_before_starts" });

        await db.SaveChangesAsync();
        logger.LogInformation("Admin {Admin} updated season {Name}", me.DisplayName, season.Name);
        return Ok(new
        {
            id = season.Id,
            name = season.Name,
            startsAt = season.StartsAt,
            endsAt = season.EndsAt,
            isActive = season.IsActive
        });
    }

    public record UpdateSeasonRequest(string? Name, DateTime? StartsAt, DateTime? EndsAt);

    /// <summary>
    /// List every registered user with their active-season aggregates so the admin
    /// console can show a real users table.
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        var activeSeason = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive);
        var seasonId = activeSeason?.Id;

        var rows = await db.Users
            .OrderBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id,
                u.SteamId64,
                u.DisplayName,
                u.AvatarUrl,
                u.IsAdmin,
                u.IsBanned,
                u.CreatedAt,
                Mmr = seasonId == null ? (double?)null : db.SeasonPlayers
                    .Where(sp => sp.SeasonId == seasonId && sp.UserId == u.Id)
                    .Select(sp => (double?)sp.Mmr).FirstOrDefault(),
                Wins = seasonId == null ? 0 : db.SeasonPlayers
                    .Where(sp => sp.SeasonId == seasonId && sp.UserId == u.Id)
                    .Select(sp => sp.Wins).FirstOrDefault(),
                Losses = seasonId == null ? 0 : db.SeasonPlayers
                    .Where(sp => sp.SeasonId == seasonId && sp.UserId == u.Id)
                    .Select(sp => sp.Losses).FirstOrDefault(),
                Abandons = seasonId == null ? 0 : db.SeasonPlayers
                    .Where(sp => sp.SeasonId == seasonId && sp.UserId == u.Id)
                    .Select(sp => sp.Abandons).FirstOrDefault()
            })
            .ToListAsync();

        var result = rows.Select(r => new
        {
            id = r.Id,
            steamId64 = r.SteamId64,
            displayName = r.DisplayName,
            avatarUrl = r.AvatarUrl,
            mmr = r.Mmr is null ? (int?)null : (int)Math.Round(r.Mmr.Value),
            wins = r.Wins,
            losses = r.Losses,
            abandons = r.Abandons,
            isAdmin = r.IsAdmin,
            isBanned = r.IsBanned,
            joinedAt = r.CreatedAt
        });
        return Ok(result);
    }

    /// <summary>Toggle the admin flag on a user.</summary>
    [HttpPost("users/{id:guid}/toggle-admin")]
    public async Task<IActionResult> ToggleAdmin(Guid id)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        // Refuse to demote yourself if you'd leave the league with zero admins.
        if (user.Id == me.Id && user.IsAdmin)
        {
            var otherAdmins = await db.Users.AnyAsync(u => u.Id != me.Id && u.IsAdmin);
            if (!otherAdmins) return Conflict(new { error = "would_leave_no_admins" });
        }

        user.IsAdmin = !user.IsAdmin;
        await db.SaveChangesAsync();

        logger.LogInformation("Admin {Admin} {Action} on user {User}",
            me.DisplayName, user.IsAdmin ? "granted admin" : "revoked admin", user.DisplayName);
        return Ok(new { isAdmin = user.IsAdmin });
    }

    /// <summary>Toggle the banned flag on a user.</summary>
    [HttpPost("users/{id:guid}/toggle-ban")]
    public async Task<IActionResult> ToggleBan(Guid id)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (user.Id == me.Id) return Conflict(new { error = "cant_ban_self" });

        user.IsBanned = !user.IsBanned;
        await db.SaveChangesAsync();

        logger.LogInformation("Admin {Admin} {Action} user {User}",
            me.DisplayName, user.IsBanned ? "banned" : "unbanned", user.DisplayName);
        return Ok(new { isBanned = user.IsBanned });
    }

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
    /// Probe OpenDota for a given Dota match ID and return a preview plus enough
    /// info to decide whether this match can be imported into the league as a
    /// historical result. Read-only — touches nothing in our DB.
    /// </summary>
    [HttpGet("opendota/match/{dotaMatchId:long}")]
    public async Task<IActionResult> ProbeOpenDotaMatch(long dotaMatchId, CancellationToken ct)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();
        if (dotaMatchId <= 0) return BadRequest(new { error = "invalid_match_id" });

        var match = await openDota.GetMatchAsync(dotaMatchId, ct);
        if (match is null)
        {
            return Ok(new
            {
                found = false,
                dotaMatchId,
                message = "OpenDota returned no usable data. The match may not be indexed yet (fresh matches can take a few minutes), the ID may be wrong, or the profile may be private."
            });
        }

        // Resolve each OpenDota account_id back to a League user by Steam64, and
        // attach that user's active-season MMR so the admin can see who would get
        // updated if they imported this match.
        var steamId64s = match.Players
            .Where(p => p.AccountId > 0)
            .Select(p => (p.AccountId + SteamIdBase).ToString())
            .ToList();

        var users = steamId64s.Count == 0
            ? []
            : await db.Users
                .Where(u => steamId64s.Contains(u.SteamId64))
                .Select(u => new { u.Id, u.SteamId64, u.DisplayName })
                .ToListAsync(ct);
        var usersBySteamId = users.ToDictionary(u => u.SteamId64);

        var activeSeason = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive, ct);
        var userIds = users.Select(u => u.Id).ToList();
        var seasonMmr = activeSeason is null || userIds.Count == 0
            ? new Dictionary<Guid, double>()
            : await db.SeasonPlayers
                .Where(sp => sp.SeasonId == activeSeason.Id && userIds.Contains(sp.UserId))
                .ToDictionaryAsync(sp => sp.UserId, sp => sp.Mmr, ct);

        var alreadyImported = await db.Matches.AnyAsync(m => m.DotaMatchId == dotaMatchId, ct);

        var playersView = match.Players.Select(p =>
        {
            var steamId64 = p.AccountId > 0 ? (p.AccountId + SteamIdBase).ToString() : null;
            var resolved = steamId64 is not null && usersBySteamId.TryGetValue(steamId64, out var u) ? u : null;
            int? mmr = resolved is not null && seasonMmr.TryGetValue(resolved.Id, out var m) ? (int)Math.Round(m) : null;
            return new
            {
                accountId = p.AccountId,
                steamId64,
                isRadiant = p.IsRadiant,
                playerSlot = p.PlayerSlot,
                kills = p.Kills,
                deaths = p.Deaths,
                assists = p.Assists,
                abandoned = p.Abandoned,
                leagueUserId = resolved?.Id,
                leagueDisplayName = resolved?.DisplayName,
                seasonMmr = mmr,
                matched = resolved is not null
            };
        }).ToList();

        var matchedCount = playersView.Count(p => p.matched);
        var radiantTotal = match.Players.Count(p => p.IsRadiant);
        var direTotal = match.Players.Count - radiantTotal;
        var anonymousSlots = match.Players.Count(p => p.AccountId <= 0);
        var willCreatePlaceholders = match.Players.Count - matchedCount - anonymousSlots;

        string? importBlockedReason = null;
        if (alreadyImported) importBlockedReason = "This Dota match ID has already been imported into the league.";
        else if (activeSeason is null) importBlockedReason = "There is no active season to import into.";
        else if (match.Players.Count != 10) importBlockedReason = $"Match must have 10 players, OpenDota reported {match.Players.Count}.";
        else if (anonymousSlots > 0) importBlockedReason = $"{anonymousSlots} player(s) have hidden Dota profiles, so they can't be tied to a Steam account. Ask them to make their profile public and re-probe.";
        else if (radiantTotal != 5 || direTotal != 5) importBlockedReason = $"Teams aren't 5v5 (Radiant {radiantTotal}, Dire {direTotal}).";

        return Ok(new
        {
            found = true,
            dotaMatchId = match.MatchId,
            radiantWin = match.RadiantWin,
            durationSec = match.DurationSec,
            startedAt = match.StartedAt,
            parsed = match.Parsed,
            playerCount = match.Players.Count,
            matchedCount,
            willCreatePlaceholders,
            activeSeasonId = activeSeason?.Id,
            activeSeasonName = activeSeason?.Name,
            alreadyImported,
            importable = importBlockedReason is null,
            importBlockedReason,
            players = playersView
        });
    }

    /// <summary>
    /// Import a historical Dota match that didn't get auto-recorded. Creates the
    /// Match + MatchPlayers in the active season and runs the normal settlement
    /// pipeline (Elo, W/L, MmrHistory, K/D/A). Admin-only recovery tool.
    ///
    /// Strict: fails unless all 10 OpenDota players map to League users and the
    /// match isn't already imported.
    /// </summary>
    [HttpPost("matches/import-from-opendota")]
    public async Task<IActionResult> ImportFromOpenDota([FromBody] ImportFromOpenDotaRequest req, CancellationToken ct)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();
        if (req.DotaMatchId <= 0) return BadRequest(new { error = "invalid_match_id" });

        if (await db.Matches.AnyAsync(m => m.DotaMatchId == req.DotaMatchId, ct))
            return Conflict(new { error = "already_imported" });

        var season = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive, ct);
        if (season is null) return Conflict(new { error = "no_active_season" });

        var dotaMatch = await openDota.GetMatchAsync(req.DotaMatchId, ct);
        if (dotaMatch is null)
            return UnprocessableEntity(new { error = "opendota_not_ready", hint = "OpenDota doesn't have the stats yet. Try again in a few minutes." });

        if (dotaMatch.Players.Count != 10)
            return UnprocessableEntity(new { error = "wrong_player_count", count = dotaMatch.Players.Count });

        // Anonymous slots (account_id = 0) are hidden-profile players — we can't
        // tie them to a Steam identity, so they can never be claimed by a real
        // sign-in later. Refuse to import those.
        if (dotaMatch.Players.Any(p => p.AccountId <= 0))
            return UnprocessableEntity(new { error = "anonymous_slot", hint = "One or more players have a hidden Dota profile." });

        var radiantCount = dotaMatch.Players.Count(p => p.IsRadiant);
        if (radiantCount != 5)
            return UnprocessableEntity(new { error = "not_five_v_five", radiant = radiantCount, dire = 10 - radiantCount });

        var steamId64s = dotaMatch.Players
            .Select(p => (p.AccountId + SteamIdBase).ToString())
            .ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existingUsers = await db.Users
            .Where(u => steamId64s.Contains(u.SteamId64))
            .ToListAsync(ct);
        var usersBySteamId = existingUsers.ToDictionary(u => u.SteamId64);

        // For any OpenDota slot not yet in our Users table, create a placeholder
        // User row keyed by Steam64. When that player eventually signs in with
        // Steam, SteamAuthController finds them by SteamId64 and refreshes their
        // DisplayName/AvatarUrl (since HasCustomProfile=false) — so the stats
        // we record here get attributed to them seamlessly on first login.
        var placeholdersCreated = 0;
        foreach (var op in dotaMatch.Players)
        {
            var steamId64 = (op.AccountId + SteamIdBase).ToString();
            if (usersBySteamId.ContainsKey(steamId64)) continue;

            // Best-effort persona fetch. If the Steam Web API key isn't configured
            // or the call fails, fall back to "Player <accountId>" — Steam sign-in
            // will replace it later anyway.
            var summary = await steam.GetPlayerSummaryAsync(steamId64, ct);
            var placeholder = new User
            {
                Id = Guid.NewGuid(),
                SteamId64 = steamId64,
                DisplayName = summary?.PersonaName ?? $"Player {op.AccountId}",
                AvatarUrl = summary?.AvatarUrl,
                CreatedAt = DateTime.UtcNow,
                HasCustomProfile = false
            };
            db.Users.Add(placeholder);
            usersBySteamId[steamId64] = placeholder;
            placeholdersCreated++;
        }
        if (placeholdersCreated > 0) await db.SaveChangesAsync(ct);

        // Auto-enroll any user who isn't yet in the active season (fresh sign-ins
        // who are playing their first league game retroactively, plus the
        // placeholders we just created). Use season starting defaults.
        var userIds = usersBySteamId.Values.Select(u => u.Id).ToList();
        var enrolledIds = await db.SeasonPlayers
            .Where(sp => sp.SeasonId == season.Id && userIds.Contains(sp.UserId))
            .Select(sp => sp.UserId)
            .ToListAsync(ct);
        var enrolledSet = enrolledIds.ToHashSet();

        foreach (var uid in userIds.Where(id => !enrolledSet.Contains(id)))
        {
            db.SeasonPlayers.Add(new SeasonPlayer
            {
                SeasonId = season.Id,
                UserId = uid,
                Mmr = season.StartingMmr,
                Rd = season.StartingRd,
                Volatility = season.StartingVolatility,
                JoinedAt = DateTime.UtcNow
            });
        }
        if (userIds.Count != enrolledSet.Count) await db.SaveChangesAsync(ct);

        // Snapshot pre-match MMR from SeasonPlayers so the MatchPlayer rows carry
        // the same "MmrBefore" data the normal flow would have recorded.
        var seasonPlayers = await db.SeasonPlayers
            .Where(sp => sp.SeasonId == season.Id && userIds.Contains(sp.UserId))
            .ToDictionaryAsync(sp => sp.UserId, ct);

        var match = new Match
        {
            Id = Guid.NewGuid(),
            SeasonId = season.Id,
            DotaMatchId = req.DotaMatchId,
            Status = MatchStatus.Live,
            CreatedAt = DateTime.UtcNow,
            StartedAt = dotaMatch.StartedAt ?? DateTime.UtcNow
        };
        db.Matches.Add(match);

        foreach (var op in dotaMatch.Players)
        {
            var steamId64 = (op.AccountId + SteamIdBase).ToString();
            var user = usersBySteamId[steamId64];
            var sp = seasonPlayers[user.Id];

            db.MatchPlayers.Add(new MatchPlayer
            {
                MatchId = match.Id,
                UserId = user.Id,
                Team = op.IsRadiant ? Team.Radiant : Team.Dire,
                MmrBefore = sp.Mmr,
                RdBefore = sp.Rd,
                JoinedLobby = true
                // K/D/A and MmrAfter get filled in by SettleFromOpenDotaAsync below.
            });
        }

        await db.SaveChangesAsync(ct);

        // Hand off to the same pipeline the live poller uses — this populates
        // K/D/A, abandon flags, MMR after, MmrHistory, flips to Completed.
        var settled = await settlement.SettleFromOpenDotaAsync(match.Id, dotaMatch, ct);
        if (!settled)
        {
            // Shouldn't happen — we just created it — but roll back if so.
            await tx.RollbackAsync(ct);
            return StatusCode(500, new { error = "settle_failed" });
        }

        await tx.CommitAsync(ct);

        logger.LogWarning(
            "Admin {Admin} imported historical Dota match {DotaMatchId} as league match {MatchId} (radiantWin={Radiant}, duration={Dur}s, placeholdersCreated={Placeholders})",
            me.DisplayName, req.DotaMatchId, match.Id, dotaMatch.RadiantWin, dotaMatch.DurationSec, placeholdersCreated);

        return Ok(new
        {
            imported = true,
            matchId = match.Id,
            dotaMatchId = req.DotaMatchId,
            seasonId = season.Id,
            seasonName = season.Name,
            radiantWin = dotaMatch.RadiantWin,
            durationSec = dotaMatch.DurationSec,
            placeholdersCreated
        });
    }

    public record ImportFromOpenDotaRequest(long DotaMatchId);

    // Steam64 = AccountId32 + 76561197960265728. Used here + in MatchSettlementService.
    private const long SteamIdBase = 76561197960265728L;

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
    /// Kicks a single user from the active season's queue. Cancels their queued entry
    /// without affecting their MMR or season enrollment. No-op if they're not queued.
    /// </summary>
    [HttpPost("queue/kick/{userId:guid}")]
    public async Task<IActionResult> KickFromQueue(Guid userId)
    {
        var me = await currentUser.GetAsync();
        if (me is null || !me.IsAdmin) return Forbid();

        var season = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive);
        if (season is null) return Conflict(new { error = "no_active_season" });

        var entry = await db.QueueEntries
            .Include(q => q.User)
            .FirstOrDefaultAsync(q => q.SeasonId == season.Id
                                     && q.UserId == userId
                                     && q.Status == QueueStatus.Queued);
        if (entry is null) return NotFound(new { error = "not_in_queue" });

        entry.Status = QueueStatus.Cancelled;
        await db.SaveChangesAsync();

        logger.LogInformation("Admin {Admin} kicked {User} from queue", me.DisplayName, entry.User.DisplayName);
        return Ok(new { kicked = true, userId });
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

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
[Route("queue")]
public class QueueController(
    Liga305DbContext db,
    CurrentUserAccessor currentUser,
    IServiceScopeFactory scopeFactory,
    ILogger<QueueController> logger) : ControllerBase
{
    private const int QueueCapacity = 10;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var season = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive);
        if (season is null) return NotFound();

        var queued = await db.QueueEntries
            .Where(q => q.SeasonId == season.Id && q.Status == QueueStatus.Queued)
            .OrderBy(q => q.EnqueuedAt)
            .Select(q => new
            {
                q.UserId,
                q.EnqueuedAt,
                q.User.DisplayName,
                q.User.AvatarUrl,
                Mmr = db.SeasonPlayers
                    .Where(sp => sp.SeasonId == season.Id && sp.UserId == q.UserId)
                    .Select(sp => sp.Mmr)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var me = await currentUser.GetAsync();
        var selfInQueue = me is not null && queued.Any(q => q.UserId == me.Id);

        var lastMatchId = await db.Matches
            .Where(m => m.SeasonId == season.Id)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync();

        return Ok(new QueueStateDto(
            season.Id,
            queued.Count,
            QueueCapacity,
            selfInQueue,
            lastMatchId,
            queued.Select(q => new QueueEntryDto(
                q.UserId, q.DisplayName, q.AvatarUrl, (int)Math.Round(q.Mmr), q.EnqueuedAt)).ToList()));
    }

    [HttpPost("join")]
    [Authorize]
    public async Task<IActionResult> Join()
    {
        var me = await currentUser.GetAsync();
        if (me is null) return Unauthorized();
        if (me.IsBanned) return Forbid();

        var season = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive);
        if (season is null) return Conflict(new { error = "no_active_season" });

        var alreadyQueued = await db.QueueEntries
            .AnyAsync(q => q.SeasonId == season.Id && q.UserId == me.Id && q.Status == QueueStatus.Queued);
        if (alreadyQueued) return await Get();

        db.QueueEntries.Add(new QueueEntry
        {
            Id = Guid.NewGuid(),
            SeasonId = season.Id,
            UserId = me.Id,
            EnqueuedAt = DateTime.UtcNow,
            Status = QueueStatus.Queued
        });
        await db.SaveChangesAsync();

        await TryFormMatchAsync(season.Id);

        return await Get();
    }

    [HttpDelete("leave")]
    [Authorize]
    public async Task<IActionResult> Leave()
    {
        var me = await currentUser.GetAsync();
        if (me is null) return Unauthorized();

        var season = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive);
        if (season is null) return Conflict(new { error = "no_active_season" });

        var entry = await db.QueueEntries
            .FirstOrDefaultAsync(q => q.SeasonId == season.Id && q.UserId == me.Id && q.Status == QueueStatus.Queued);
        if (entry is not null)
        {
            entry.Status = QueueStatus.Cancelled;
            await db.SaveChangesAsync();
        }
        return await Get();
    }

    private async Task TryFormMatchAsync(Guid seasonId)
    {
        Guid? newMatchId = null;
        List<BotPlayerSpec>? lobbyPlayers = null;

        using (var tx = await db.Database.BeginTransactionAsync())
        {
            var oldest = await db.QueueEntries
                .Where(q => q.SeasonId == seasonId && q.Status == QueueStatus.Queued)
                .OrderBy(q => q.EnqueuedAt)
                .Take(QueueCapacity)
                .ToListAsync();

            if (oldest.Count < QueueCapacity)
            {
                await tx.CommitAsync();
                return;
            }

            var userIds = oldest.Select(q => q.UserId).ToList();
            var mmrs = await db.SeasonPlayers
                .Where(sp => sp.SeasonId == seasonId && userIds.Contains(sp.UserId))
                .ToDictionaryAsync(sp => sp.UserId);

            var users = await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

            // Snake-draft pairing by MMR desc for balanced sides.
            var ordered = oldest
                .Select(q => new { Entry = q, Mmr = mmrs[q.UserId].Mmr, Rd = mmrs[q.UserId].Rd })
                .OrderByDescending(x => x.Mmr)
                .ToList();

            var teamOrder = new[] { Team.Radiant, Team.Dire, Team.Dire, Team.Radiant, Team.Radiant, Team.Dire, Team.Dire, Team.Radiant, Team.Radiant, Team.Dire };

            var match = new Match
            {
                Id = Guid.NewGuid(),
                SeasonId = seasonId,
                Status = MatchStatus.Draft,
                CreatedAt = DateTime.UtcNow
            };
            db.Matches.Add(match);

            var specs = new List<BotPlayerSpec>();
            for (int i = 0; i < QueueCapacity; i++)
            {
                var p = ordered[i];
                var team = teamOrder[i];
                db.MatchPlayers.Add(new MatchPlayer
                {
                    MatchId = match.Id,
                    UserId = p.Entry.UserId,
                    Team = team,
                    MmrBefore = p.Mmr,
                    RdBefore = p.Rd,
                    JoinedLobby = false,
                    Abandoned = false
                });
                p.Entry.Status = QueueStatus.Matched;
                p.Entry.MatchId = match.Id;
                specs.Add(new BotPlayerSpec(users[p.Entry.UserId].SteamId64, team.ToString()));
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            newMatchId = match.Id;
            lobbyPlayers = specs;
            logger.LogInformation("Match {MatchId} formed from 10 queue entries in season {SeasonId}", match.Id, seasonId);
        }

        // Fire-and-forget the bot call so the requesting user doesn't wait on Steam.
        if (newMatchId is Guid mid && lobbyPlayers is not null)
        {
            _ = Task.Run(() => CreateLobbyForMatchAsync(mid, lobbyPlayers));
        }
    }

    private async Task CreateLobbyForMatchAsync(Guid matchId, IReadOnlyList<BotPlayerSpec> players)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var bot = scope.ServiceProvider.GetRequiredService<BotWorkerClient>();
            var scopedDb = scope.ServiceProvider.GetRequiredService<Liga305DbContext>();
            var scopedLog = scope.ServiceProvider.GetRequiredService<ILogger<QueueController>>();

            var result = await bot.CreateLobbyAsync(matchId, players);
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

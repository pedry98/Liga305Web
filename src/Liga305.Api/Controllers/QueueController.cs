using Liga305.Api.Auth;
using Liga305.Api.Contracts;
using Liga305.Domain.Entities;
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
    ILogger<QueueController> logger) : ControllerBase
{
    private const int QueueCapacity = 10;
    private static readonly Random Rng = new();

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

    /// <summary>
    /// When 10 players are queued, form a Drafting match: pick two captains randomly
    /// from the top half by MMR (so the highest-rated players run the draft), and
    /// leave the other 8 unpicked. The captains then alternate picks via the Pick
    /// endpoint on MatchesController. The bot worker is NOT contacted yet — that
    /// happens once all 8 picks are in.
    /// </summary>
    private async Task TryFormMatchAsync(Guid seasonId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();

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

        // Top half by MMR is the captain pool. Pick 2 distinct captains from it.
        var byMmrDesc = oldest
            .OrderByDescending(q => mmrs[q.UserId].Mmr)
            .ToList();
        var captainPool = byMmrDesc.Take(QueueCapacity / 2).ToList(); // top 5
        var shuffled = captainPool.OrderBy(_ => Rng.Next()).ToList();
        var capRadiant = shuffled[0];
        var capDire    = shuffled[1];

        var match = new Match
        {
            Id = Guid.NewGuid(),
            SeasonId = seasonId,
            Status = MatchStatus.Drafting,
            CreatedAt = DateTime.UtcNow,
            RadiantCaptainUserId = capRadiant.UserId,
            DireCaptainUserId    = capDire.UserId
        };
        db.Matches.Add(match);

        foreach (var entry in oldest)
        {
            var sp = mmrs[entry.UserId];
            var isRadiantCap = entry.UserId == capRadiant.UserId;
            var isDireCap    = entry.UserId == capDire.UserId;
            db.MatchPlayers.Add(new MatchPlayer
            {
                MatchId = match.Id,
                UserId = entry.UserId,
                // Captains get their team + PickOrder=0 immediately. The other 8
                // are unpicked: PickOrder=null, Team holds a placeholder (Radiant)
                // that gets overwritten when their captain picks them.
                Team = isRadiantCap ? Team.Radiant : isDireCap ? Team.Dire : Team.Radiant,
                MmrBefore = sp.Mmr,
                RdBefore = sp.Rd,
                JoinedLobby = false,
                Abandoned = false,
                PickOrder = (isRadiantCap || isDireCap) ? 0 : (int?)null
            });
            entry.Status = QueueStatus.Matched;
            entry.MatchId = match.Id;
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation(
            "Match {MatchId} entered Drafting in season {SeasonId} (captains: Radiant={CapR} Dire={CapD})",
            match.Id, seasonId, capRadiant.UserId, capDire.UserId);
    }
}

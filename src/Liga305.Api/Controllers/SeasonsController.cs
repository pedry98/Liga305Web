using Liga305.Api.Contracts;
using Liga305.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Liga305.Api.Controllers;

[ApiController]
[Route("seasons")]
public class SeasonsController(Liga305DbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<SeasonDto>> GetAll()
    {
        return await db.Seasons
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.StartsAt)
            .Select(s => new SeasonDto(
                s.Id, s.Name, s.StartsAt, s.EndsAt, s.IsActive,
                s.Players.Count,
                s.Matches.Count(m => m.Status == Domain.Entities.MatchStatus.Completed)))
            .ToListAsync();
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var s = await db.Seasons.FirstOrDefaultAsync(x => x.IsActive);
        if (s is null) return NotFound();
        var playerCount = await db.SeasonPlayers.CountAsync(sp => sp.SeasonId == s.Id);
        var matchCount = await db.Matches.CountAsync(m => m.SeasonId == s.Id && m.Status == Domain.Entities.MatchStatus.Completed);
        return Ok(new SeasonDto(s.Id, s.Name, s.StartsAt, s.EndsAt, s.IsActive, playerCount, matchCount));
    }

    [HttpGet("active/leaderboard")]
    public async Task<IActionResult> GetActiveLeaderboard()
    {
        var s = await db.Seasons.FirstOrDefaultAsync(x => x.IsActive);
        if (s is null) return NotFound();
        return await LeaderboardFor(s.Id);
    }

    [HttpGet("{id:guid}/leaderboard")]
    public Task<IActionResult> GetLeaderboard(Guid id) => LeaderboardFor(id);

    private async Task<IActionResult> LeaderboardFor(Guid seasonId)
    {
        var rows = await db.SeasonPlayers
            .Where(sp => sp.SeasonId == seasonId)
            .OrderByDescending(sp => sp.Mmr)
            .Select(sp => new
            {
                sp.UserId,
                sp.Mmr,
                sp.Rd,
                sp.Wins,
                sp.Losses,
                sp.Abandons,
                sp.User.SteamId64,
                sp.User.DisplayName,
                sp.User.AvatarUrl
            })
            .ToListAsync();

        var result = rows
            .Select((r, i) => new LeaderboardEntryDto(
                i + 1, r.UserId, r.SteamId64, r.DisplayName, r.AvatarUrl,
                (int)Math.Round(r.Mmr), (int)Math.Round(r.Rd),
                r.Wins, r.Losses, r.Abandons))
            .ToList();

        return Ok(result);
    }
}

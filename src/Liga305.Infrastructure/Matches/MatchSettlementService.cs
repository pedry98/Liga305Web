using Liga305.Domain;
using Liga305.Domain.Entities;
using Liga305.Infrastructure.OpenDota;
using Liga305.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Liga305.Infrastructure.Matches;

/// <summary>
/// Applies a final result to a match: updates Match.Status, per-player MMR via
/// Glicko-2, SeasonPlayer aggregates (wins/losses/abandons), per-player KDA from
/// OpenDota, and writes MmrHistory rows. Idempotent: calling twice on a Completed
/// match is a no-op.
/// </summary>
public class MatchSettlementService(Liga305DbContext db, ILogger<MatchSettlementService> logger)
{
    private const long SteamIdBase = 76561197960265728L;
    private const double AbandonMmrPenalty = -50.0;

    /// <summary>Settle from real OpenDota data (preferred path).</summary>
    public Task<bool> SettleFromOpenDotaAsync(Guid matchId, OpenDotaMatch dotaMatch, CancellationToken ct = default) =>
        SettleAsync(matchId, dotaMatch.RadiantWin, dotaMatch.DurationSec, dotaMatch, ct);

    /// <summary>Settle without OpenDota data (admin test-settle path).</summary>
    public Task<bool> SettleAsync(Guid matchId, bool radiantWin, int? durationSec, CancellationToken ct = default) =>
        SettleAsync(matchId, radiantWin, durationSec, openDotaMatch: null, ct);

    private async Task<bool> SettleAsync(
        Guid matchId, bool radiantWin, int? durationSec, OpenDotaMatch? openDotaMatch, CancellationToken ct)
    {
        var match = await db.Matches
            .Include(m => m.Players).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) return false;
        if (match.Status == MatchStatus.Completed)
        {
            logger.LogInformation("Match {MatchId} already settled; skipping", matchId);
            return true;
        }

        var userIds = match.Players.Select(p => p.UserId).ToList();
        var seasonPlayers = await db.SeasonPlayers
            .Where(sp => sp.SeasonId == match.SeasonId && userIds.Contains(sp.UserId))
            .ToDictionaryAsync(sp => sp.UserId, ct);

        // Index OpenDota player rows by Dota account_id (32-bit) so we can match
        // them to our MatchPlayers via the player's SteamId64.
        var openDotaByAccountId = openDotaMatch?.Players
            .ToDictionary(p => p.AccountId, p => p) ?? new Dictionary<long, OpenDotaPlayer>();

        var preRatings = match.Players.ToDictionary(
            p => p.UserId,
            p => Glicko2.FromGlicko(seasonPlayers[p.UserId].Mmr, seasonPlayers[p.UserId].Rd, seasonPlayers[p.UserId].Volatility));

        var radiant = match.Players.Where(p => p.Team == Team.Radiant).ToList();
        var dire    = match.Players.Where(p => p.Team == Team.Dire).ToList();

        foreach (var player in match.Players)
        {
            var sp = seasonPlayers[player.UserId];
            var preMmr = sp.Mmr;
            var preRd  = sp.Rd;

            // Pull KDA + leaver status from OpenDota if we have it.
            var accountId = SteamIdToAccountId(player.User.SteamId64);
            openDotaByAccountId.TryGetValue(accountId, out var oPlayer);
            if (oPlayer is not null)
            {
                player.HeroId  = null; // OpenDota player_slot, not hero — keep null unless we extend the DTO
                player.Kills   = oPlayer.Kills;
                player.Deaths  = oPlayer.Deaths;
                player.Assists = oPlayer.Assists;
                player.Abandoned = oPlayer.Abandoned;
            }

            if (player.Abandoned)
            {
                // Skip Glicko: flat MMR penalty, increment abandon counter, no W/L.
                sp.Mmr = preMmr + AbandonMmrPenalty;
                sp.Abandons++;
                player.MmrBefore = preMmr;
                player.RdBefore  = preRd;
                player.MmrAfter  = sp.Mmr;
                player.RdAfter   = sp.Rd;

                db.MmrHistory.Add(new MmrHistory
                {
                    Id = Guid.NewGuid(),
                    SeasonId = match.SeasonId,
                    UserId = player.UserId,
                    MatchId = match.Id,
                    MmrBefore = preMmr,
                    MmrAfter = sp.Mmr,
                    Delta = AbandonMmrPenalty,
                    Won = false,
                    CreatedAt = DateTime.UtcNow
                });
                continue;
            }

            // Normal Glicko team-update: every player on the winning side beat every
            // player on the losing side (5×1.0 or 5×0.0).
            var mine = preRatings[player.UserId];
            var opponents = (player.Team == Team.Radiant ? dire : radiant)
                .Select(opp => new Glicko2.GameResult(preRatings[opp.UserId], WinScore(player.Team, radiantWin)))
                .ToList();
            var updated = Glicko2.Update(mine, opponents);
            var (newMmr, newRd, newVol) = Glicko2.ToGlicko(updated);

            sp.Mmr = newMmr;
            sp.Rd  = newRd;
            sp.Volatility = newVol;
            if ((player.Team == Team.Radiant) == radiantWin) sp.Wins++; else sp.Losses++;

            player.MmrBefore = preMmr;
            player.RdBefore  = preRd;
            player.MmrAfter  = newMmr;
            player.RdAfter   = newRd;

            db.MmrHistory.Add(new MmrHistory
            {
                Id = Guid.NewGuid(),
                SeasonId = match.SeasonId,
                UserId = player.UserId,
                MatchId = match.Id,
                MmrBefore = preMmr,
                MmrAfter = newMmr,
                Delta = newMmr - preMmr,
                Won = (player.Team == Team.Radiant) == radiantWin,
                CreatedAt = DateTime.UtcNow
            });
        }

        match.Status = MatchStatus.Completed;
        match.RadiantWin = radiantWin;
        match.EndedAt = DateTime.UtcNow;
        match.DurationSec = durationSec;

        await db.SaveChangesAsync(ct);

        var abandonCount = match.Players.Count(p => p.Abandoned);
        logger.LogInformation(
            "Settled match {MatchId} (radiantWin={Radiant}, duration={Duration}s, abandons={Abandons})",
            matchId, radiantWin, durationSec, abandonCount);
        return true;
    }

    private static long SteamIdToAccountId(string steamId64) =>
        long.TryParse(steamId64, out var n) ? n - SteamIdBase : 0;

    private static double WinScore(Team team, bool radiantWin) =>
        (team == Team.Radiant) == radiantWin ? 1.0 : 0.0;
}

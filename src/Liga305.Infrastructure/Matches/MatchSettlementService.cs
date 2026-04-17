using Liga305.Domain.Entities;
using Liga305.Infrastructure.OpenDota;
using Liga305.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Liga305.Infrastructure.Matches;

/// <summary>
/// Applies a final result to a match and updates per-player MMR using a simple
/// Elo formula scaled to give a ±25-point swing in evenly-matched games.
///
///   expected_self = 1 / (1 + 10^((avg_opponent_mmr - my_mmr) / 400))
///   delta         = K * (won ? 1 : 0  -  expected_self)
///   K = 50  →  even match: +25 win / -25 loss
///              big favorite (≈75% expected): +12 win / -38 loss
///
/// Idempotent: calling twice on a Completed match is a no-op.
/// </summary>
public class MatchSettlementService(Liga305DbContext db, ILogger<MatchSettlementService> logger)
{
    private const long SteamIdBase = 76561197960265728L;
    private const double KFactor = 50.0;
    private const double AbandonMmrPenalty = -50.0;

    public Task<bool> SettleFromOpenDotaAsync(Guid matchId, OpenDotaMatch dotaMatch, CancellationToken ct = default) =>
        SettleAsync(matchId, dotaMatch.RadiantWin, dotaMatch.DurationSec, dotaMatch, ct);

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

        var openDotaByAccountId = openDotaMatch?.Players
            .ToDictionary(p => p.AccountId, p => p) ?? new Dictionary<long, OpenDotaPlayer>();

        // Pre-match average MMR per side. Used as the "opponent strength" in Elo.
        var radiantPlayers = match.Players.Where(p => p.Team == Team.Radiant).ToList();
        var direPlayers    = match.Players.Where(p => p.Team == Team.Dire).ToList();
        var radiantAvg = radiantPlayers.Average(p => seasonPlayers[p.UserId].Mmr);
        var direAvg    = direPlayers.Average(p => seasonPlayers[p.UserId].Mmr);

        foreach (var player in match.Players)
        {
            var sp = seasonPlayers[player.UserId];
            var preMmr = sp.Mmr;

            var accountId = SteamIdToAccountId(player.User.SteamId64);
            openDotaByAccountId.TryGetValue(accountId, out var oPlayer);
            if (oPlayer is not null)
            {
                player.Kills   = oPlayer.Kills;
                player.Deaths  = oPlayer.Deaths;
                player.Assists = oPlayer.Assists;
                player.Abandoned = oPlayer.Abandoned;
            }

            if (player.Abandoned)
            {
                sp.Mmr = preMmr + AbandonMmrPenalty;
                sp.Abandons++;
                player.MmrBefore = preMmr;
                player.MmrAfter  = sp.Mmr;
                AddHistory(match.Id, match.SeasonId, player.UserId, preMmr, sp.Mmr, AbandonMmrPenalty, won: false);
                continue;
            }

            var opponentAvg = player.Team == Team.Radiant ? direAvg : radiantAvg;
            var expected = 1.0 / (1.0 + Math.Pow(10, (opponentAvg - preMmr) / 400.0));
            var won = (player.Team == Team.Radiant) == radiantWin;
            var delta = KFactor * ((won ? 1.0 : 0.0) - expected);
            var newMmr = preMmr + delta;

            sp.Mmr = newMmr;
            if (won) sp.Wins++; else sp.Losses++;

            player.MmrBefore = preMmr;
            player.MmrAfter  = newMmr;

            AddHistory(match.Id, match.SeasonId, player.UserId, preMmr, newMmr, delta, won);
        }

        match.Status = MatchStatus.Completed;
        match.RadiantWin = radiantWin;
        match.EndedAt = DateTime.UtcNow;
        match.DurationSec = durationSec;

        await db.SaveChangesAsync(ct);

        var abandonCount = match.Players.Count(p => p.Abandoned);
        logger.LogInformation(
            "Settled match {MatchId} radiantWin={Radiant} duration={Duration}s abandons={Abandons} (radiantAvg={RAvg:F0} direAvg={DAvg:F0})",
            matchId, radiantWin, durationSec, abandonCount, radiantAvg, direAvg);
        return true;
    }

    private void AddHistory(Guid matchId, Guid seasonId, Guid userId, double pre, double post, double delta, bool won) =>
        db.MmrHistory.Add(new MmrHistory
        {
            Id = Guid.NewGuid(),
            SeasonId = seasonId,
            UserId = userId,
            MatchId = matchId,
            MmrBefore = pre,
            MmrAfter = post,
            Delta = delta,
            Won = won,
            CreatedAt = DateTime.UtcNow
        });

    private static long SteamIdToAccountId(string steamId64) =>
        long.TryParse(steamId64, out var n) ? n - SteamIdBase : 0;
}

using Liga305.Domain.Entities;
using Liga305.Infrastructure.OpenDota;
using Liga305.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Liga305.Infrastructure.Matches;

/// <summary>
/// Every PollInterval, finds every Match in Live state with a DotaMatchId,
/// queries OpenDota, and settles it when OpenDota has radiant_win + duration.
/// </summary>
public class MatchResultPoller(IServiceScopeFactory scopeFactory, ILogger<MatchResultPoller> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MatchResultPoller started (interval {Interval}s)", PollInterval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Poller tick failed"); }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Liga305DbContext>();
        var openDota = scope.ServiceProvider.GetRequiredService<OpenDotaClient>();
        var settlement = scope.ServiceProvider.GetRequiredService<MatchSettlementService>();

        var pending = await db.Matches
            .Where(m => m.Status == MatchStatus.Live && m.DotaMatchId != null)
            .Select(m => new { m.Id, m.DotaMatchId })
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        foreach (var p in pending)
        {
            if (p.DotaMatchId is null) continue;
            var match = await openDota.GetMatchAsync(p.DotaMatchId.Value, ct);
            if (match is null)
            {
                logger.LogDebug("OpenDota doesn't have match {DotaMatchId} yet", p.DotaMatchId);
                continue;
            }
            await settlement.SettleFromOpenDotaAsync(p.Id, match, ct);
        }
    }
}

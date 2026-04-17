using Liga305.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Liga305.Infrastructure.Persistence;

public class DatabaseSeeder(Liga305DbContext db)
{
    /// <summary>
    /// If no admin exists, promote the oldest user. Runs on every startup;
    /// becomes a no-op once an admin is set. Covers the case where users
    /// registered before the "first user = admin" rule was added to auth.
    /// </summary>
    public async Task EnsureBootstrapAdminAsync(CancellationToken ct = default)
    {
        var hasAdmin = await db.Users.AnyAsync(u => u.IsAdmin, ct);
        if (hasAdmin) return;

        var first = await db.Users.OrderBy(u => u.CreatedAt).FirstOrDefaultAsync(ct);
        if (first is null) return;

        first.IsAdmin = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task EnsureActiveSeasonAsync(CancellationToken ct = default)
    {
        var activeSeason = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive, ct);
        if (activeSeason is null)
        {
            var now = DateTime.UtcNow;
            activeSeason = new Season
            {
                Id = Guid.NewGuid(),
                Name = "Pre-Season Test Cup",
                StartsAt = now,
                EndsAt = now.AddDays(90),
                IsActive = true,
                CreatedAt = now
            };
            db.Seasons.Add(activeSeason);
            await db.SaveChangesAsync(ct);
        }

        // Backfill enrollment for any users registered before the active season existed.
        var enrolledUserIds = await db.SeasonPlayers
            .Where(sp => sp.SeasonId == activeSeason.Id)
            .Select(sp => sp.UserId)
            .ToListAsync(ct);

        var missing = await db.Users
            .Where(u => !enrolledUserIds.Contains(u.Id))
            .ToListAsync(ct);

        foreach (var user in missing)
        {
            db.SeasonPlayers.Add(new SeasonPlayer
            {
                SeasonId = activeSeason.Id,
                UserId = user.Id,
                Mmr = activeSeason.StartingMmr,
                Rd = activeSeason.StartingRd,
                Volatility = activeSeason.StartingVolatility,
                JoinedAt = DateTime.UtcNow
            });
        }
        if (missing.Count > 0) await db.SaveChangesAsync(ct);
    }
}

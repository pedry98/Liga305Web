using Liga305.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Liga305.Infrastructure.Persistence;

public class Liga305DbContext(DbContextOptions<Liga305DbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<SeasonPlayer> SeasonPlayers => Set<SeasonPlayer>();
    public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();
    public DbSet<MmrHistory> MmrHistory => Set<MmrHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.HasIndex(u => u.SteamId64).IsUnique();
            b.Property(u => u.SteamId64).HasMaxLength(20);
            b.Property(u => u.DisplayName).HasMaxLength(64);
            b.Property(u => u.AvatarUrl).HasMaxLength(512);
        });

        modelBuilder.Entity<Season>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).HasMaxLength(128);
            b.HasIndex(s => s.IsActive);
        });

        modelBuilder.Entity<SeasonPlayer>(b =>
        {
            b.HasKey(sp => new { sp.SeasonId, sp.UserId });
            b.HasOne(sp => sp.Season).WithMany(s => s.Players).HasForeignKey(sp => sp.SeasonId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(sp => sp.User).WithMany().HasForeignKey(sp => sp.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(sp => new { sp.SeasonId, sp.Mmr });
        });

        modelBuilder.Entity<QueueEntry>(b =>
        {
            b.HasKey(q => q.Id);
            b.Property(q => q.Status).HasConversion<string>().HasMaxLength(16);
            b.HasOne(q => q.Season).WithMany(s => s.QueueEntries).HasForeignKey(q => q.SeasonId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(q => q.User).WithMany().HasForeignKey(q => q.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(q => q.Match).WithMany().HasForeignKey(q => q.MatchId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(q => new { q.SeasonId, q.Status, q.EnqueuedAt });
            // One active queue entry per user per season
            b.HasIndex(q => new { q.SeasonId, q.UserId, q.Status }).HasFilter("\"Status\" = 'Queued'").IsUnique();
        });

        modelBuilder.Entity<Match>(b =>
        {
            b.HasKey(m => m.Id);
            b.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(m => m.LobbyName).HasMaxLength(64);
            b.Property(m => m.LobbyPassword).HasMaxLength(64);
            b.Property(m => m.BotSteamName).HasMaxLength(64);
            b.HasOne(m => m.Season).WithMany(s => s.Matches).HasForeignKey(m => m.SeasonId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(m => new { m.SeasonId, m.Status });
            b.HasIndex(m => m.CreatedAt);
            b.HasIndex(m => m.DotaMatchId);
        });

        modelBuilder.Entity<MatchPlayer>(b =>
        {
            b.HasKey(mp => new { mp.MatchId, mp.UserId });
            b.Property(mp => mp.Team).HasConversion<string>().HasMaxLength(10);
            b.HasOne(mp => mp.Match).WithMany(m => m.Players).HasForeignKey(mp => mp.MatchId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(mp => mp.User).WithMany().HasForeignKey(mp => mp.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(mp => mp.UserId);
        });

        modelBuilder.Entity<MmrHistory>(b =>
        {
            b.HasKey(h => h.Id);
            b.HasIndex(h => new { h.UserId, h.SeasonId, h.CreatedAt });
            b.HasOne<Season>().WithMany().HasForeignKey(h => h.SeasonId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<User>().WithMany().HasForeignKey(h => h.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Match>().WithMany().HasForeignKey(h => h.MatchId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

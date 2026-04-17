namespace Liga305.Domain.Entities;

public class Season
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsActive { get; set; }
    public double StartingMmr { get; set; } = 1000;
    public double StartingRd { get; set; } = 350;
    public double StartingVolatility { get; set; } = 0.06;
    public DateTime CreatedAt { get; set; }

    public ICollection<SeasonPlayer> Players { get; set; } = [];
    public ICollection<Match> Matches { get; set; } = [];
    public ICollection<QueueEntry> QueueEntries { get; set; } = [];
}

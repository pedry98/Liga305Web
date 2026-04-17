namespace Liga305.Domain.Entities;

public enum QueueStatus
{
    Queued = 0,
    Matched = 1,
    Cancelled = 2
}

public class QueueEntry
{
    public Guid Id { get; set; }
    public Guid SeasonId { get; set; }
    public Guid UserId { get; set; }
    public DateTime EnqueuedAt { get; set; }
    public QueueStatus Status { get; set; }
    public Guid? MatchId { get; set; }

    public Season Season { get; set; } = null!;
    public User User { get; set; } = null!;
    public Match? Match { get; set; }
}

namespace Liga305.Domain.Entities;

public class MmrHistory
{
    public Guid Id { get; set; }
    public Guid SeasonId { get; set; }
    public Guid UserId { get; set; }
    public Guid MatchId { get; set; }
    public double MmrBefore { get; set; }
    public double MmrAfter { get; set; }
    public double Delta { get; set; }
    public bool Won { get; set; }
    public DateTime CreatedAt { get; set; }
}

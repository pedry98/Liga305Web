namespace Liga305.Domain.Entities;

public class SeasonPlayer
{
    public Guid SeasonId { get; set; }
    public Guid UserId { get; set; }

    public double Mmr { get; set; }
    public double Rd { get; set; }
    public double Volatility { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Abandons { get; set; }
    public DateTime JoinedAt { get; set; }

    public Season Season { get; set; } = null!;
    public User User { get; set; } = null!;
}

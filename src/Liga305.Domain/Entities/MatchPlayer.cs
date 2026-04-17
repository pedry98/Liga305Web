namespace Liga305.Domain.Entities;

public enum Team
{
    Radiant = 0,
    Dire = 1
}

public class MatchPlayer
{
    public Guid MatchId { get; set; }
    public Guid UserId { get; set; }
    public Team Team { get; set; }

    public double MmrBefore { get; set; }
    public double RdBefore { get; set; }
    public double? MmrAfter { get; set; }
    public double? RdAfter { get; set; }
    public bool JoinedLobby { get; set; }
    public bool Abandoned { get; set; }

    // Populated at settlement time from the OpenDota match data.
    public int? HeroId { get; set; }
    public int? Kills { get; set; }
    public int? Deaths { get; set; }
    public int? Assists { get; set; }

    public Match Match { get; set; } = null!;
    public User User { get; set; } = null!;
}

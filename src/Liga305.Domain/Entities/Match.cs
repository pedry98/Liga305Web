namespace Liga305.Domain.Entities;

public enum MatchStatus
{
    Draft = 0,
    Lobby = 1,
    Live = 2,
    Completed = 3,
    Abandoned = 4,
    /// <summary>10 players matched; captains are alternating picks.</summary>
    Drafting = 5
}

public class Match
{
    public Guid Id { get; set; }
    public Guid SeasonId { get; set; }

    public long? DotaMatchId { get; set; }
    public string? LobbyName { get; set; }
    public string? LobbyPassword { get; set; }
    public string? BotSteamName { get; set; }

    public MatchStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? DurationSec { get; set; }
    public bool? RadiantWin { get; set; }

    // Captain-pick: populated when the queue pops. The two captains take turns
    // picking from the remaining 8 players.
    public Guid? RadiantCaptainUserId { get; set; }
    public Guid? DireCaptainUserId { get; set; }

    public Season Season { get; set; } = null!;
    public ICollection<MatchPlayer> Players { get; set; } = [];
}

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
    public int? LastHits { get; set; }
    public int? Denies { get; set; }
    public int? GoldPerMin { get; set; }
    public int? XpPerMin { get; set; }
    public int? NetWorth { get; set; }
    public int? HeroDamage { get; set; }
    public int? TowerDamage { get; set; }
    public int? HeroHealing { get; set; }

    // Inventory slots 0..5, backpack 0..2, plus neutral. Dota item IDs (nullable
    // = empty slot). Resolved to names/icons by the SPA using OpenDota's
    // /constants/items lookup.
    public int? Item0 { get; set; }
    public int? Item1 { get; set; }
    public int? Item2 { get; set; }
    public int? Item3 { get; set; }
    public int? Item4 { get; set; }
    public int? Item5 { get; set; }
    public int? Backpack0 { get; set; }
    public int? Backpack1 { get; set; }
    public int? Backpack2 { get; set; }
    public int? ItemNeutral { get; set; }

    // Per-minute net-worth array from OpenDota (`gold_t`) — index = minute, value
    // = total net worth at that minute. JSON-encoded int[]. Null when the match
    // isn't parsed yet. Drives the interactive per-player net worth graph.
    public string? GoldTJson { get; set; }

    // Captain-pick draft order: 0 = captain, 1 = first pick, 2 = second pick, ...
    // Null while drafting is still in progress for this slot.
    public int? PickOrder { get; set; }

    public Match Match { get; set; } = null!;
    public User User { get; set; } = null!;
}

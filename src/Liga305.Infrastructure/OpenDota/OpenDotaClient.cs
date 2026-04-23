using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Liga305.Infrastructure.OpenDota;

public record OpenDotaMatch(
    long MatchId,
    bool RadiantWin,
    int DurationSec,
    DateTime? StartedAt,
    IReadOnlyList<OpenDotaPlayer> Players,
    bool Parsed,
    IReadOnlyList<int>? RadiantGoldAdv,
    IReadOnlyList<int>? RadiantXpAdv);

public record OpenDotaPlayer(
    long AccountId,
    int PlayerSlot,   // 0–4 Radiant, 128–132 Dire
    bool IsRadiant,
    bool Abandoned,
    int Kills,
    int Deaths,
    int Assists,
    int? HeroId,
    int? LastHits,
    int? Denies,
    int? GoldPerMin,
    int? XpPerMin,
    int? NetWorth,
    int? HeroDamage,
    int? TowerDamage,
    int? HeroHealing,
    int? Item0,
    int? Item1,
    int? Item2,
    int? Item3,
    int? Item4,
    int? Item5,
    int? Backpack0,
    int? Backpack1,
    int? Backpack2,
    int? ItemNeutral);

public class OpenDotaClient(HttpClient http, ILogger<OpenDotaClient> logger)
{
    public async Task<OpenDotaMatch?> GetMatchAsync(long matchId, CancellationToken ct = default)
    {
        try
        {
            var resp = await http.GetAsync($"matches/{matchId}", ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenDota /matches/{MatchId} returned {Status}", matchId, resp.StatusCode);
                return null;
            }
            var raw = await resp.Content.ReadFromJsonAsync<RawMatch>(cancellationToken: ct);
            if (raw is null) return null;

            // A match is "complete enough" when OpenDota has basic fields populated.
            // `version` is set after the match is parsed; duration + radiant_win are
            // set as soon as the match ends.
            if (raw.Duration is null || raw.RadiantWin is null) return null;

            // net_worth isn't always returned at top level — fall back to summing
            // gold + gold_spent when the dedicated field is missing.
            static int? NetWorthOf(RawPlayer p) =>
                p.NetWorth ?? ((p.Gold is not null || p.GoldSpent is not null) ? (p.Gold ?? 0) + (p.GoldSpent ?? 0) : null);

            return new OpenDotaMatch(
                MatchId: matchId,
                RadiantWin: raw.RadiantWin.Value,
                DurationSec: raw.Duration.Value,
                StartedAt: raw.StartTime is null ? null : DateTimeOffset.FromUnixTimeSeconds(raw.StartTime.Value).UtcDateTime,
                Parsed: raw.Version is not null,
                RadiantGoldAdv: raw.RadiantGoldAdv,
                RadiantXpAdv: raw.RadiantXpAdv,
                Players: (raw.Players ?? []).Select(p => new OpenDotaPlayer(
                    AccountId: p.AccountId ?? 0,
                    PlayerSlot: p.PlayerSlot ?? 0,
                    IsRadiant: (p.PlayerSlot ?? 0) < 128,
                    Abandoned: p.LeaverStatus is 2 or 3 or 4 or 5 or 6,
                    Kills: p.Kills ?? 0,
                    Deaths: p.Deaths ?? 0,
                    Assists: p.Assists ?? 0,
                    HeroId: p.HeroId,
                    LastHits: p.LastHits,
                    Denies: p.Denies,
                    GoldPerMin: p.GoldPerMin,
                    XpPerMin: p.XpPerMin,
                    NetWorth: NetWorthOf(p),
                    HeroDamage: p.HeroDamage,
                    TowerDamage: p.TowerDamage,
                    HeroHealing: p.HeroHealing,
                    Item0: p.Item0,
                    Item1: p.Item1,
                    Item2: p.Item2,
                    Item3: p.Item3,
                    Item4: p.Item4,
                    Item5: p.Item5,
                    Backpack0: p.Backpack0,
                    Backpack1: p.Backpack1,
                    Backpack2: p.Backpack2,
                    ItemNeutral: p.ItemNeutral)).ToList());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenDota /matches/{MatchId} failed", matchId);
            return null;
        }
    }

    private sealed record RawMatch(
        [property: JsonPropertyName("match_id")] long? MatchId,
        [property: JsonPropertyName("radiant_win")] bool? RadiantWin,
        [property: JsonPropertyName("duration")] int? Duration,
        [property: JsonPropertyName("start_time")] long? StartTime,
        [property: JsonPropertyName("version")] int? Version,
        [property: JsonPropertyName("radiant_gold_adv")] List<int>? RadiantGoldAdv,
        [property: JsonPropertyName("radiant_xp_adv")] List<int>? RadiantXpAdv,
        [property: JsonPropertyName("players")] List<RawPlayer>? Players);

    private sealed record RawPlayer(
        [property: JsonPropertyName("account_id")] long? AccountId,
        [property: JsonPropertyName("player_slot")] int? PlayerSlot,
        [property: JsonPropertyName("leaver_status")] int? LeaverStatus,
        [property: JsonPropertyName("kills")] int? Kills,
        [property: JsonPropertyName("deaths")] int? Deaths,
        [property: JsonPropertyName("assists")] int? Assists,
        [property: JsonPropertyName("hero_id")] int? HeroId,
        [property: JsonPropertyName("last_hits")] int? LastHits,
        [property: JsonPropertyName("denies")] int? Denies,
        [property: JsonPropertyName("gold_per_min")] int? GoldPerMin,
        [property: JsonPropertyName("xp_per_min")] int? XpPerMin,
        [property: JsonPropertyName("net_worth")] int? NetWorth,
        [property: JsonPropertyName("gold")] int? Gold,
        [property: JsonPropertyName("gold_spent")] int? GoldSpent,
        [property: JsonPropertyName("hero_damage")] int? HeroDamage,
        [property: JsonPropertyName("tower_damage")] int? TowerDamage,
        [property: JsonPropertyName("hero_healing")] int? HeroHealing,
        [property: JsonPropertyName("item_0")] int? Item0,
        [property: JsonPropertyName("item_1")] int? Item1,
        [property: JsonPropertyName("item_2")] int? Item2,
        [property: JsonPropertyName("item_3")] int? Item3,
        [property: JsonPropertyName("item_4")] int? Item4,
        [property: JsonPropertyName("item_5")] int? Item5,
        [property: JsonPropertyName("backpack_0")] int? Backpack0,
        [property: JsonPropertyName("backpack_1")] int? Backpack1,
        [property: JsonPropertyName("backpack_2")] int? Backpack2,
        [property: JsonPropertyName("item_neutral")] int? ItemNeutral);
}

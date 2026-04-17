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
    bool Parsed);

public record OpenDotaPlayer(
    long AccountId,
    int PlayerSlot,   // 0–4 Radiant, 128–132 Dire
    bool IsRadiant,
    bool Abandoned,
    int Kills,
    int Deaths,
    int Assists);

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

            return new OpenDotaMatch(
                MatchId: matchId,
                RadiantWin: raw.RadiantWin.Value,
                DurationSec: raw.Duration.Value,
                StartedAt: raw.StartTime is null ? null : DateTimeOffset.FromUnixTimeSeconds(raw.StartTime.Value).UtcDateTime,
                Players: (raw.Players ?? []).Select(p => new OpenDotaPlayer(
                    AccountId: p.AccountId ?? 0,
                    PlayerSlot: p.PlayerSlot ?? 0,
                    IsRadiant: (p.PlayerSlot ?? 0) < 128,
                    Abandoned: p.LeaverStatus is 2 or 3 or 4 or 5 or 6,
                    Kills: p.Kills ?? 0,
                    Deaths: p.Deaths ?? 0,
                    Assists: p.Assists ?? 0)).ToList(),
                Parsed: raw.Version is not null);
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
        [property: JsonPropertyName("players")] List<RawPlayer>? Players);

    private sealed record RawPlayer(
        [property: JsonPropertyName("account_id")] long? AccountId,
        [property: JsonPropertyName("player_slot")] int? PlayerSlot,
        [property: JsonPropertyName("leaver_status")] int? LeaverStatus,
        [property: JsonPropertyName("kills")] int? Kills,
        [property: JsonPropertyName("deaths")] int? Deaths,
        [property: JsonPropertyName("assists")] int? Assists);
}

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Liga305.Infrastructure.Steam;

public class SteamWebApiOptions
{
    public string? ApiKey { get; set; }
}

public record SteamPlayerSummary(string SteamId64, string PersonaName, string? AvatarUrl);

public class SteamWebApiClient(
    HttpClient http,
    IOptions<SteamWebApiOptions> options,
    ILogger<SteamWebApiClient> logger)
{
    private readonly string? _apiKey = options.Value.ApiKey;

    public async Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId64, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            logger.LogDebug("Steam Web API key not configured; skipping profile enrichment for {SteamId}", steamId64);
            return null;
        }

        var url = $"ISteamUser/GetPlayerSummaries/v0002/?key={_apiKey}&steamids={steamId64}";

        try
        {
            var response = await http.GetFromJsonAsync<SteamSummariesEnvelope>(url, ct);
            var player = response?.Response?.Players?.FirstOrDefault();
            if (player is null) return null;

            return new SteamPlayerSummary(
                SteamId64: player.SteamId,
                PersonaName: player.PersonaName,
                AvatarUrl: player.AvatarFull);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Steam profile fetch failed for {SteamId}", steamId64);
            return null;
        }
    }

    private sealed record SteamSummariesEnvelope([property: JsonPropertyName("response")] SteamSummariesResponse? Response);
    private sealed record SteamSummariesResponse([property: JsonPropertyName("players")] List<SteamPlayer>? Players);
    private sealed record SteamPlayer(
        [property: JsonPropertyName("steamid")] string SteamId,
        [property: JsonPropertyName("personaname")] string PersonaName,
        [property: JsonPropertyName("avatarfull")] string? AvatarFull);
}

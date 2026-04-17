using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Liga305.Infrastructure.BotWorker;

public class BotWorkerOptions
{
    public string BaseUrl { get; set; } = "http://localhost:4100";
    public string SharedSecret { get; set; } = "dev-only-not-secure";
}

public record BotPlayerSpec(string SteamId64, string Team);
public record BotLobbyResult(string LobbyName, string Password, string BotSteamName, bool Simulated);

public class BotWorkerClient(HttpClient http, ILogger<BotWorkerClient> logger)
{
    public async Task<bool> LaunchMatchAsync(Guid matchId, CancellationToken ct = default)
    {
        try
        {
            var resp = await http.PostAsync($"/lobbies/{matchId}/launch", content: null, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bot worker launch failed for match {MatchId}", matchId);
            return false;
        }
    }

    public async Task<bool> CancelLobbyAsync(Guid matchId, CancellationToken ct = default)
    {
        try
        {
            var resp = await http.PostAsync($"/lobbies/{matchId}/cancel", content: null, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return true; // already gone
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bot worker cancel failed for match {MatchId}", matchId);
            return false;
        }
    }

    public async Task<int?> ResendInvitesAsync(Guid matchId, CancellationToken ct = default)
    {
        try
        {
            var resp = await http.PostAsync($"/lobbies/{matchId}/invite", content: null, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, int>>(cancellationToken: ct);
            return body is not null && body.TryGetValue("invited", out var n) ? n : 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bot worker resend-invite failed for match {MatchId}", matchId);
            return null;
        }
    }

    public async Task<BotLobbyResult?> CreateLobbyAsync(
        Guid matchId,
        IEnumerable<BotPlayerSpec> players,
        CancellationToken ct = default)
    {
        try
        {
            var req = new
            {
                matchId = matchId.ToString(),
                players = players.Select(p => new { steamId64 = p.SteamId64, team = p.Team })
            };
            var resp = await http.PostAsJsonAsync("/lobbies", req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                logger.LogError("Bot worker /lobbies returned {Status}: {Body}", resp.StatusCode, body);
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<BotLobbyResult>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bot worker call failed for match {MatchId}", matchId);
            return null;
        }
    }
}

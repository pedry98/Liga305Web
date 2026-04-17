using System.Security.Claims;
using AspNet.Security.OpenId.Steam;
using Liga305.Domain.Entities;
using Liga305.Infrastructure.Persistence;
using Liga305.Infrastructure.Steam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Liga305.Api.Auth;

[ApiController]
[Route("auth")]
public class SteamAuthController(
    Liga305DbContext db,
    SteamWebApiClient steam,
    ILogger<SteamAuthController> logger) : ControllerBase
{
    [HttpGet("steam/login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl = "/")
    {
        var safeReturn = string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "/" : returnUrl;
        var props = new AuthenticationProperties { RedirectUri = $"/auth/steam/post-login?returnUrl={Uri.EscapeDataString(safeReturn)}" };
        return Challenge(props, SteamAuthenticationDefaults.AuthenticationScheme);
    }

    [HttpGet("steam/post-login")]
    [Authorize]
    public async Task<IActionResult> PostLogin([FromQuery] string? returnUrl, [FromServices] IConfiguration config)
    {
        var steamId = ExtractSteamId64(User);
        if (steamId is null)
        {
            logger.LogWarning("Steam login completed but no SteamID claim found");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect(SafeFrontendUrl(config, "/?error=no-steamid"));
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.SteamId64 == steamId);
        var summary = await steam.GetPlayerSummaryAsync(steamId);

        if (user is null)
        {
            // First user on a fresh instance gets admin automatically
            var isFirstUser = !await db.Users.AnyAsync();

            user = new User
            {
                Id = Guid.NewGuid(),
                SteamId64 = steamId,
                DisplayName = summary?.PersonaName ?? steamId,
                AvatarUrl = summary?.AvatarUrl,
                CreatedAt = DateTime.UtcNow,
                IsAdmin = isFirstUser
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            if (isFirstUser)
            {
                logger.LogInformation("First user {SteamId} granted admin", steamId);
            }
        }
        else if (summary is not null && !user.HasCustomProfile)
        {
            user.DisplayName = summary.PersonaName;
            user.AvatarUrl = summary.AvatarUrl;
            await db.SaveChangesAsync();
        }

        await EnsureSeasonEnrollmentAsync(user);

        return Redirect(SafeFrontendUrl(config, returnUrl ?? "/"));
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var steamId = ExtractSteamId64(User);
        if (steamId is null) return Unauthorized();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.SteamId64 == steamId);
        if (user is null) return Unauthorized();

        return Ok(new
        {
            id = user.Id,
            steamId64 = user.SteamId64,
            displayName = user.DisplayName,
            avatarUrl = ResolveAvatarUrl(user.AvatarUrl),
            isAdmin = user.IsAdmin,
            hasCustomProfile = user.HasCustomProfile
        });
    }

    private string? ResolveAvatarUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("http://") || url.StartsWith("https://")) return url;
        var req = HttpContext.Request;
        if (!url.StartsWith('/')) url = "/" + url;
        return $"{req.Scheme}://{req.Host}{url}";
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private async Task EnsureSeasonEnrollmentAsync(User user)
    {
        var activeSeasons = await db.Seasons.Where(s => s.IsActive).ToListAsync();
        foreach (var season in activeSeasons)
        {
            var alreadyEnrolled = await db.SeasonPlayers
                .AnyAsync(sp => sp.SeasonId == season.Id && sp.UserId == user.Id);
            if (alreadyEnrolled) continue;

            db.SeasonPlayers.Add(new SeasonPlayer
            {
                SeasonId = season.Id,
                UserId = user.Id,
                Mmr = season.StartingMmr,
                Rd = season.StartingRd,
                Volatility = season.StartingVolatility,
                JoinedAt = DateTime.UtcNow
            });
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
    }

    private static string? ExtractSteamId64(ClaimsPrincipal user)
    {
        var nameId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(nameId)) return null;
        var slash = nameId.LastIndexOf('/');
        var id = slash >= 0 ? nameId[(slash + 1)..] : nameId;
        return id.Length == 17 && id.All(char.IsDigit) ? id : null;
    }

    private static string SafeFrontendUrl(IConfiguration config, string path)
    {
        var baseUrl = config["Frontend:BaseUrl"]?.TrimEnd('/') ?? "";
        if (string.IsNullOrEmpty(baseUrl)) return path;
        if (!path.StartsWith('/')) path = "/" + path;
        return baseUrl + path;
    }
}

using System.Text.RegularExpressions;
using Liga305.Api.Auth;
using Liga305.Api.Contracts;
using Liga305.Domain.Entities;
using Liga305.Infrastructure.Persistence;
using Liga305.Infrastructure.Steam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Liga305.Api.Controllers;

[ApiController]
[Route("users")]
public partial class UsersController(
    Liga305DbContext db,
    CurrentUserAccessor currentUser,
    SteamWebApiClient steam,
    IWebHostEnvironment env,
    ILogger<UsersController> logger) : ControllerBase
{
    private const long MaxAvatarBytes = 2 * 1024 * 1024; // 2 MB
    private static readonly string[] AllowedImageContentTypes =
        ["image/png", "image/jpeg", "image/gif", "image/webp"];

    [GeneratedRegex(@"^[\p{L}\p{N} _\-\.]+$")]
    private static partial Regex DisplayNamePattern();

    [HttpGet("me/matches")]
    [Authorize]
    public async Task<IActionResult> GetMyMatches([FromQuery] int limit = 20)
    {
        var me = await currentUser.GetAsync();
        if (me is null) return Unauthorized();

        limit = Math.Clamp(limit, 1, 100);
        var rows = await db.MatchPlayers
            .Where(mp => mp.UserId == me.Id)
            .OrderByDescending(mp => mp.Match.CreatedAt)
            .Take(limit)
            .Select(mp => new
            {
                mp.Match.Id,
                mp.Match.SeasonId,
                SeasonName = mp.Match.Season.Name,
                mp.Match.DotaMatchId,
                Status = mp.Match.Status.ToString(),
                mp.Match.CreatedAt,
                mp.Match.StartedAt,
                mp.Match.EndedAt,
                mp.Match.DurationSec,
                mp.Match.RadiantWin,
                RadiantAvg = mp.Match.Players.Where(p => p.Team == Team.Radiant).Average(p => (double?)p.MmrBefore) ?? 0,
                DireAvg = mp.Match.Players.Where(p => p.Team == Team.Dire).Average(p => (double?)p.MmrBefore) ?? 0
            })
            .ToListAsync();

        return Ok(rows.Select(r => new MatchSummaryDto(
            r.Id, r.SeasonId, r.SeasonName, r.DotaMatchId, r.Status,
            r.CreatedAt, r.StartedAt, r.EndedAt, r.DurationSec, r.RadiantWin,
            (int)Math.Round(r.RadiantAvg), (int)Math.Round(r.DireAvg))));
    }

    [HttpGet("me/mmr-history")]
    [Authorize]
    public async Task<IActionResult> GetMyMmrHistory()
    {
        var me = await currentUser.GetAsync();
        if (me is null) return Unauthorized();

        var history = await db.MmrHistory
            .Where(h => h.UserId == me.Id)
            .OrderBy(h => h.CreatedAt)
            .Select(h => new MmrHistoryPointDto(
                h.MatchId,
                h.CreatedAt,
                (int)Math.Round(h.MmrBefore),
                (int)Math.Round(h.MmrAfter),
                (int)Math.Round(h.Delta),
                null,
                h.Won))
            .ToListAsync();

        return Ok(history);
    }

    [HttpPatch("me/profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
    {
        var me = await currentUser.GetAsync();
        if (me is null) return Unauthorized();

        if (req.DisplayName is not null)
        {
            var trimmed = req.DisplayName.Trim();
            if (trimmed.Length < 2 || trimmed.Length > 32)
                return BadRequest(new { error = "display_name_length", message = "Display name must be 2–32 characters." });
            if (!DisplayNamePattern().IsMatch(trimmed))
                return BadRequest(new { error = "display_name_chars", message = "Only letters, digits, spaces, and . _ - are allowed." });
            me.DisplayName = trimmed;
        }

        if (req.AvatarUrl is not null)
        {
            var url = req.AvatarUrl.Trim();
            if (url.Length == 0)
            {
                me.AvatarUrl = null;
            }
            else
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                    (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                {
                    return BadRequest(new { error = "avatar_url_invalid", message = "Avatar URL must be an absolute http(s) URL." });
                }
                if (url.Length > 512)
                    return BadRequest(new { error = "avatar_url_too_long" });
                me.AvatarUrl = url;
            }
        }

        me.HasCustomProfile = true;
        await db.SaveChangesAsync();

        return Ok(ToProfileResponse(me));
    }

    [HttpPost("me/avatar")]
    [Authorize]
    [RequestSizeLimit(MaxAvatarBytes + 4 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAvatarBytes + 4 * 1024)]
    public async Task<IActionResult> UploadAvatar(IFormFile file)
    {
        var me = await currentUser.GetAsync();
        if (me is null) return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "no_file" });
        if (file.Length > MaxAvatarBytes)
            return BadRequest(new { error = "too_large", message = "Max 2 MB." });
        if (!AllowedImageContentTypes.Contains(file.ContentType))
            return BadRequest(new { error = "bad_type", message = "Allowed: PNG, JPEG, GIF, WEBP." });

        var ext = file.ContentType switch
        {
            "image/png"  => ".png",
            "image/jpeg" => ".jpg",
            "image/gif"  => ".gif",
            "image/webp" => ".webp",
            _ => ".bin"
        };

        var dir = Path.Combine(env.WebRootPath, "avatars");
        Directory.CreateDirectory(dir);

        // Delete any previous avatar file for this user (different extension may exist).
        foreach (var existing in Directory.EnumerateFiles(dir, me.Id + ".*"))
        {
            try { System.IO.File.Delete(existing); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to delete prior avatar {Path}", existing); }
        }

        var filename = me.Id + ext;
        var path = Path.Combine(dir, filename);
        await using (var fs = System.IO.File.Create(path))
        {
            await file.CopyToAsync(fs);
        }

        // Cache-bust with a version query so browsers pick up the new image.
        var version = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var relativeUrl = $"/avatars/{filename}?v={version}";
        me.AvatarUrl = AbsoluteUrl(relativeUrl);
        me.HasCustomProfile = true;
        await db.SaveChangesAsync();

        return Ok(ToProfileResponse(me));
    }

    [HttpPost("me/profile/reset")]
    [Authorize]
    public async Task<IActionResult> ResetProfile()
    {
        var me = await currentUser.GetAsync();
        if (me is null) return Unauthorized();

        var summary = await steam.GetPlayerSummaryAsync(me.SteamId64);
        if (summary is not null)
        {
            me.DisplayName = summary.PersonaName;
            me.AvatarUrl = summary.AvatarUrl;
        }
        else
        {
            me.DisplayName = me.SteamId64;
            me.AvatarUrl = null;
        }

        // Also delete any uploaded avatar file
        var dir = Path.Combine(env.WebRootPath, "avatars");
        if (Directory.Exists(dir))
        {
            foreach (var existing in Directory.EnumerateFiles(dir, me.Id + ".*"))
            {
                try { System.IO.File.Delete(existing); } catch { /* ignore */ }
            }
        }

        me.HasCustomProfile = false;
        await db.SaveChangesAsync();

        return Ok(ToProfileResponse(me));
    }

    private ProfileResponse ToProfileResponse(User user) => new(
        user.Id, user.SteamId64, user.DisplayName,
        ResolveAvatarUrl(user.AvatarUrl),
        user.IsAdmin, user.HasCustomProfile);

    private string? ResolveAvatarUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("http://") || url.StartsWith("https://")) return url;
        return AbsoluteUrl(url);
    }

    private string AbsoluteUrl(string relative)
    {
        var req = HttpContext.Request;
        if (!relative.StartsWith('/')) relative = "/" + relative;
        return $"{req.Scheme}://{req.Host}{relative}";
    }
}

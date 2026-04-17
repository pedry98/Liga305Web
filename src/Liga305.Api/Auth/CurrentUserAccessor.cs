using System.Security.Claims;
using Liga305.Domain.Entities;
using Liga305.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Liga305.Api.Auth;

public class CurrentUserAccessor(IHttpContextAccessor http, Liga305DbContext db)
{
    public string? SteamId64
    {
        get
        {
            var nameId = http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(nameId)) return null;
            var slash = nameId.LastIndexOf('/');
            var id = slash >= 0 ? nameId[(slash + 1)..] : nameId;
            return id.Length == 17 && id.All(char.IsDigit) ? id : null;
        }
    }

    public async Task<User?> GetAsync(CancellationToken ct = default)
    {
        var steamId = SteamId64;
        if (steamId is null) return null;
        return await db.Users.FirstOrDefaultAsync(u => u.SteamId64 == steamId, ct);
    }
}

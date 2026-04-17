namespace Liga305.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string SteamId64 { get; set; }
    public required string DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsBanned { get; set; }

    // When true, DisplayName/AvatarUrl are user-customized and won't be
    // overwritten on next Steam sign-in. Cleared via POST /users/me/profile/reset.
    public bool HasCustomProfile { get; set; }
}

namespace Liga305.Api.Contracts;

public record SeasonDto(
    Guid Id,
    string Name,
    DateTime StartsAt,
    DateTime EndsAt,
    bool IsActive,
    int PlayerCount,
    int MatchCount);

public record LeaderboardEntryDto(
    int Rank,
    Guid UserId,
    string SteamId64,
    string DisplayName,
    string? AvatarUrl,
    int Mmr,
    int Rd,
    int Wins,
    int Losses,
    int Abandons);

public record QueueEntryDto(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    int Mmr,
    DateTime EnqueuedAt);

public record QueueStateDto(
    Guid SeasonId,
    int Size,
    int Capacity,
    bool SelfInQueue,
    Guid? LastMatchId,
    IReadOnlyList<QueueEntryDto> Entries,
    Guid? SelfActiveMatchId = null);

public record MatchSummaryDto(
    Guid Id,
    Guid SeasonId,
    string SeasonName,
    long? DotaMatchId,
    string Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? EndedAt,
    int? DurationSec,
    bool? RadiantWin,
    int RadiantAvgMmr,
    int DireAvgMmr);

public record MatchPlayerDto(
    Guid UserId,
    string SteamId64,
    string DisplayName,
    string? AvatarUrl,
    string Team,
    int MmrBefore,
    int? MmrAfter,
    bool JoinedLobby,
    bool Abandoned,
    int? Kills,
    int? Deaths,
    int? Assists,
    int? PickOrder,
    bool IsCaptain,
    bool IsPicked,
    int? HeroId,
    int? LastHits,
    int? Denies,
    int? GoldPerMin,
    int? XpPerMin,
    int? NetWorth,
    int? HeroDamage,
    int? TowerDamage,
    int? HeroHealing,
    IReadOnlyList<int?> Items,      // length 6 (inventory slots)
    IReadOnlyList<int?> Backpack,   // length 3
    int? ItemNeutral,
    IReadOnlyList<int>? GoldT);     // per-minute net worth — null if match not parsed

public record MatchDetailDto(
    Guid Id,
    Guid SeasonId,
    string SeasonName,
    long? DotaMatchId,
    string Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? EndedAt,
    int? DurationSec,
    bool? RadiantWin,
    int RadiantAvgMmr,
    int DireAvgMmr,
    string? LobbyName,
    string? LobbyPassword,
    string? BotSteamName,
    DateTime? AbandonsAt,
    Guid? RadiantCaptainUserId,
    Guid? DireCaptainUserId,
    Guid? CurrentPickerUserId,    // captain whose turn it is, or null if drafting is done
    string? CurrentPickerTeam,    // "Radiant" / "Dire" / null
    IReadOnlyList<int>? RadiantGoldAdv,  // per-minute gold advantage (Radiant lead = positive)
    IReadOnlyList<int>? RadiantXpAdv,
    IReadOnlyList<MatchPlayerDto> Players);

public record PickPlayerRequest(Guid UserId);

public record MmrHistoryPointDto(
    Guid MatchId,
    DateTime At,
    int MmrBefore,
    int MmrAfter,
    int Delta,
    bool? RadiantWin,
    bool Won);

public record UpdateProfileRequest(string? DisplayName, string? AvatarUrl);

public record ProfileResponse(
    Guid Id,
    string SteamId64,
    string DisplayName,
    string? AvatarUrl,
    bool IsAdmin,
    bool HasCustomProfile);

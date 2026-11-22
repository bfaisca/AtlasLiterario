using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Profiles;

/// <summary>Card of a public profile. Never carries e-mail or internal ids (the handle is the public identifier).</summary>
public sealed record ProfileSummaryDto(
    string Handle,
    string Name,
    int FollowersCount,
    int ReadBooksCount,
    bool IsFollowing,
    bool FollowsYou);

/// <summary>Header of a profile page. <c>IsFollowing</c>/<c>FollowsYou</c> are false when <c>IsSelf</c>.</summary>
public sealed record ProfileDto(
    string Handle,
    string Name,
    int FollowersCount,
    int ReadBooksCount,
    bool IsFollowing,
    bool FollowsYou,
    int FollowingCount,
    int ExploredCountriesAuthors,
    int ExploredCountriesOrigins,
    DateTimeOffset MemberSince,
    bool IsSelf);

/// <summary>Read book of a profile owner; deliberately has no library item id.</summary>
public sealed record ProfileBookDto(
    Guid BookId,
    string Title,
    string? CoverUrl,
    IReadOnlyList<string> Authors,
    DateOnly? FinishedDate,
    int? Rating,
    string? Review);

public sealed record ProfileCountryAuthorDto(
    Guid AuthorId, string Name, string CountryIso, string CountryName, IReadOnlyList<ProfileBookDto> Books);

public sealed record ProfileCountryDetailDto(
    MapType MapType,
    string Iso,
    string Name,
    Continent Continent,
    int BookCount,
    int AuthorCount,
    IReadOnlyList<ProfileBookDto> Books,
    IReadOnlyList<ProfileCountryAuthorDto> Authors,
    string Status);

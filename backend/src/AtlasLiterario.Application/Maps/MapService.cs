using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Maps;

public interface IMapService
{
    Task<MapDataDto> GetMapAsync(MapType mapType, string? status, CancellationToken ct);

    Task<CountryDetailDto> GetCountryDetailAsync(MapType mapType, string iso, string? status, CancellationToken ct);

    /// <summary>Throws <c>share.private_profile</c> (403) when the current user's maps are Private.</summary>
    Task EnsureSharingAllowedAsync(CancellationToken ct);
}

/// <summary>
/// Loads the authenticated user's books of the chosen status (default Read) and their geographic relations with flat projections
/// (no entity tracking, no descriptions/reviews, no N+1) and delegates the counting to <see cref="MapAggregator"/>.
/// The owner-parameterized building blocks live in <see cref="UserMapReader"/> (shared with the public profile maps).
/// </summary>
public sealed class MapService(IAppDbContext db, ICurrentUser currentUser) : IMapService
{
    public Task<MapDataDto> GetMapAsync(MapType mapType, string? status, CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        return UserMapReader.GetMapAsync(db, userId, mapType, MapStatusFilterParser.Parse(status), ct);
    }

    public Task<CountryDetailDto> GetCountryDetailAsync(MapType mapType, string iso, string? status, CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        return UserMapReader.GetCountryDetailAsync(db, userId, mapType, iso, MapStatusFilterParser.Parse(status), ct);
    }

    public async Task EnsureSharingAllowedAsync(CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        var visibility = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (MapVisibility?)u.MapVisibility)
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("auth.user_not_found", "Usuário não encontrado.");
        if (visibility == MapVisibility.Private)
        {
            throw new ForbiddenException(
                "share.private_profile", "Altere a visibilidade do perfil para compartilhar seus mapas.");
        }
    }
}

/// <summary>
/// Map and country-panel queries for one owner (<paramref name="userId"/> is always passed explicitly, never taken from the
/// request), so the private maps and the public profile maps count with exactly the same rules.
/// </summary>
internal static class UserMapReader
{
    public static async Task<MapDataDto> GetMapAsync(
        IAppDbContext db, Guid userId, MapType mapType, MapStatusFilter filter, CancellationToken ct)
    {
        var input = await MapInputLoader.LoadAsync(
            db, userId, filter, includeAuthors: mapType == MapType.Authors, includeOrigins: mapType == MapType.Origins,
            includeAuthorNames: mapType == MapType.Authors, ct);
        return MapAggregator.Build(mapType, input, filter);
    }

    public static async Task<CountryDetailDto> GetCountryDetailAsync(
        IAppDbContext db, Guid userId, MapType mapType, string iso, MapStatusFilter filter, CancellationToken ct)
    {
        var code = (iso ?? string.Empty).Trim().ToUpperInvariant();
        var country = await db.Countries.AsNoTracking()
            .Where(c => c.IsoCode == code || c.GeoJsonId == code)
            .Select(c => new MapCountry(c.GeoJsonId, c.NamePt, c.Continent))
            .FirstOrDefaultAsync(ct)
            ?? throw new ValidationException(
                "country.unknown_iso",
                "Código de país desconhecido.",
                new Dictionary<string, string[]> { ["iso"] = [$"ISO 3166-1 alpha-3 desconhecido: {iso}"] });

        var input = await MapInputLoader.LoadAsync(
            db, userId, filter, includeAuthors: mapType == MapType.Authors, includeOrigins: mapType == MapType.Origins,
            includeAuthorNames: mapType == MapType.Authors, ct);
        var bookIds = MapAggregator.GetBookIdsForCountry(mapType, input, country.Iso, filter);
        var datum = MapAggregator.Build(mapType, input, filter).Countries.FirstOrDefault(c => c.Iso == country.Iso);

        var books = bookIds.Count == 0
            ? []
            : CountryBookQuery.Order(await CountryBookQuery.ToDtosAsync(
                db.UserBooks.AsNoTracking().Where(ub => ub.UserId == userId && bookIds.Contains(ub.BookId)), ct));

        var authors = MapAggregator.GetAuthorsForCountry(mapType, input, country, books, filter);

        return new CountryDetailDto(
            mapType, country.Iso, country.Name, country.Continent,
            datum?.BookCount ?? 0, datum?.AuthorCount ?? 0, books, filter.ToString(), authors);
    }
}

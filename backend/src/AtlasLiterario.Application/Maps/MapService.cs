using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Maps;

public interface IMapService
{
    Task<MapDataDto> GetMapAsync(MapType mapType, CancellationToken ct);

    Task<CountryDetailDto> GetCountryDetailAsync(MapType mapType, string iso, CancellationToken ct);
}

/// <summary>
/// Loads the authenticated user's Read books and their geographic relations with flat projections
/// (no entity tracking, no descriptions/reviews, no N+1) and delegates the counting to <see cref="MapAggregator"/>.
/// </summary>
public sealed class MapService(IAppDbContext db, ICurrentUser currentUser) : IMapService
{
    public async Task<MapDataDto> GetMapAsync(MapType mapType, CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        var input = await LoadAsync(mapType, userId, ct);
        return MapAggregator.Build(mapType, input);
    }

    public async Task<CountryDetailDto> GetCountryDetailAsync(MapType mapType, string iso, CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        var code = (iso ?? string.Empty).Trim().ToUpperInvariant();
        var country = await db.Countries.AsNoTracking()
            .Where(c => c.IsoCode == code || c.GeoJsonId == code)
            .Select(c => new MapCountry(c.GeoJsonId, c.NamePt, c.Continent))
            .FirstOrDefaultAsync(ct)
            ?? throw new ValidationException(
                "country.unknown_iso",
                "Código de país desconhecido.",
                new Dictionary<string, string[]> { ["iso"] = [$"ISO 3166-1 alpha-3 desconhecido: {iso}"] });

        var input = await LoadAsync(mapType, userId, ct);
        var bookIds = MapAggregator.GetBookIdsForCountry(mapType, input, country.Iso);
        var datum = MapAggregator.Build(mapType, input).Countries.FirstOrDefault(c => c.Iso == country.Iso);

        var rows = bookIds.Count == 0
            ? []
            : await db.UserBooks.AsNoTracking()
                .Where(ub => ub.UserId == userId && ub.Status == ReadingStatus.Read && bookIds.Contains(ub.BookId))
                .Select(ub => new
                {
                    ub.Id,
                    ub.BookId,
                    ub.Book!.Title,
                    ub.Book.CoverUrl,
                    Authors = ub.Book.Authors.OrderBy(ba => ba.Position).Select(ba => ba.Author!.Name).ToList(),
                    ub.FinishedDate,
                    ub.Rating,
                })
                .ToListAsync(ct);

        var books = rows
            .OrderBy(r => r.FinishedDate is null)
            .ThenByDescending(r => r.FinishedDate)
            .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new CountryBookDto(r.Id, r.BookId, r.Title, r.CoverUrl, r.Authors, r.FinishedDate, r.Rating))
            .ToList();

        return new CountryDetailDto(
            mapType, country.Iso, country.Name, country.Continent,
            datum?.BookCount ?? 0, datum?.AuthorCount ?? 0, books);
    }

    private async Task<MapAggregationInput> LoadAsync(MapType mapType, Guid userId, CancellationToken ct)
    {
        var read = db.UserBooks.AsNoTracking().Where(ub => ub.UserId == userId && ub.Status == ReadingStatus.Read);

        var libraryBooks = await read
            .Select(ub => new LibraryBookRow(ub.BookId, ub.Status))
            .ToListAsync(ct);

        var bookAuthors = await db.BookAuthors.AsNoTracking()
            .Where(ba => read.Any(ub => ub.BookId == ba.BookId))
            .Select(ba => new BookAuthorRow(ba.BookId, ba.AuthorId))
            .ToListAsync(ct);

        IReadOnlyCollection<AuthorCitizenshipRow> citizenships = [];
        IReadOnlyCollection<BookOriginRow> origins = [];

        if (mapType == MapType.Authors)
        {
            citizenships = await db.AuthorCountries.AsNoTracking()
                .Where(ac => ac.RelationshipType == AuthorCountryRelationship.Citizenship
                    && read.Any(ub => ub.Book!.Authors.Any(ba => ba.AuthorId == ac.AuthorId)))
                .Select(ac => new AuthorCitizenshipRow(
                    ac.AuthorId, new MapCountry(ac.Country!.GeoJsonId, ac.Country.NamePt, ac.Country.Continent)))
                .ToListAsync(ct);
        }
        else
        {
            origins = await db.BookOriginCountries.AsNoTracking()
                .Where(bo => read.Any(ub => ub.BookId == bo.BookId))
                .Select(bo => new BookOriginRow(
                    bo.BookId, new MapCountry(bo.Country!.GeoJsonId, bo.Country.NamePt, bo.Country.Continent)))
                .ToListAsync(ct);
        }

        return new MapAggregationInput(libraryBooks, bookAuthors, citizenships, origins);
    }
}

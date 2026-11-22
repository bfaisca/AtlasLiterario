using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Maps;

/// <summary>
/// Loads the flat rows consumed by <see cref="MapAggregator"/> for one user (no entity tracking, no N+1).
/// Shared by <see cref="MapService"/> and the statistics service so both count from the same data.
/// </summary>
internal static class MapInputLoader
{
    public static async Task<MapAggregationInput> LoadAsync(
        IAppDbContext db,
        Guid userId,
        MapStatusFilter filter,
        bool includeAuthors,
        bool includeOrigins,
        bool includeAuthorNames,
        CancellationToken ct)
    {
        var read = db.UserBooks.AsNoTracking().Where(ub => ub.UserId == userId);
        if (filter != MapStatusFilter.All)
        {
            var status = filter.ToReadingStatus();
            read = read.Where(ub => ub.Status == status);
        }

        var libraryBooks = await read
            .Select(ub => new LibraryBookRow(ub.BookId, ub.Status, ub.FinishedDate))
            .ToListAsync(ct);

        var bookAuthors = await db.BookAuthors.AsNoTracking()
            .Where(ba => read.Any(ub => ub.BookId == ba.BookId))
            .Select(ba => new BookAuthorRow(ba.BookId, ba.AuthorId))
            .ToListAsync(ct);

        IReadOnlyCollection<AuthorCitizenshipRow> citizenships = [];
        IReadOnlyCollection<BookOriginRow> origins = [];
        IReadOnlyCollection<AuthorNameRow> authorNames = [];

        if (includeAuthors)
        {
            citizenships = await db.AuthorCountries.AsNoTracking()
                .Where(ac => ac.RelationshipType == AuthorCountryRelationship.Citizenship
                    && read.Any(ub => ub.Book!.Authors.Any(ba => ba.AuthorId == ac.AuthorId)))
                .Select(ac => new AuthorCitizenshipRow(
                    ac.AuthorId, new MapCountry(ac.Country!.GeoJsonId, ac.Country.NamePt, ac.Country.Continent)))
                .ToListAsync(ct);

            if (includeAuthorNames)
            {
                var authorIds = citizenships.Select(c => c.AuthorId).Distinct().ToList();
                authorNames = authorIds.Count == 0
                    ? []
                    : await db.Authors.AsNoTracking()
                        .Where(a => authorIds.Contains(a.Id))
                        .Select(a => new AuthorNameRow(a.Id, a.Name, a.NormalizedName))
                        .ToListAsync(ct);
            }
        }

        if (includeOrigins)
        {
            origins = await db.BookOriginCountries.AsNoTracking()
                .Where(bo => read.Any(ub => ub.BookId == bo.BookId))
                .Select(bo => new BookOriginRow(
                    bo.BookId, new MapCountry(bo.Country!.GeoJsonId, bo.Country.NamePt, bo.Country.Continent)))
                .ToListAsync(ct);
        }

        return new MapAggregationInput(libraryBooks, bookAuthors, citizenships, origins, authorNames);
    }
}

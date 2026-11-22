using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Books;

/// <summary>Loading and mapping of <see cref="BookDetailsDto"/> (entities never leave the Application layer).</summary>
internal static class BookDetailsMapping
{
    public static IQueryable<Book> IncludeDetails(this IQueryable<Book> books) =>
        books
            .Include(b => b.Authors).ThenInclude(ba => ba.Author!).ThenInclude(a => a.Countries).ThenInclude(c => c.Country)
            .Include(b => b.OriginCountries).ThenInclude(o => o.Country)
            .Include(b => b.ExternalIdentifiers);

    public static IQueryable<UserBook> IncludeBookDetails(this IQueryable<UserBook> userBooks) =>
        userBooks
            .Include(ub => ub.Book!).ThenInclude(b => b.Authors).ThenInclude(ba => ba.Author!).ThenInclude(a => a.Countries).ThenInclude(c => c.Country)
            .Include(ub => ub.Book!).ThenInclude(b => b.OriginCountries).ThenInclude(o => o.Country)
            .Include(ub => ub.Book!).ThenInclude(b => b.ExternalIdentifiers);

    public static BookDetailsDto ToDetailsDto(this Book book) =>
        new(
            book.Id,
            book.Title,
            book.Subtitle,
            book.Isbn13,
            book.Isbn10,
            book.Publisher,
            book.PublicationYear,
            book.Description,
            book.CoverUrl,
            book.OriginalLanguage,
            book.Genres.ToList(),
            book.Authors
                .OrderBy(ba => ba.Position)
                .Where(ba => ba.Author is not null)
                .Select(ba => ba.Author!.ToSummaryDto())
                .ToList(),
            book.OriginCountries
                .Where(o => o.Country is not null)
                .OrderByDescending(o => o.Confidence).ThenBy(o => o.Country!.IsoCode, StringComparer.Ordinal)
                .Select(o => new GeoRelationDto(o.Country!.IsoCode, o.Country.NamePt, o.Source, o.Confidence, o.IsVerified))
                .ToList(),
            book.GeoEnrichmentStatus,
            book.ExternalIdentifiers
                .OrderBy(i => i.Provider).ThenBy(i => i.ExternalId, StringComparer.Ordinal)
                .Select(i => new ExternalSourceRef(ToExternalProvider(i.Provider), i.ExternalId))
                .ToList());

    public static AuthorSummaryDto ToSummaryDto(this Author author) =>
        new(
            author.Id,
            author.Name,
            author.Countries
                .Where(c => c.RelationshipType == AuthorCountryRelationship.Citizenship && c.Country is not null)
                .OrderByDescending(c => c.Confidence).ThenBy(c => c.Country!.IsoCode, StringComparer.Ordinal)
                .Select(c => new GeoRelationDto(c.Country!.IsoCode, c.Country.NamePt, c.Source, c.Confidence, c.IsVerified))
                .ToList());

    public static ExternalProvider ToExternalProvider(BookIdentifierProvider provider) => provider switch
    {
        BookIdentifierProvider.GoogleBooks => ExternalProvider.GoogleBooks,
        BookIdentifierProvider.OpenLibraryWork or BookIdentifierProvider.OpenLibraryEdition => ExternalProvider.OpenLibrary,
        BookIdentifierProvider.Wikidata => ExternalProvider.Wikidata,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    /// <summary>Maps an API source reference to the persisted identifier provider (Open Library by id suffix W/M).</summary>
    public static BookIdentifierProvider? ToIdentifierProvider(ExternalSourceRef source) => source.Provider switch
    {
        ExternalProvider.GoogleBooks => BookIdentifierProvider.GoogleBooks,
        ExternalProvider.OpenLibrary when source.ExternalId.EndsWith('W') => BookIdentifierProvider.OpenLibraryWork,
        ExternalProvider.OpenLibrary when source.ExternalId.EndsWith('M') => BookIdentifierProvider.OpenLibraryEdition,
        ExternalProvider.Wikidata => BookIdentifierProvider.Wikidata,
        _ => null,
    };
}

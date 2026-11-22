using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Authors;

public interface IAuthorService
{
    Task<AuthorDetailsDto> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Catalog books of the author that are in the current user's library.</summary>
    Task<IReadOnlyList<BookDetailsDto>> GetBooksInLibraryAsync(Guid id, CancellationToken ct);

    /// <summary>Replaces the Citizenship relations with manual, verified ones (other relationship types are kept).</summary>
    Task<AuthorDetailsDto> UpdateCountriesAsync(Guid id, UpdateAuthorCountriesRequest request, CancellationToken ct);
}

public sealed class AuthorService(IAppDbContext db, ICurrentUser currentUser, IClock clock) : IAuthorService
{
    public async Task<AuthorDetailsDto> GetAsync(Guid id, CancellationToken ct)
    {
        currentUser.GetRequiredUserId();
        var author = await db.Authors.AsNoTracking()
            .Include(a => a.Countries).ThenInclude(c => c.Country)
            .Include(a => a.ExternalIdentifiers)
            .SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw NotFound();
        return ToDto(author);
    }

    public async Task<IReadOnlyList<BookDetailsDto>> GetBooksInLibraryAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        if (!await db.Authors.AnyAsync(a => a.Id == id, ct))
        {
            throw NotFound();
        }

        var books = await db.Books.AsNoTracking()
            .Where(b => b.Authors.Any(ba => ba.AuthorId == id)
                        && db.UserBooks.Any(ub => ub.UserId == userId && ub.BookId == b.Id))
            .OrderBy(b => b.Title)
            .IncludeDetails()
            .ToListAsync(ct);
        return books.Select(b => b.ToDetailsDto()).ToList();
    }

    public async Task<AuthorDetailsDto> UpdateCountriesAsync(Guid id, UpdateAuthorCountriesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.GetRequiredUserId();
        var author = await db.Authors.Include(a => a.Countries).SingleOrDefaultAsync(a => a.Id == id, ct)
                     ?? throw NotFound();
        var countries = await CountryResolution.ResolveIsosAsync(db, request.CitizenshipIsos, "citizenshipIsos", ct);
        if (await GeoEditPolicy.CheckCitizenshipsAsync(db, userId, id, countries, ct) == GeoEditDecision.NoOp)
        {
            return await GetAsync(id, ct);
        }

        var now = clock.UtcNow;
        foreach (var relation in author.Countries.Where(c => c.RelationshipType == AuthorCountryRelationship.Citizenship).ToList())
        {
            author.Countries.Remove(relation);
            db.AuthorCountries.Remove(relation);
        }

        foreach (var country in countries)
        {
            author.Countries.Add(AuthorCountry.CreateManual(author.Id, country.Id, AuthorCountryRelationship.Citizenship, userId, now));
        }

        author.Touch(now);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static AuthorDetailsDto ToDto(Author author) =>
        new(
            author.Id,
            author.Name,
            author.BirthDate,
            author.Countries
                .Where(c => c.Country is not null)
                .OrderBy(c => c.RelationshipType).ThenByDescending(c => c.Confidence).ThenBy(c => c.Country!.IsoCode, StringComparer.Ordinal)
                .Select(c => new AuthorCountryDto(
                    c.Country!.IsoCode, c.Country.NamePt, c.Source, c.Confidence, c.IsVerified, c.RelationshipType))
                .ToList(),
            author.ExternalIdentifiers
                .Where(i => i.Provider != AuthorIdentifierProvider.Viaf) // VIAF is stored but not exposed in the MVP
                .OrderBy(i => i.Provider)
                .Select(i => new ExternalSourceRef(
                    i.Provider == AuthorIdentifierProvider.Wikidata ? ExternalProvider.Wikidata : ExternalProvider.OpenLibrary,
                    i.ExternalId))
                .ToList());

    private static NotFoundException NotFound() => new("author.not_found", "Autor não encontrado.");
}

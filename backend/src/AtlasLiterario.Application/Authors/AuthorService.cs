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

    /// <summary>Autocomplete: accent/case-insensitive prefix of the name or of any word (prefix of the whole name first).</summary>
    Task<IReadOnlyList<AuthorSuggestionDto>> SearchAsync(string? q, int? limit, CancellationToken ct);

    /// <summary>Catalog books of the author that are in the current user's library.</summary>
    Task<IReadOnlyList<BookDetailsDto>> GetBooksInLibraryAsync(Guid id, CancellationToken ct);

    /// <summary>Replaces the Citizenship relations with manual, verified ones (other relationship types are kept).</summary>
    Task<AuthorDetailsDto> UpdateCountriesAsync(Guid id, UpdateAuthorCountriesRequest request, CancellationToken ct);
}

public sealed class AuthorService(IAppDbContext db, ICurrentUser currentUser, IClock clock) : IAuthorService
{
    public const int MinQueryLength = 2;
    public const int DefaultLimit = 10;
    public const int MaxLimit = 20;

    public async Task<IReadOnlyList<AuthorSuggestionDto>> SearchAsync(string? q, int? limit, CancellationToken ct)
    {
        currentUser.GetRequiredUserId();
        var errors = new ValidationErrors();
        var term = (q ?? string.Empty).Trim();
        if (term.Length < MinQueryLength)
        {
            errors.Add("q", $"Informe ao menos {MinQueryLength} caracteres.");
        }

        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
        {
            errors.Add("limit", $"O limite deve estar entre 1 e {MaxLimit}.");
        }

        errors.ThrowIfAny();

        var normalized = Author.NormalizeName(term);
        if (normalized.Length == 0)
        {
            return [];
        }

        var escaped = EscapeLike(normalized);
        var prefix = escaped + "%";
        var wordPrefix = "% " + escaped + "%";

        var authors = await db.Authors.AsNoTracking()
            .Where(a => EF.Functions.Like(a.NormalizedName, prefix, "\\")
                        || EF.Functions.Like(a.NormalizedName, wordPrefix, "\\"))
            .OrderBy(a => EF.Functions.Like(a.NormalizedName, prefix, "\\") ? 0 : 1)
            .ThenBy(a => a.NormalizedName)
            .ThenBy(a => a.Id)
            .Take(take)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);
        if (authors.Count == 0)
        {
            return [];
        }

        var ids = authors.Select(a => a.Id).ToList();
        var nationalities = await db.AuthorCountries.AsNoTracking()
            .Where(ac => ac.RelationshipType == AuthorCountryRelationship.Citizenship && ids.Contains(ac.AuthorId))
            .Select(ac => new { ac.AuthorId, ac.Country!.IsoCode, ac.Country.NamePt })
            .ToListAsync(ct);
        var byAuthor = nationalities
            .GroupBy(n => n.AuthorId)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.IsoCode, StringComparer.Ordinal).ToList());

        return authors
            .Select(a =>
            {
                var countries = byAuthor.GetValueOrDefault(a.Id) ?? [];
                return new AuthorSuggestionDto(
                    a.Id, a.Name, countries.Select(c => c.IsoCode).ToList(), countries.Select(c => c.NamePt).ToList());
            })
            .ToList();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

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

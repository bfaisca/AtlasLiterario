using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Maps;

/// <summary>Country as shown on the maps (Portuguese name).</summary>
public sealed record MapCountry(string Iso, string Name, Continent Continent);

/// <summary>One library entry of the user (any status; the aggregator keeps only Read).</summary>
public sealed record LibraryBookRow(Guid BookId, ReadingStatus Status, DateOnly? FinishedDate = null);

/// <summary>Book → author link.</summary>
public sealed record BookAuthorRow(Guid BookId, Guid AuthorId);

/// <summary>Author → citizenship country (only Citizenship relations belong here).</summary>
public sealed record AuthorCitizenshipRow(Guid AuthorId, MapCountry Country);

/// <summary>Book → literary country of origin.</summary>
public sealed record BookOriginRow(Guid BookId, MapCountry Country);

/// <summary>Author display name and its normalized (lower-case, accent-free) form used for ordering.</summary>
public sealed record AuthorNameRow(Guid AuthorId, string Name, string NormalizedName);

/// <summary>Flat rows loaded from the database for one user. Rows of other books are ignored.</summary>
public sealed record MapAggregationInput(
    IReadOnlyCollection<LibraryBookRow> LibraryBooks,
    IReadOnlyCollection<BookAuthorRow> BookAuthors,
    IReadOnlyCollection<AuthorCitizenshipRow> AuthorCitizenships,
    IReadOnlyCollection<BookOriginRow> BookOrigins,
    IReadOnlyCollection<AuthorNameRow>? AuthorNames = null);

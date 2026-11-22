using AtlasLiterario.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Abstractions;

/// <summary>Persistence boundary used by application services (implemented by Infrastructure).</summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<UserFollow> UserFollows { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<PasswordResetToken> PasswordResetTokens { get; }

    DbSet<Book> Books { get; }

    DbSet<Author> Authors { get; }

    DbSet<BookAuthor> BookAuthors { get; }

    DbSet<Country> Countries { get; }

    DbSet<HistoricalCountryMapping> HistoricalCountryMappings { get; }

    DbSet<AuthorCountry> AuthorCountries { get; }

    DbSet<BookOriginCountry> BookOriginCountries { get; }

    DbSet<UserBook> UserBooks { get; }

    DbSet<BookCover> BookCovers { get; }

    DbSet<ExternalBookIdentifier> ExternalBookIdentifiers { get; }

    DbSet<ExternalAuthorIdentifier> ExternalAuthorIdentifiers { get; }

    DbSet<MapShare> MapShares { get; }

    DbSet<ExternalLookupCache> ExternalLookupCache { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Detaches every tracked entity (used to recover after a failed SaveChanges, e.g. a unique-index race).</summary>
    void DiscardChanges();
}

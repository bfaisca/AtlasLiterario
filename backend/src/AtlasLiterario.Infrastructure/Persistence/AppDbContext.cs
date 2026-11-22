using System.Text;
using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<UserFollow> UserFollows => Set<UserFollow>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<Book> Books => Set<Book>();

    public DbSet<BookCover> BookCovers => Set<BookCover>();

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<BookAuthor> BookAuthors => Set<BookAuthor>();

    public DbSet<Country> Countries => Set<Country>();

    public DbSet<HistoricalCountryMapping> HistoricalCountryMappings => Set<HistoricalCountryMapping>();

    public DbSet<AuthorCountry> AuthorCountries => Set<AuthorCountry>();

    public DbSet<BookOriginCountry> BookOriginCountries => Set<BookOriginCountry>();

    public DbSet<UserBook> UserBooks => Set<UserBook>();

    public DbSet<ExternalBookIdentifier> ExternalBookIdentifiers => Set<ExternalBookIdentifier>();

    public DbSet<ExternalAuthorIdentifier> ExternalAuthorIdentifiers => Set<ExternalAuthorIdentifier>();

    public DbSet<MapShare> MapShares => Set<MapShare>();

    public DbSet<ExternalLookupCache> ExternalLookupCache => Set<ExternalLookupCache>();

    public void DiscardChanges() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplySnakeCaseNames(modelBuilder);
    }

    /// <summary>
    /// Column, key, foreign key and index names in snake_case (table names are set explicitly in each configuration).
    /// Done here to avoid an extra naming-convention package.
    /// </summary>
    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        var entityTypes = modelBuilder.Model.GetEntityTypes().ToList();

        foreach (var entityType in entityTypes)
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }

        foreach (var entityType in entityTypes)
        {
            foreach (var key in entityType.GetKeys())
            {
                key.SetName(key.GetName()?.ToLowerInvariant());
            }

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.SetConstraintName(foreignKey.GetConstraintName()?.ToLowerInvariant());
            }

            foreach (var index in entityType.GetIndexes())
            {
                index.SetDatabaseName(index.GetDatabaseName()?.ToLowerInvariant());
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var previousIsLowerOrDigit = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var startsNewWordInAcronym = i > 0 && char.IsUpper(name[i - 1])
                                             && i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (previousIsLowerOrDigit || startsNewWordInAcronym)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}

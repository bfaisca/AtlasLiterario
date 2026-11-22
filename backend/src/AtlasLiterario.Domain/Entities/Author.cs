using AtlasLiterario.Domain.Common;
using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.Domain.Entities;

public sealed class Author
{
    public const int MaxNameLength = 300;

    private Author()
    {
    }

    public Author(string name, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        Rename(name);
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Lower-case, accent-free name used for deduplication lookups.</summary>
    public string NormalizedName { get; private set; } = null!;

    public DateOnly? BirthDate { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public ICollection<BookAuthor> Books { get; private set; } = new List<BookAuthor>();

    public ICollection<AuthorCountry> Countries { get; private set; } = new List<AuthorCountry>();

    public ICollection<ExternalAuthorIdentifier> ExternalIdentifiers { get; private set; } = new List<ExternalAuthorIdentifier>();

    public static string NormalizeName(string name) => TextNormalizer.NormalizeForComparison(name);

    public void Rename(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxNameLength)
        {
            throw ValidationException.ForField(
                "author.invalid_name", "name", $"O nome do autor deve ter entre 1 e {MaxNameLength} caracteres.");
        }

        Name = trimmed;
        NormalizedName = NormalizeName(trimmed);
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}

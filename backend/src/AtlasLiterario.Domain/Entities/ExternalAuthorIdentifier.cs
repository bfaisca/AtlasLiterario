using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Domain.Entities;

public sealed class ExternalAuthorIdentifier
{
    public const int MaxExternalIdLength = 100;

    private ExternalAuthorIdentifier()
    {
    }

    public ExternalAuthorIdentifier(Guid authorId, AuthorIdentifierProvider provider, string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        if (externalId.Length > MaxExternalIdLength)
        {
            throw new ArgumentException("External id is too long.", nameof(externalId));
        }

        Id = Guid.CreateVersion7();
        AuthorId = authorId;
        Provider = provider;
        ExternalId = externalId.Trim();
    }

    public Guid Id { get; private set; }

    public Guid AuthorId { get; private set; }

    /// <summary>Re-links the identifier to another author (merge of duplicate authors).</summary>
    public void MoveTo(Guid authorId) => AuthorId = authorId;

    public AuthorIdentifierProvider Provider { get; private set; }

    public string ExternalId { get; private set; } = null!;
}

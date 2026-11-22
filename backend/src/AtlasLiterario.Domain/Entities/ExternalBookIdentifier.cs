using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Domain.Entities;

public sealed class ExternalBookIdentifier
{
    public const int MaxExternalIdLength = 100;

    private ExternalBookIdentifier()
    {
    }

    public ExternalBookIdentifier(Guid bookId, BookIdentifierProvider provider, string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        if (externalId.Length > MaxExternalIdLength)
        {
            throw new ArgumentException("External id is too long.", nameof(externalId));
        }

        Id = Guid.CreateVersion7();
        BookId = bookId;
        Provider = provider;
        ExternalId = externalId.Trim();
    }

    public Guid Id { get; private set; }

    public Guid BookId { get; private set; }

    public BookIdentifierProvider Provider { get; private set; }

    public string ExternalId { get; private set; } = null!;
}

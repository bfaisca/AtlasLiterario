namespace AtlasLiterario.Application.ExternalSources;

/// <summary>Resolves author identities (Open Library keys → Wikidata QIDs).</summary>
public interface IAuthorIdentityResolver
{
    Task<IReadOnlyList<ResolvedAuthor>> ResolveAuthorsAsync(ExternalBookCandidate book, CancellationToken ct);
}

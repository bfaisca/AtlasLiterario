using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Common;

/// <summary>Outcome of the country-edit permission check.</summary>
public enum GeoEditDecision
{
    /// <summary>Apply the requested set.</summary>
    Apply,

    /// <summary>A common user resent exactly the current set: nothing to change, answer 200.</summary>
    NoOp,
}

/// <summary>
/// Who may change countries (docs/business-rules.md §2). The role is read from the database at check time, so a
/// demotion is effective immediately and a stale token claim is never trusted.
/// <list type="bullet">
/// <item>Moderator: always (any book/author, even outside the library).</item>
/// <item>User: only with the book (or a book of the author) in the library AND only when the current set is empty
/// (first information, from any source); resending the current set is a no-op; otherwise 403 geo.moderator_required.</item>
/// </list>
/// </summary>
internal static class GeoEditPolicy
{
    public static async Task<bool> IsModeratorAsync(IAppDbContext db, Guid userId, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct) == UserRole.Moderator;

    public static async Task<GeoEditDecision> CheckOriginsAsync(
        IAppDbContext db, Guid userId, Guid bookId, IReadOnlyCollection<Country> requested, CancellationToken ct)
    {
        if (await IsModeratorAsync(db, userId, ct))
        {
            return GeoEditDecision.Apply;
        }

        await LibraryAccess.EnsureUserHasBookAsync(db, userId, bookId, ct);
        var current = await db.BookOriginCountries.AsNoTracking()
            .Where(o => o.BookId == bookId).Select(o => o.CountryId).ToListAsync(ct);
        return Decide(current, requested);
    }

    public static async Task<GeoEditDecision> CheckCitizenshipsAsync(
        IAppDbContext db, Guid userId, Guid authorId, IReadOnlyCollection<Country> requested, CancellationToken ct)
    {
        if (await IsModeratorAsync(db, userId, ct))
        {
            return GeoEditDecision.Apply;
        }

        await LibraryAccess.EnsureUserHasAuthorAsync(db, userId, authorId, ct);
        var current = await db.AuthorCountries.AsNoTracking()
            .Where(c => c.AuthorId == authorId && c.RelationshipType == AuthorCountryRelationship.Citizenship)
            .Select(c => c.CountryId).ToListAsync(ct);
        return Decide(current, requested);
    }

    private static GeoEditDecision Decide(IReadOnlyCollection<short> current, IReadOnlyCollection<Country> requested)
    {
        if (current.Count == 0)
        {
            return requested.Count == 0 ? GeoEditDecision.NoOp : GeoEditDecision.Apply;
        }

        if (current.ToHashSet().SetEquals(requested.Select(c => c.Id)))
        {
            return GeoEditDecision.NoOp;
        }

        throw new ForbiddenException(
            "geo.moderator_required",
            "Os países já informados só podem ser alterados ou removidos por um moderador. Você pode preencher apenas quando ainda não há país cadastrado.");
    }
}

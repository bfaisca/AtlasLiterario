using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Common;

/// <summary>Permission checks shared by catalog corrections (business-rules §2: manual correction requires the book in the library).</summary>
internal static class LibraryAccess
{
    public static async Task EnsureUserHasBookAsync(IAppDbContext db, Guid userId, Guid bookId, CancellationToken ct)
    {
        if (!await db.UserBooks.AnyAsync(ub => ub.UserId == userId && ub.BookId == bookId, ct))
        {
            throw new ForbiddenException(
                "book.not_in_library", "Só é possível corrigir livros que estão na sua biblioteca.");
        }
    }

    public static async Task EnsureUserHasAuthorAsync(IAppDbContext db, Guid userId, Guid authorId, CancellationToken ct)
    {
        if (!await db.UserBooks.AnyAsync(
                ub => ub.UserId == userId && ub.Book!.Authors.Any(ba => ba.AuthorId == authorId), ct))
        {
            throw new ForbiddenException(
                "author.not_in_library", "Só é possível corrigir autores de livros que estão na sua biblioteca.");
        }
    }
}

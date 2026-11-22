using AtlasLiterario.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Maps;

/// <summary>Single place that builds <see cref="CountryBookDto"/> from library items (map panels and dashboard).</summary>
internal static class CountryBookQuery
{
    /// <summary>Projects the (already filtered, user-scoped) library items; one query, authors in book position order.</summary>
    public static async Task<List<CountryBookDto>> ToDtosAsync(IQueryable<UserBook> items, CancellationToken ct)
    {
        var rows = await items
            .Select(ub => new
            {
                ub.Id,
                ub.BookId,
                ub.Book!.Title,
                ub.Book.CoverUrl,
                Authors = ub.Book.Authors.OrderBy(ba => ba.Position).Select(ba => ba.Author!.Name).ToList(),
                ub.FinishedDate,
                ub.Rating,
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new CountryBookDto(r.Id, r.BookId, r.Title, r.CoverUrl, r.Authors, r.FinishedDate, r.Rating))
            .ToList();
    }

    /// <summary>finishedDate desc (nulls last), ties by title.</summary>
    public static List<CountryBookDto> Order(IEnumerable<CountryBookDto> books) =>
        books
            .OrderBy(b => b.FinishedDate is null)
            .ThenByDescending(b => b.FinishedDate)
            .ThenBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}

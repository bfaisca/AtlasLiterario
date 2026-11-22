using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Common;

internal static class CountryResolution
{
    /// <summary>Resolves ISO alpha-3 codes (case-insensitive) to countries; unknown codes → 400 <c>country.unknown_iso</c>.</summary>
    public static async Task<IReadOnlyList<Country>> ResolveIsosAsync(
        IAppDbContext db, IReadOnlyList<string>? isos, string field, CancellationToken ct)
    {
        var requested = (isos ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (requested.Count == 0)
        {
            return [];
        }

        var countries = await db.Countries.Where(c => requested.Contains(c.IsoCode)).ToListAsync(ct);
        var unknown = requested.Except(countries.Select(c => c.IsoCode), StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            throw new ValidationException(
                "country.unknown_iso",
                "Código de país desconhecido.",
                new Dictionary<string, string[]> { [field] = unknown.Select(u => $"ISO 3166-1 alpha-3 desconhecido: {u}").ToArray() });
        }

        return countries;
    }
}

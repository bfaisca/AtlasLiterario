using AtlasLiterario.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Countries;

public interface ICountryService
{
    Task<IReadOnlyList<CountryDto>> GetAllAsync(CancellationToken ct);
}

public sealed class CountryService(IAppDbContext db) : ICountryService
{
    public async Task<IReadOnlyList<CountryDto>> GetAllAsync(CancellationToken ct) =>
        await db.Countries
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CountryDto(c.IsoCode, c.IsoCode2, c.Name, c.NamePt, c.Continent, c.GeoJsonId))
            .ToListAsync(ct);
}

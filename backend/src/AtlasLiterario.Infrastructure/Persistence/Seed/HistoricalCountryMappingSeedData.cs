using AtlasLiterario.Domain.Entities;

namespace AtlasLiterario.Infrastructure.Persistence.Seed;

/// <summary>
/// Wikidata entities without a usable ISO 3166-1 alpha-3 (P298) mapped to current countries.
/// Every QID below was checked on 2026-11-15 (query.wikidata.org, English label shown in the note).
/// Some have ISO 3166-3 codes (SUN, CSK, DDR, YUG) that do not exist in the country table, so the
/// resolver must consult this table whenever P298 is missing OR unknown.
/// Mappings to a single successor are a simplification: results get reduced confidence and IsVerified=false.
/// </summary>
internal static class HistoricalCountryMappingSeedData
{
    private const short Austria = 40;
    private const short Czechia = 203;
    private const short Denmark = 208;
    private const short Germany = 276;
    private const short Netherlands = 528;
    private const short Russia = 643;
    private const short Serbia = 688;
    private const short Turkey = 792;
    private const short UnitedKingdom = 826;

    public static IReadOnlyList<HistoricalCountryMapping> All { get; } =
    [
        new(1, "Q28513", Austria, "Austria-Hungary"),
        new(2, "Q131964", Austria, "Austrian Empire"),
        new(3, "Q15180", Russia, "Soviet Union (P298 SUN)"),
        new(4, "Q34266", Russia, "Russian Empire"),
        new(5, "Q33946", Czechia, "Czechoslovakia (P298 CSK)"),
        new(6, "Q36704", Serbia, "Yugoslavia"),
        new(7, "Q83286", Serbia, "Socialist Federal Republic of Yugoslavia (P298 YUG)"),
        new(8, "Q12560", Turkey, "Ottoman Empire"),
        new(9, "Q27306", Germany, "Kingdom of Prussia"),
        new(10, "Q43287", Germany, "German Empire"),
        new(11, "Q41304", Germany, "Weimar Republic"),
        new(12, "Q7318", Germany, "Nazi Germany"),
        new(13, "Q16957", Germany, "East Germany (P298 DDR)"),
        new(14, "Q713750", Germany, "West Germany"),
        new(15, "Q174193", UnitedKingdom, "United Kingdom of Great Britain and Ireland"),
        new(16, "Q161885", UnitedKingdom, "Kingdom of Great Britain"),
        new(17, "Q8680", UnitedKingdom, "British Empire"),
        // Current entities whose P298 lives on another item:
        new(18, "Q55", Netherlands, "Netherlands (constituent country; P298 NLD is on Q29999)"),
        new(19, "Q756617", Denmark, "Kingdom of Denmark (P298 DNK is on Q35)"),
        // Added in Etapa 3 (checked 2026-11-15): Kafka's P27 is Czechoslovakia + Cisleithania, not Austria-Hungary.
        new(20, "Q533534", Austria, "Cisleithania (Austrian half of Austria-Hungary)"),
        new(21, "Q42585", Czechia, "Kingdom of Bohemia"),
    ];
}

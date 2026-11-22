using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Maps;

/// <summary>
/// Visualization filter of the private maps (docs/business-rules.md §3). Default is <see cref="Read"/>;
/// statistics, Passports and public sharing are not affected by it and always use Read.
/// </summary>
public enum MapStatusFilter
{
    Read = 0,
    WantToRead,
    Reading,
    Abandoned,
    All,
}

public static class MapStatusFilterParser
{
    /// <summary>Null/blank means the default (Read). Case-insensitive; anything else is a 400 on field <c>status</c>.</summary>
    public static MapStatusFilter Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MapStatusFilter.Read;
        }

        var text = value.Trim();
        // Exact names only: Enum.TryParse would also accept numbers and comma-separated lists.
        var name = Enum.GetNames<MapStatusFilter>().FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
        if (name is not null)
        {
            return Enum.Parse<MapStatusFilter>(name);
        }

        new ValidationErrors()
            .Add("status", "Status inválido (Read, WantToRead, Reading, Abandoned, All).")
            .ThrowIfAny();
        throw new InvalidOperationException("Unreachable.");
    }

    public static bool Matches(this MapStatusFilter filter, ReadingStatus status) =>
        filter == MapStatusFilter.All || filter.ToReadingStatus() == status;

    public static ReadingStatus ToReadingStatus(this MapStatusFilter filter) => filter switch
    {
        MapStatusFilter.Read => ReadingStatus.Read,
        MapStatusFilter.WantToRead => ReadingStatus.WantToRead,
        MapStatusFilter.Reading => ReadingStatus.Reading,
        MapStatusFilter.Abandoned => ReadingStatus.Abandoned,
        _ => throw new InvalidOperationException("All has no single reading status."),
    };
}

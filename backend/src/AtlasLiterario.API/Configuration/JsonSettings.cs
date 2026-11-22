using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasLiterario.API.Configuration;

/// <summary>JSON conventions of docs/api-contracts.md §0: camelCase, enums as strings, instants in UTC with "Z".</summary>
public static class JsonSettings
{
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        // Numbers are numbers (also keeps OpenAPI schemas as "integer"/"number" only).
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new UtcDateTimeOffsetJsonConverter());
    }
}

/// <summary>Writes DateTimeOffset as ISO-8601 UTC ("2025-03-14T18:20:00Z").</summary>
public sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture));
}

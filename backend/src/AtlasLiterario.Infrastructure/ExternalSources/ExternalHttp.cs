using System.Net;
using System.Text.Json;
using AtlasLiterario.Application.Common;

namespace AtlasLiterario.Infrastructure.ExternalSources;

internal static class ExternalHttp
{
    /// <summary>
    /// GET + JSON parse. 404 → null. Any other failure (HTTP error, timeout, open circuit, invalid JSON)
    /// → <see cref="ExternalServiceUnavailableException"/>. Messages never contain URLs (they could carry credentials).
    /// </summary>
    /// <summary>Retry-After as a delay (seconds or HTTP date); null when absent or unparsable.</summary>
    internal static TimeSpan? RetryAfter(HttpResponseMessage response, TimeProvider? time = null)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - (time ?? TimeProvider.System).GetUtcNow() : null;
    }

    public static async Task<JsonDocument?> GetJsonAsync(
        HttpClient client, string relativeUri, string sourceName, CancellationToken ct, string? accept = null,
        ExternalCooldown? cooldown = null)
    {
        if (cooldown?.IsPaused(sourceName) == true)
        {
            throw new ExternalServiceUnavailableException(sourceName, $"{sourceName} em pausa após limite de requisições (HTTP 429).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        if (accept is not null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                cooldown?.Pause(sourceName, RetryAfter(response));
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ExternalServiceUnavailableException(
                    sourceName, $"{sourceName} respondeu HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (ExternalServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new ExternalServiceUnavailableException(
                sourceName, $"{sourceName} indisponível ({ex.GetType().Name}).", ex);
        }
    }
}

/// <summary>Defensive accessors: missing or unexpected JSON shapes yield null/empty, never exceptions.</summary>
internal static class JsonElementExtensions
{
    public static JsonElement? Prop(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value
            : null;

    public static string? Str(this JsonElement element, string name)
    {
        var value = element.Prop(name);
        return value?.ValueKind == JsonValueKind.String ? CleanText(value.Value.GetString()) : null;
    }

    /// <summary>
    /// Removes C1 control characters (Open Library titles carry U+0098/U+009C "non-sorting" markers,
    /// e.g. "\u0098O \u009cprocesso") and trims. Returns null when nothing is left.
    /// </summary>
    internal static string? CleanText(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var cleaned = string.Concat(text.Where(c => c is < '\u0080' or > '\u009f')).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    public static int? Int(this JsonElement element, string name)
    {
        var value = element.Prop(name);
        return value?.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number) ? number : null;
    }

    public static IEnumerable<JsonElement> Arr(this JsonElement element, string name)
    {
        var value = element.Prop(name);
        return value?.ValueKind == JsonValueKind.Array ? value.Value.EnumerateArray() : [];
    }

    public static IEnumerable<string> Strings(this JsonElement element, string name) =>
        element.Arr(name)
            .Where(v => v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
            .Select(v => v.GetString()!.Trim());
}

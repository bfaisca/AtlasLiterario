using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasLiterario.Application.Auth;
using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Library;
using AtlasLiterario.Application.ExternalSources;
using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.API.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class CoverAndSearchTests(AtlasApiFactory factory) : IClassFixture<AtlasApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // Minimal byte sequences with the real magic numbers (content is irrelevant, only the signature is validated).
    private static byte[] Jpeg(int size = 64) => Pad([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00], size);

    private static byte[] Png(int size = 64) => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D], size);

    private static byte[] WebP(int size = 64) =>
        Pad([.. "RIFF"u8, (byte)(size - 8), 0, 0, 0, .. "WEBPVP8 "u8], size);

    private static byte[] Pad(byte[] head, int size)
    {
        var data = new byte[Math.Max(size, head.Length)];
        head.CopyTo(data, 0);
        for (var i = head.Length; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        return data;
    }

    private static string Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private async Task<HttpClient> UserClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { name = "Leitora", email = $"c-{Guid.NewGuid():N}@example.com", password = "segredo123" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    private static MultipartFormDataContent FileForm(byte[] data, string fileName = "capa.jpg", string contentType = "image/jpeg", string field = "file")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, field, fileName);
        return content;
    }

    /// <summary>Manual book + library item for the caller.</summary>
    private static async Task<(BookDetailsDto Book, LibraryItemDto Item)> ManualBookInLibraryAsync(
        HttpClient client, string title = "Livro com Capa", string? isbn13 = null, string? isbn10 = null, string author = "Autora Manual")
    {
        var book = await ReadAsync<BookDetailsDto>(
            await client.PostAsJsonAsync("/api/books", new { title, authorNames = new[] { author }, isbn13, isbn10 }, Json),
            HttpStatusCode.Created);
        var item = await ReadAsync<LibraryItemDto>(
            await client.PostAsJsonAsync("/api/library", new { bookId = book.Id, status = "WantToRead" }, Json), HttpStatusCode.Created);
        return (book, item);
    }

    // ---------------------------------------------------------------- upload

    [Theory]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public async Task Valid_images_are_accepted_by_magic_bytes_regardless_of_name_and_content_type(string kind, string expected)
    {
        var client = await UserClientAsync();
        var (book, _) = await ManualBookInLibraryAsync(client);
        var data = kind switch { "jpeg" => Jpeg(), "png" => Png(), _ => WebP() };

        // Misleading name and Content-Type on purpose.
        var updated = await ReadAsync<BookDetailsDto>(
            await client.PostAsync($"/api/books/{book.Id}/cover", FileForm(data, "qualquer.bin", "application/octet-stream")),
            HttpStatusCode.OK);

        updated.CoverUrl.ShouldBe($"/api/books/{book.Id}/cover?v={Hex(data)[..12]}");
        var get = await factory.CreateClient().GetAsync(updated.CoverUrl);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        get.Content.Headers.ContentType!.MediaType.ShouldBe(expected);
        (await get.Content.ReadAsByteArrayAsync()).ShouldBe(data);
    }

    [Fact]
    public async Task Other_formats_and_renamed_files_are_rejected_with_cover_invalid_type()
    {
        var client = await UserClientAsync();
        var (book, _) = await ManualBookInLibraryAsync(client);
        var samples = new Dictionary<string, byte[]>
        {
            ["gif"] = Encoding.ASCII.GetBytes("GIF89a\u0001\0\u0001\0\0\0\0;"),
            ["svg"] = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"),
            ["pdf"] = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n"),
            ["text"] = Encoding.UTF8.GetBytes("isto não é uma imagem, só foi renomeado para .jpg"),
            ["riff-not-webp"] = [.. "RIFF"u8, 4, 0, 0, 0, .. "WAVEfmt "u8],
        };

        foreach (var (name, data) in samples)
        {
            var response = await client.PostAsync($"/api/books/{book.Id}/cover", FileForm(data, "foto.jpg", "image/jpeg"));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
            (await CodeAsync(response)).ShouldBe("cover.invalid_type", name);
        }

        (await client.GetAsync($"/api/books/{book.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.CreateClient().GetAsync($"/api/books/{book.Id}/cover")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Empty_or_missing_file_is_a_validation_error()
    {
        var client = await UserClientAsync();
        var (book, _) = await ManualBookInLibraryAsync(client);

        var empty = await client.PostAsync($"/api/books/{book.Id}/cover", FileForm([]));
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(empty)).ShouldBe("validation.failed");

        var wrongField = await client.PostAsync($"/api/books/{book.Id}/cover", FileForm(Jpeg(), field: "outro"));
        wrongField.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(wrongField)).ShouldBe("validation.failed");
    }

    [Fact]
    public async Task Two_megabytes_is_accepted_and_one_byte_more_is_413_cover_too_large()
    {
        var client = await UserClientAsync();
        var (book, _) = await ManualBookInLibraryAsync(client);

        (await client.PostAsync($"/api/books/{book.Id}/cover", FileForm(Jpeg(2 * 1024 * 1024)))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var tooBig = await client.PostAsync($"/api/books/{book.Id}/cover", FileForm(Jpeg(2 * 1024 * 1024 + 1)));
        tooBig.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await CodeAsync(tooBig)).ShouldBe("cover.too_large");
    }

    [Fact]
    public async Task Upload_requires_authentication_the_book_and_the_book_in_the_library()
    {
        var owner = await UserClientAsync();
        var (book, _) = await ManualBookInLibraryAsync(owner);

        (await factory.CreateClient().PostAsync($"/api/books/{book.Id}/cover", FileForm(Jpeg()))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var stranger = await UserClientAsync();
        var forbidden = await stranger.PostAsync($"/api/books/{book.Id}/cover", FileForm(Jpeg()));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(forbidden)).ShouldBe("book.not_in_library");
        (await stranger.DeleteAsync($"/api/books/{book.Id}/cover")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var missing = await owner.PostAsync($"/api/books/{Guid.NewGuid()}/cover", FileForm(Jpeg()));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(missing)).ShouldBe("book.not_found");
    }

    [Fact]
    public async Task Replacing_changes_the_url_and_delete_is_idempotent()
    {
        var client = await UserClientAsync();
        var (book, _) = await ManualBookInLibraryAsync(client);
        var first = Jpeg(100);
        var second = Png(120);

        var one = await ReadAsync<BookDetailsDto>(await client.PostAsync($"/api/books/{book.Id}/cover", FileForm(first)), HttpStatusCode.OK);
        var two = await ReadAsync<BookDetailsDto>(await client.PostAsync($"/api/books/{book.Id}/cover", FileForm(second)), HttpStatusCode.OK);
        two.CoverUrl.ShouldNotBe(one.CoverUrl);
        two.CoverUrl.ShouldBe($"/api/books/{book.Id}/cover?v={Hex(second)[..12]}");
        var anonymous = factory.CreateClient();
        (await (await anonymous.GetAsync($"/api/books/{book.Id}/cover")).Content.ReadAsByteArrayAsync()).ShouldBe(second);

        var removed = await ReadAsync<BookDetailsDto>(await client.DeleteAsync($"/api/books/{book.Id}/cover"), HttpStatusCode.OK);
        removed.CoverUrl.ShouldBeNull();
        var gone = await anonymous.GetAsync($"/api/books/{book.Id}/cover");
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(gone)).ShouldBe("cover.not_found");

        var again = await ReadAsync<BookDetailsDto>(await client.DeleteAsync($"/api/books/{book.Id}/cover"), HttpStatusCode.OK);
        again.CoverUrl.ShouldBeNull();
    }

    // ------------------------------------------------------------- anonymous GET

    [Fact]
    public async Task Anonymous_get_serves_only_the_image_with_security_and_cache_headers_and_supports_304()
    {
        var client = await UserClientAsync();
        var (book, _) = await ManualBookInLibraryAsync(client);
        var data = WebP(300);
        var details = await ReadAsync<BookDetailsDto>(await client.PostAsync($"/api/books/{book.Id}/cover", FileForm(data)), HttpStatusCode.OK);
        var anonymous = factory.CreateClient();

        var ok = await anonymous.GetAsync(details.CoverUrl);

        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        ok.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        ok.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        ok.Headers.ETag!.Tag.ShouldBe($"\"{Hex(data)}\"");
        ok.Headers.CacheControl!.ToString().ShouldBe("public, max-age=31536000, immutable");
        ok.Content.Headers.ContentDisposition.ShouldBeNull();
        ok.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await ok.Content.ReadAsByteArrayAsync()).ShouldBe(data);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/books/{book.Id}/cover");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", $"\"{Hex(data)}\"");
        var notModified = await anonymous.SendAsync(conditional);
        notModified.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        (await notModified.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
        notModified.Headers.ETag!.Tag.ShouldBe($"\"{Hex(data)}\"");

        using var stale = new HttpRequestMessage(HttpMethod.Get, $"/api/books/{book.Id}/cover");
        stale.Headers.TryAddWithoutValidation("If-None-Match", "\"outro\"");
        (await anonymous.SendAsync(stale)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Not_found_and_other_endpoints_never_get_the_public_immutable_cache_header()
    {
        var client = await UserClientAsync();
        var (book, _) = await ManualBookInLibraryAsync(client);
        var anonymous = factory.CreateClient();

        var missing = await anonymous.GetAsync($"/api/books/{book.Id}/cover");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(missing)).ShouldBe("cover.not_found");
        missing.Headers.CacheControl?.ToString().ShouldNotContain("immutable");
        (await anonymous.GetAsync($"/api/books/{Guid.NewGuid()}/cover")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The rest of the book stays private: anonymous cannot read the book, and the authenticated response is not public-cacheable.
        (await anonymous.GetAsync($"/api/books/{book.Id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var authed = await client.GetAsync($"/api/books/{book.Id}");
        authed.Headers.CacheControl?.ToString().ShouldNotContain("public");
        (await anonymous.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------- PUT / POST coverUrl semantics

    [Fact]
    public async Task Put_with_internal_url_keeps_external_replaces_and_null_removes()
    {
        var client = await UserClientAsync();
        var (book, _) = await ManualBookInLibraryAsync(client);
        var uploaded = await ReadAsync<BookDetailsDto>(await client.PostAsync($"/api/books/{book.Id}/cover", FileForm(Jpeg())), HttpStatusCode.OK);
        var anonymous = factory.CreateClient();

        // 1) the frontend sends the current internal URL back: kept.
        var kept = await ReadAsync<BookDetailsDto>(
            await client.PutAsJsonAsync($"/api/books/{book.Id}", new { title = "Livro com Capa", authorNames = new[] { "Autora Manual" }, coverUrl = uploaded.CoverUrl }, Json),
            HttpStatusCode.OK);
        kept.CoverUrl.ShouldBe(uploaded.CoverUrl);
        (await anonymous.GetAsync(uploaded.CoverUrl)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // 2) external https URL: replaces and deletes the uploaded file.
        var external = await ReadAsync<BookDetailsDto>(
            await client.PutAsJsonAsync($"/api/books/{book.Id}", new { title = "Livro com Capa", authorNames = new[] { "Autora Manual" }, coverUrl = "https://covers.example.org/c.jpg" }, Json),
            HttpStatusCode.OK);
        external.CoverUrl.ShouldBe("https://covers.example.org/c.jpg");
        (await anonymous.GetAsync($"/api/books/{book.Id}/cover")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // an internal URL without an uploaded cover is invalid now
        var stale = await client.PutAsJsonAsync($"/api/books/{book.Id}", new { title = "Livro com Capa", authorNames = new[] { "Autora Manual" }, coverUrl = uploaded.CoverUrl }, Json);
        stale.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 3) upload again, then null removes the cover and the row.
        await ReadAsync<BookDetailsDto>(await client.PostAsync($"/api/books/{book.Id}/cover", FileForm(Png())), HttpStatusCode.OK);
        var removed = await ReadAsync<BookDetailsDto>(
            await client.PutAsJsonAsync($"/api/books/{book.Id}", new { title = "Livro com Capa", authorNames = new[] { "Autora Manual" }, coverUrl = (string?)null }, Json),
            HttpStatusCode.OK);
        removed.CoverUrl.ShouldBeNull();
        (await anonymous.GetAsync($"/api/books/{book.Id}/cover")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // empty string behaves like null
        await ReadAsync<BookDetailsDto>(await client.PostAsync($"/api/books/{book.Id}/cover", FileForm(Png())), HttpStatusCode.OK);
        var emptied = await ReadAsync<BookDetailsDto>(
            await client.PutAsJsonAsync($"/api/books/{book.Id}", new { title = "Livro com Capa", authorNames = new[] { "Autora Manual" }, coverUrl = "" }, Json),
            HttpStatusCode.OK);
        emptied.CoverUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Relative_paths_other_than_the_books_own_cover_are_rejected()
    {
        var client = await UserClientAsync();
        var (book, _) = await ManualBookInLibraryAsync(client, "Livro A");
        var (other, _) = await ManualBookInLibraryAsync(client, "Livro B");
        var otherCover = await ReadAsync<BookDetailsDto>(await client.PostAsync($"/api/books/{other.Id}/cover", FileForm(Jpeg())), HttpStatusCode.OK);

        foreach (var url in new[] { otherCover.CoverUrl!, "/api/library", "../etc/passwd", "javascript:alert(1)", "http://insecure.example/c.jpg", "ftp://x/c.jpg" })
        {
            var put = await client.PutAsJsonAsync($"/api/books/{book.Id}", new { title = "Livro A", authorNames = new[] { "Autora Manual" }, coverUrl = url }, Json);
            put.StatusCode.ShouldBe(HttpStatusCode.BadRequest, url);
        }

        var post = await client.PostAsJsonAsync("/api/books", new { title = "Novo", authorNames = new[] { "X" }, coverUrl = "/api/books/x/cover" }, Json);
        post.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var ok = await ReadAsync<BookDetailsDto>(
            await client.PostAsJsonAsync("/api/books", new { title = "Novo 2", authorNames = new[] { "X" }, coverUrl = "https://covers.example.org/n.jpg" }, Json),
            HttpStatusCode.Created);
        ok.CoverUrl.ShouldBe("https://covers.example.org/n.jpg");
    }

    // ------------------------------------------------------------- imports never overwrite an uploaded cover

    [Fact]
    public async Task Reimport_and_enrichment_do_not_overwrite_an_uploaded_cover()
    {
        var first = await UserClientAsync();
        var item = await ReadAsync<LibraryItemDto>(
            await first.PostAsJsonAsync("/api/library", new { source = new { provider = "OpenLibrary", externalId = "OL1003040W" }, status = "WantToRead" }, Json),
            HttpStatusCode.Created);
        item.Book.CoverUrl.ShouldStartWith("https://covers.openlibrary.org/");
        var uploaded = await ReadAsync<BookDetailsDto>(await first.PostAsync($"/api/books/{item.Book.Id}/cover", FileForm(Jpeg(200))), HttpStatusCode.OK);

        var second = await UserClientAsync(); // imports the same external book again
        var again = await ReadAsync<LibraryItemDto>(
            await second.PostAsJsonAsync("/api/library", new { source = new { provider = "OpenLibrary", externalId = "OL1003040W" }, status = "Read" }, Json),
            HttpStatusCode.Created);
        again.Book.Id.ShouldBe(item.Book.Id);
        again.Book.CoverUrl.ShouldBe(uploaded.CoverUrl);

        var enriched = await ReadAsync<BookDetailsDto>(await first.PostAsync($"/api/books/{item.Book.Id}/enrich", null), HttpStatusCode.OK);
        enriched.CoverUrl.ShouldBe(uploaded.CoverUrl);
    }

    // ------------------------------------------------------------- manual books are searchable

    [Fact]
    public async Task Manual_book_is_found_by_title_author_isbn13_isbn10_and_all_with_local_source()
    {
        var owner = await UserClientAsync();
        var (book, item) = await ManualBookInLibraryAsync(
            owner, "Tratado Quimérico das Mangueiras", isbn13: "9780306406157", isbn10: "0306406152", author: "Zuleica Hortênsia Quimera");
        var cover = await ReadAsync<BookDetailsDto>(await owner.PostAsync($"/api/books/{book.Id}/cover", FileForm(Png())), HttpStatusCode.OK);

        foreach (var (q, type) in new[]
                 {
                     ("quimérico das mangueiras", "title"), ("Zuleica Hortênsia", "author"), ("9780306406157", "isbn"),
                     ("0306406152", "isbn"), ("978-0-306-40615-7", "isbn"), ("mangueiras", "all"), ("Hortênsia Quimera", "all"),
                     ("9780306406157", "all"), ("0306406152", "all"),
                 })
        {
            var results = await ReadAsync<List<BookSearchResultDto>>(
                await owner.GetAsync($"/api/books/search?q={Uri.EscapeDataString(q)}&type={type}"), HttpStatusCode.OK);
            var hit = results.SingleOrDefault(r => r.BookId == book.Id);
            hit.ShouldNotBeNull($"{type}:{q}");
            hit.Source.Provider.ShouldBe(ExternalProvider.Local);
            hit.ResultKey.ShouldBe($"local:{book.Id}");
            hit.InLibrary.ShouldBeTrue();
            hit.LibraryItemId.ShouldBe(item.Id);
            hit.CoverUrl.ShouldBe(cover.CoverUrl);
            hit.Authors.ShouldBe(["Zuleica Hortênsia Quimera"]);
        }
    }

    [Fact]
    public async Task Manual_book_is_visible_to_another_user_not_in_library_even_when_every_external_source_fails()
    {
        var owner = await UserClientAsync();
        var (book, _) = await ManualBookInLibraryAsync(owner, "Almanaque dos Faróis Esquecidos", isbn13: "9780140328721");
        var other = await UserClientAsync();

        factory.ExternalSearchDown = true; // Google always over quota + Open Library down
        try
        {
            foreach (var (q, type) in new[] { ("faróis esquecidos", "title"), ("9780140328721", "isbn"), ("almanaque", "all") })
            {
                var response = await other.GetAsync($"/api/books/search?q={Uri.EscapeDataString(q)}&type={type}");
                var results = await ReadAsync<List<BookSearchResultDto>>(response, HttpStatusCode.OK); // not 502: local result exists
                var hit = results.Single(r => r.BookId == book.Id);
                hit.InLibrary.ShouldBeFalse();
                hit.LibraryItemId.ShouldBeNull();
                hit.Source.Provider.ShouldBe(ExternalProvider.Local);
            }

            // Without a local match every source failing is still a 502.
            (await other.GetAsync("/api/books/search?q=zzzxqwv-inexistente&type=title")).StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        }
        finally
        {
            factory.ExternalSearchDown = false;
        }

        // The other user adds it from the search result (by catalog id) and then it is flagged as in the library.
        var added = await ReadAsync<LibraryItemDto>(
            await other.PostAsJsonAsync("/api/library", new { bookId = book.Id, status = "Reading" }, Json), HttpStatusCode.Created);
        var after = await ReadAsync<List<BookSearchResultDto>>(await other.GetAsync("/api/books/search?q=almanaque&type=title"), HttpStatusCode.OK);
        after.Single(r => r.BookId == book.Id).LibraryItemId.ShouldBe(added.Id);
    }
}

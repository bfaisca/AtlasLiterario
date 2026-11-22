using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasLiterario.Application.Auth;
using AtlasLiterario.Application.Authors;
using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Library;
using AtlasLiterario.Application.Maps;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasLiterario.API.IntegrationTests;

/// <summary>Who may change countries: first information by any user with the book; changes only by a Moderator (role read from the DB).</summary>
[Trait("Category", "Integration")]
public sealed class GeoPermissionTests(AtlasApiFactory factory) : IClassFixture<AtlasApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private sealed record Session(HttpClient Client, Guid UserId, string Email, AuthResponse Auth);

    private async Task<Session> NewUserAsync()
    {
        var client = factory.CreateClient();
        var email = $"g-{Guid.NewGuid():N}@example.com";
        var auth = await ReadAsync<AuthResponse>(
            await client.PostAsJsonAsync("/api/auth/register", new { name = "Leitora", email, password = "segredo123" }), HttpStatusCode.Created);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new Session(client, auth.User.Id, email, auth);
    }

    private async Task SetRoleAsync(Session session, UserRole role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(u => u.Id == session.UserId)).SetRole(role, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    private static async Task<BookDetailsDto> CreateBookAsync(Session owner, string[]? origins, string? author = null) =>
        await ReadAsync<BookDetailsDto>(
            await owner.Client.PostAsJsonAsync("/api/books",
                new { title = $"Livro {Guid.NewGuid():N}", authorNames = new[] { author ?? $"Autor {Guid.NewGuid():N}" }, originCountryIsos = origins }, Json),
            HttpStatusCode.Created);

    private static async Task<LibraryItemDto> AddAsync(Session s, Guid bookId) =>
        await ReadAsync<LibraryItemDto>(await s.Client.PostAsJsonAsync("/api/library", new { bookId, status = "WantToRead" }, Json), HttpStatusCode.Created);

    private static Task<HttpResponseMessage> PutOrigins(Session s, Guid bookId, params string[] isos) =>
        s.Client.PutAsJsonAsync($"/api/books/{bookId}/origin-countries", new { countryIsos = isos }, Json);

    private static Task<HttpResponseMessage> PutCitizenship(Session s, Guid authorId, params string[] isos) =>
        s.Client.PutAsJsonAsync($"/api/authors/{authorId}/countries", new { citizenshipIsos = isos }, Json);

    // ------------------------------------------------------------------ origins

    [Fact]
    public async Task User_can_fill_an_empty_origin_once_then_only_a_moderator_can_change_it()
    {
        var ana = await NewUserAsync();
        var book = await CreateBookAsync(ana, origins: null);
        await AddAsync(ana, book.Id);

        var filled = await ReadAsync<BookDetailsDto>(await PutOrigins(ana, book.Id, "jpn"), HttpStatusCode.OK);
        var origin = filled.OriginCountries.ShouldHaveSingleItem();
        (origin.CountryIso, origin.Source, origin.Confidence, origin.IsVerified).ShouldBe(("JPN", GeoSource.Manual, 1m, true));

        var change = await PutOrigins(ana, book.Id, "KOR");
        change.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(change)).ShouldBe("geo.moderator_required");
        (await PutOrigins(ana, book.Id)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // removing is a change too

        // Resending exactly the current set is a no-op, not an error.
        var same = await ReadAsync<BookDetailsDto>(await PutOrigins(ana, book.Id, "JPN"), HttpStatusCode.OK);
        same.OriginCountries.ShouldHaveSingleItem().CountryIso.ShouldBe("JPN");
        // Empty -> empty is a no-op too.
        var empty = await CreateBookAsync(ana, origins: null);
        await AddAsync(ana, empty.Id);
        (await ReadAsync<BookDetailsDto>(await PutOrigins(ana, empty.Id), HttpStatusCode.OK)).OriginCountries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Origins_filled_by_the_creator_or_by_wikidata_are_locked_for_common_users()
    {
        var ana = await NewUserAsync();
        var manual = await CreateBookAsync(ana, origins: ["BRA"]); // creation = first information, allowed
        manual.OriginCountries.ShouldHaveSingleItem().Source.ShouldBe(GeoSource.Manual);
        await AddAsync(ana, manual.Id);
        var bia = await NewUserAsync();
        await AddAsync(bia, manual.Id);
        var denied = await PutOrigins(bia, manual.Id, "PRT");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(denied)).ShouldBe("geo.moderator_required");
        denied.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await denied.Content.ReadAsStringAsync()).ShouldContain("moderador"); // Portuguese message

        // Wikidata-filled origin (Dom Casmurro -> BRA in the fake).
        var imported = await ReadAsync<LibraryItemDto>(
            await bia.Client.PostAsJsonAsync("/api/library",
                new { source = new { provider = "OpenLibrary", externalId = "OL1003040W" }, status = "Read" }, Json), HttpStatusCode.Created);
        imported.Book.OriginCountries.ShouldHaveSingleItem().Source.ShouldBe(GeoSource.Wikidata);
        (await CodeAsync(await PutOrigins(bia, imported.Book.Id, "ARG"))).ShouldBe("geo.moderator_required");
        (await PutOrigins(bia, imported.Book.Id, "BRA")).StatusCode.ShouldBe(HttpStatusCode.OK); // no-op
    }

    [Fact]
    public async Task User_without_the_book_gets_not_in_library_and_a_moderator_does_not_need_it()
    {
        var ana = await NewUserAsync();
        var book = await CreateBookAsync(ana, origins: ["BRA"]);
        await AddAsync(ana, book.Id);
        var stranger = await NewUserAsync();

        var forbidden = await PutOrigins(stranger, book.Id, "BRA");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(forbidden)).ShouldBe("book.not_in_library");
        (await PutOrigins(stranger, Guid.NewGuid(), "BRA")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await SetRoleAsync(stranger, UserRole.Moderator);
        var edited = await ReadAsync<BookDetailsDto>(await PutOrigins(stranger, book.Id, "PRT"), HttpStatusCode.OK);
        var origin = edited.OriginCountries.ShouldHaveSingleItem();
        (origin.CountryIso, origin.Source, origin.Confidence, origin.IsVerified).ShouldBe(("PRT", GeoSource.Manual, 1m, true));
        (await ReadAsync<BookDetailsDto>(await PutOrigins(stranger, book.Id), HttpStatusCode.OK)).OriginCountries.ShouldBeEmpty(); // removal
    }

    [Fact]
    public async Task Put_book_applies_the_same_rule_to_originCountryIsos()
    {
        var ana = await NewUserAsync();
        var book = await CreateBookAsync(ana, origins: ["BRA"]);
        await AddAsync(ana, book.Id);
        object Body(string[]? isos) => new { title = book.Title, authorNames = new[] { book.Authors[0].Name }, originCountryIsos = isos };

        (await ana.Client.PutAsJsonAsync($"/api/books/{book.Id}", Body(null), Json)).StatusCode.ShouldBe(HttpStatusCode.OK); // omitted
        (await ana.Client.PutAsJsonAsync($"/api/books/{book.Id}", Body(["BRA"]), Json)).StatusCode.ShouldBe(HttpStatusCode.OK); // same set
        var denied = await ana.Client.PutAsJsonAsync($"/api/books/{book.Id}", Body(["PRT"]), Json);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(denied)).ShouldBe("geo.moderator_required");

        await SetRoleAsync(ana, UserRole.Moderator);
        var applied = await ReadAsync<BookDetailsDto>(await ana.Client.PutAsJsonAsync($"/api/books/{book.Id}", Body(["PRT"]), Json), HttpStatusCode.OK);
        applied.OriginCountries.ShouldHaveSingleItem().CountryIso.ShouldBe("PRT");
    }

    // ------------------------------------------------------------------ citizenships

    [Fact]
    public async Task Author_citizenship_follows_the_same_matrix()
    {
        var ana = await NewUserAsync();
        var book = await CreateBookAsync(ana, origins: null, author: $"Autora {Guid.NewGuid():N}");
        await AddAsync(ana, book.Id);
        var authorId = book.Authors.Single().Id;
        var stranger = await NewUserAsync();

        // not in library
        var notIn = await PutCitizenship(stranger, authorId, "FRA");
        notIn.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(notIn)).ShouldBe("author.not_in_library");

        // first information
        var filled = await ReadAsync<AuthorDetailsDto>(await PutCitizenship(ana, authorId, "fra"), HttpStatusCode.OK);
        var c = filled.Countries.ShouldHaveSingleItem();
        (c.CountryIso, c.Source, c.Confidence, c.IsVerified).ShouldBe(("FRA", GeoSource.Manual, 1m, true));

        // change / removal denied, no-op allowed
        var denied = await PutCitizenship(ana, authorId, "FRA", "BEL");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(denied)).ShouldBe("geo.moderator_required");
        (await PutCitizenship(ana, authorId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PutCitizenship(ana, authorId, "FRA")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // moderator without the book
        await SetRoleAsync(stranger, UserRole.Moderator);
        var edited = await ReadAsync<AuthorDetailsDto>(await PutCitizenship(stranger, authorId, "FRA", "BEL"), HttpStatusCode.OK);
        edited.Countries.Select(x => x.CountryIso).ShouldBe(["BEL", "FRA"], ignoreOrder: true);
    }

    [Fact]
    public async Task Only_citizenship_relations_count_as_the_authors_current_set()
    {
        // An author whose only relation is a BirthPlace has an EMPTY citizenship set: a user may fill it.
        var ana = await NewUserAsync();
        var book = await CreateBookAsync(ana, origins: null);
        await AddAsync(ana, book.Id);
        var authorId = book.Authors.Single().Id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var france = await db.Countries.SingleAsync(x => x.IsoCode == "FRA");
            db.AuthorCountries.Add(new AtlasLiterario.Domain.Entities.AuthorCountry(
                authorId, france.Id, AuthorCountryRelationship.BirthPlace, GeoSource.Wikidata, 0.9m, false, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        (await PutCitizenship(ana, authorId, "BEL")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ role in the database, not in the token

    [Fact]
    public async Task Demotion_is_immediate_and_a_stale_moderator_claim_is_not_trusted()
    {
        var ana = await NewUserAsync();
        var book = await CreateBookAsync(ana, origins: ["BRA"]);
        await AddAsync(ana, book.Id);
        var mod = await NewUserAsync();
        await SetRoleAsync(mod, UserRole.Moderator);

        // Log in again AFTER the promotion: the token now carries role=Moderator.
        var login = await ReadAsync<AuthResponse>(
            await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = mod.Email, password = "segredo123" }), HttpStatusCode.OK);
        login.User.Role.ShouldBe(UserRole.Moderator);
        ClaimOf(login.AccessToken, "role").ShouldBe("Moderator");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var asMod = new Session(client, mod.UserId, mod.Email, login);
        (await PutOrigins(asMod, book.Id, "PRT")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Demoted in the database: the very same token stops working for edits, at once.
        await SetRoleAsync(mod, UserRole.User);
        var denied = await PutOrigins(asMod, book.Id, "ARG");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(denied)).ShouldBe("book.not_in_library"); // no book in library and no longer a moderator
        // and /auth/me reflects the database
        (await ReadAsync<UserProfileDto>(await client.GetAsync("/api/auth/me"), HttpStatusCode.OK)).Role.ShouldBe(UserRole.User);

        // Promoted in the database after login: an old User token works at once, without a new login.
        await SetRoleAsync(mod, UserRole.Moderator);
        (await PutOrigins(asMod, book.Id, "ARG")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Role_is_exposed_in_register_login_and_me()
    {
        var user = await NewUserAsync();
        user.Auth.User.Role.ShouldBe(UserRole.User);
        ClaimOf(user.Auth.AccessToken, "role").ShouldBe("User");
        (await ReadAsync<UserProfileDto>(await user.Client.GetAsync("/api/auth/me"), HttpStatusCode.OK)).Role.ShouldBe(UserRole.User);
        using var raw = JsonDocument.Parse(await (await user.Client.GetAsync("/api/auth/me")).Content.ReadAsStringAsync());
        raw.RootElement.GetProperty("role").GetString().ShouldBe("User"); // JSON string, not a number

        await SetRoleAsync(user, UserRole.Moderator);
        (await ReadAsync<UserProfileDto>(await user.Client.GetAsync("/api/auth/me"), HttpStatusCode.OK)).Role.ShouldBe(UserRole.Moderator);
    }

    [Fact]
    public async Task There_is_no_public_way_to_get_the_moderator_role()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { name = "Espertinha", email = $"x-{Guid.NewGuid():N}@example.com", password = "segredo123", role = "Moderator" });
        var auth = await ReadAsync<AuthResponse>(response, HttpStatusCode.Created);
        auth.User.Role.ShouldBe(UserRole.User);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var update = await client.PutAsJsonAsync("/api/auth/me", new { name = "Espertinha", mapVisibility = "Private", role = "Moderator" }, Json);
        (await ReadAsync<UserProfileDto>(update, HttpStatusCode.OK)).Role.ShouldBe(UserRole.User);
    }

    // ------------------------------------------------------------------ maps follow moderator edits

    [Fact]
    public async Task Maps_are_recalculated_after_a_moderator_edit()
    {
        var ana = await NewUserAsync();
        var book = await CreateBookAsync(ana, origins: ["BRA"]);
        var item = await AddAsync(ana, book.Id);
        await ReadAsync<LibraryItemDto>(await ana.Client.PutAsJsonAsync($"/api/library/{item.Id}", new { status = "Read" }, Json), HttpStatusCode.OK);
        (await ReadAsync<MapDataDto>(await ana.Client.GetAsync("/api/maps/origins"), HttpStatusCode.OK))
            .Countries.ShouldHaveSingleItem().Iso.ShouldBe("BRA");

        var mod = await NewUserAsync();
        await SetRoleAsync(mod, UserRole.Moderator);
        (await PutOrigins(mod, book.Id, "PRT", "AGO")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var authorMod = await PutCitizenship(mod, book.Authors.Single().Id, "PRT");
        authorMod.StatusCode.ShouldBe(HttpStatusCode.OK);

        var origins = await ReadAsync<MapDataDto>(await ana.Client.GetAsync("/api/maps/origins"), HttpStatusCode.OK);
        origins.Countries.Select(c => c.Iso).ShouldBe(["AGO", "PRT"], ignoreOrder: true);
        var authors = await ReadAsync<MapDataDto>(await ana.Client.GetAsync("/api/maps/authors"), HttpStatusCode.OK);
        authors.Countries.ShouldHaveSingleItem().Iso.ShouldBe("PRT");
    }

    private static string ClaimOf(string jwt, string claim)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return doc.RootElement.GetProperty(claim).GetString()!;
    }
}

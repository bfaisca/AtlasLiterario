using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AtlasLiterario.Application.Auth;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasLiterario.API.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class ProfilesEndpointTests(AtlasApiFactory factory) : IClassFixture<AtlasApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private sealed record Session(HttpClient Client, Guid UserId, string Email, string Handle);

    private static string NewHandle() => "u" + Guid.NewGuid().ToString("N")[..12];

    private async Task<Session> UserAsync(string name = "Leitora", MapVisibility visibility = MapVisibility.Private, bool withHandle = true)
    {
        var client = factory.CreateClient();
        var email = $"p-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new { name, email, password = "segredo123" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var handle = withHandle ? NewHandle() : string.Empty;
        if (withHandle || visibility != MapVisibility.Private)
        {
            var update = await client.PutAsJsonAsync("/api/auth/me",
                new { name, mapVisibility = visibility.ToString(), handle = withHandle ? handle : null }, Json);
            update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        }

        return new Session(client, auth.User.Id, email, handle);
    }

    private async Task ShelveAsync(Guid userId, string title, string authorIso, string originIso, ReadingStatus status, DateOnly? finished, string? review = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var countries = await db.Countries.ToDictionaryAsync(c => c.IsoCode, c => c.Id);
        var author = new Author($"Autor {Guid.NewGuid():N}", now);
        var book = new Book(title, now);
        db.Authors.Add(author);
        db.Books.Add(book);
        db.BookAuthors.Add(new BookAuthor(book.Id, author.Id, 0));
        db.AuthorCountries.Add(new AuthorCountry(author.Id, countries[authorIso], AuthorCountryRelationship.Citizenship, GeoSource.Wikidata, 0.9m, false, null, now));
        db.BookOriginCountries.Add(new BookOriginCountry(book.Id, countries[originIso], GeoSource.Wikidata, 0.9m, false, null, now));
        db.UserBooks.Add(new UserBook(userId, book.Id, status, 5, review, null, finished, now));
        await db.SaveChangesAsync();
    }

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private static void AssertNoPrivateData(string rawJson, params string[] forbiddenValues)
    {
        var node = JsonNode.Parse(rawJson)!;
        foreach (var key in AllKeys(node))
        {
            key.ShouldNotBe("email");
            key.ShouldNotBe("id");
            key.ShouldNotBe("userId");
            key.ShouldNotBe("libraryItemId");
            key.ShouldNotBe("startDate");
            key.ShouldNotBe("passwordHash");
        }

        foreach (var value in forbiddenValues)
        {
            rawJson.ShouldNotContain(value);
        }
    }

    private static IEnumerable<string> AllKeys(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (k, v) in o)
                {
                    yield return k;
                    foreach (var inner in AllKeys(v))
                    {
                        yield return inner;
                    }
                }

                break;
            case JsonArray a:
                foreach (var item in a)
                {
                    foreach (var inner in AllKeys(item))
                    {
                        yield return inner;
                    }
                }

                break;
        }
    }

    // ------------------------------------------------------------ auth

    [Theory]
    [InlineData("GET", "/api/profiles?q=ab")]
    [InlineData("GET", "/api/profiles/alguem")]
    [InlineData("POST", "/api/profiles/alguem/follow")]
    [InlineData("DELETE", "/api/profiles/alguem/follow")]
    [InlineData("GET", "/api/profiles/alguem/followers")]
    [InlineData("GET", "/api/profiles/alguem/following")]
    [InlineData("GET", "/api/profiles/alguem/maps/authors")]
    [InlineData("GET", "/api/profiles/alguem/maps/origins")]
    [InlineData("GET", "/api/profiles/alguem/maps/authors/countries/BRA")]
    [InlineData("GET", "/api/me/followers")]
    [InlineData("GET", "/api/me/following")]
    public async Task Every_profile_endpoint_requires_a_token(string method, string url)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------ handle on /auth/me

    [Fact]
    public async Task Handle_flow_through_put_and_get_me()
    {
        var u = await UserAsync(withHandle: false);
        var me = await u.Client.GetFromJsonAsync<JsonNode>("/api/auth/me", Json);
        me!["handle"].ShouldBeNull();
        me["followersCount"]!.GetValue<int>().ShouldBe(0);
        me["followingCount"]!.GetValue<int>().ShouldBe(0);

        var put = await u.Client.PutAsJsonAsync("/api/auth/me", new { name = "Leitora", mapVisibility = "Public", handle = "  Leitora_" + Guid.NewGuid().ToString("N")[..6] + " " }, Json);
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await BodyAsync(put);
        body["mapVisibility"]!.GetValue<string>().ShouldBe("Public");
        var handle = body["handle"]!.GetValue<string>();
        handle.ShouldBe(handle.ToLowerInvariant());
        handle.ShouldStartWith("leitora_");

        // handle omitted keeps it; explicit null with Public is refused; null once Private removes it.
        var kept = await u.Client.PutAsJsonAsync("/api/auth/me", new { name = "Outra", mapVisibility = "Public" }, Json);
        (await BodyAsync(kept))["handle"]!.GetValue<string>().ShouldBe(handle);
        var refused = await u.Client.PutAsJsonAsync("/api/auth/me", new { name = "Outra", mapVisibility = "Public", handle = (string?)null }, Json);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BodyAsync(refused))["code"]!.GetValue<string>().ShouldBe("profile.handle_required");
        var removed = await u.Client.PutAsJsonAsync("/api/auth/me", new { name = "Outra", mapVisibility = "Private", handle = (string?)null }, Json);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(removed))["handle"].ShouldBeNull();
    }

    [Fact]
    public async Task Handle_errors_use_the_contract_codes_and_errors_handle()
    {
        var a = await UserAsync();
        var b = await UserAsync();

        var invalid = await b.Client.PutAsJsonAsync("/api/auth/me", new { name = "B", mapVisibility = "Private", handle = "a b" }, Json);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var invalidBody = await BodyAsync(invalid);
        invalidBody["code"]!.GetValue<string>().ShouldBe("profile.handle_invalid");
        invalidBody["errors"]!["handle"]!.AsArray().Count.ShouldBeGreaterThan(0);

        var reserved = await b.Client.PutAsJsonAsync("/api/auth/me", new { name = "B", mapVisibility = "Private", handle = "Admin" }, Json);
        (await BodyAsync(reserved))["code"]!.GetValue<string>().ShouldBe("profile.handle_invalid");

        var required = await b.Client.PutAsJsonAsync("/api/auth/me", new { name = "B", mapVisibility = "Public", handle = "" }, Json);
        required.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BodyAsync(required))["code"]!.GetValue<string>().ShouldBe("profile.handle_required");

        var taken = await b.Client.PutAsJsonAsync("/api/auth/me", new { name = "B", mapVisibility = "Private", handle = a.Handle.ToUpperInvariant() }, Json);
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BodyAsync(taken))["code"]!.GetValue<string>().ShouldBe("profile.handle_taken");
    }

    [Fact]
    public async Task Existing_clients_that_send_no_handle_still_work_and_unknown_visibility_is_rejected()
    {
        var u = await UserAsync();

        var ok = await u.Client.PutAsJsonAsync("/api/auth/me", new { name = "Novo nome", mapVisibility = "LinkOnly" }, Json);

        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(ok))["handle"]!.GetValue<string>().ShouldBe(u.Handle);
        (await u.Client.PutAsJsonAsync("/api/auth/me", new { name = "X", mapVisibility = "Todos" }, Json))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Auth_responses_carry_handle_and_counts()
    {
        var a = await UserAsync();
        var b = await UserAsync(visibility: MapVisibility.Public);
        (await a.Client.PostAsync($"/api/profiles/{b.Handle}/follow", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = b.Email, password = "segredo123" });
        var user = (await BodyAsync(login))["user"]!;

        user["handle"]!.GetValue<string>().ShouldBe(b.Handle);
        user["followersCount"]!.GetValue<int>().ShouldBe(1);
        user["followingCount"]!.GetValue<int>().ShouldBe(0);
    }

    // ------------------------------------------------------------ search

    [Fact]
    public async Task Search_finds_only_public_profiles_without_leaking_private_data()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var seeker = await UserAsync();
        var pub = await UserAsync($"Zélia {token}", MapVisibility.Public);
        await UserAsync($"Zélia Privada {token}", MapVisibility.Private);
        await UserAsync($"Zélia Link {token}", MapVisibility.LinkOnly);
        await ShelveAsync(pub.UserId, "Livro", "BRA", "BRA", ReadingStatus.Read, new DateOnly(2025, 1, 1));

        var response = await seeker.Client.GetAsync($"/api/profiles?q=zelia%20{token}");
        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = JsonNode.Parse(raw)!.AsArray();
        items.Count.ShouldBe(1);
        items[0]!["handle"]!.GetValue<string>().ShouldBe(pub.Handle);
        items[0]!["readBooksCount"]!.GetValue<int>().ShouldBe(1);
        AssertNoPrivateData(raw, pub.Email, pub.UserId.ToString());

        // The caller never finds themselves.
        (await seeker.Client.GetStringAsync($"/api/profiles?q={seeker.Handle}")).ShouldBe("[]");
    }

    [Theory]
    [InlineData("/api/profiles", "q")]
    [InlineData("/api/profiles?q=a", "q")]
    [InlineData("/api/profiles?q=%20a%20", "q")]
    [InlineData("/api/profiles?q=ab&limit=0", "limit")]
    [InlineData("/api/profiles?q=ab&limit=21", "limit")]
    public async Task Search_validates_query_and_limit(string url, string field)
    {
        var u = await UserAsync();

        var response = await u.Client.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await BodyAsync(response);
        body["code"]!.GetValue<string>().ShouldBe("validation.failed");
        body["errors"]![field].ShouldNotBeNull();
    }

    [Fact]
    public async Task Search_is_rate_limited_per_user()
    {
        var limited = await UserAsync();
        var other = await UserAsync();

        for (var i = 0; i < 30; i++)
        {
            (await limited.Client.GetAsync("/api/profiles?q=zz")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var blocked = await limited.Client.GetAsync("/api/profiles?q=zz");
        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        blocked.Headers.Contains("Retry-After").ShouldBeTrue();
        (await BodyAsync(blocked))["code"]!.GetValue<string>().ShouldBe("rate_limit.exceeded");

        (await other.Client.GetAsync("/api/profiles?q=zz")).StatusCode.ShouldBe(HttpStatusCode.OK); // another user is unaffected
        (await limited.Client.GetAsync($"/api/profiles/{other.Handle}")).StatusCode.ShouldBe(HttpStatusCode.NotFound); // other routes are not limited
    }

    // ------------------------------------------------------------ profile + privacy

    [Fact]
    public async Task Private_and_unknown_profiles_return_the_same_404_and_the_owner_still_reads_their_own()
    {
        var viewer = await UserAsync();
        var hidden = await UserAsync(visibility: MapVisibility.Private);
        var link = await UserAsync(visibility: MapVisibility.LinkOnly);

        var privateResponse = await viewer.Client.GetAsync($"/api/profiles/{hidden.Handle}");
        var linkResponse = await viewer.Client.GetAsync($"/api/profiles/{link.Handle}");
        var unknownResponse = await viewer.Client.GetAsync("/api/profiles/ninguem_aqui");

        foreach (var response in new[] { privateResponse, linkResponse, unknownResponse })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var shapes = new List<string>();
        foreach (var response in new[] { privateResponse, linkResponse, unknownResponse })
        {
            var body = await BodyAsync(response);
            body.AsObject().Remove("traceId");
            body.AsObject().Remove("instance");
            shapes.Add(body.ToJsonString());
        }

        shapes.Distinct().Count().ShouldBe(1);
        JsonNode.Parse(shapes[0])!["code"]!.GetValue<string>().ShouldBe("profile.not_found");

        var own = await hidden.Client.GetAsync($"/api/profiles/{hidden.Handle}");
        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(own))["isSelf"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Profile_header_and_maps_never_expose_non_read_books_or_private_data()
    {
        var viewer = await UserAsync();
        var owner = await UserAsync("Dona Pública", MapVisibility.Public);
        await ShelveAsync(owner.UserId, "Lido", "BRA", "PRT", ReadingStatus.Read, new DateOnly(2025, 1, 10), review: "Resenha pública");
        await ShelveAsync(owner.UserId, "Em andamento", "JPN", "JPN", ReadingStatus.Reading, null, review: "SEGREDO-reading");
        await ShelveAsync(owner.UserId, "Desejado", "FRA", "FRA", ReadingStatus.WantToRead, null, review: "SEGREDO-want");
        await ShelveAsync(owner.UserId, "Abandonado", "ARG", "ARG", ReadingStatus.Abandoned, null, review: "SEGREDO-abandoned");
        string[] secrets = [owner.Email, owner.UserId.ToString(), "SEGREDO", "Em andamento", "Desejado", "Abandonado"];

        var headerRaw = await viewer.Client.GetStringAsync($"/api/profiles/{owner.Handle}");
        var header = JsonNode.Parse(headerRaw)!;
        header["readBooksCount"]!.GetValue<int>().ShouldBe(1);
        header["exploredCountriesAuthors"]!.GetValue<int>().ShouldBe(1);
        header["exploredCountriesOrigins"]!.GetValue<int>().ShouldBe(1);
        header["isSelf"]!.GetValue<bool>().ShouldBeFalse();
        AssertNoPrivateData(headerRaw, secrets);

        foreach (var type in new[] { "authors", "origins" })
        {
            var mapRaw = await viewer.Client.GetStringAsync($"/api/profiles/{owner.Handle}/maps/{type}");
            AssertNoPrivateData(mapRaw, secrets);
            JsonNode.Parse(mapRaw)!["status"]!.GetValue<string>().ShouldBe("Read");

            // status is not a parameter here: the viewer can never widen the set.
            var forced = await viewer.Client.GetStringAsync($"/api/profiles/{owner.Handle}/maps/{type}?status=All");
            JsonNode.Parse(forced)!["totalReadBooks"]!.GetValue<int>().ShouldBe(1);
        }

        var panelRaw = await viewer.Client.GetStringAsync($"/api/profiles/{owner.Handle}/maps/authors/countries/BRA");
        var panel = JsonNode.Parse(panelRaw)!;
        panel["books"]![0]!["review"]!.GetValue<string>().ShouldBe("Resenha pública");
        panel["books"]![0]!["rating"]!.GetValue<int>().ShouldBe(5);
        panel["authors"]![0]!["books"]![0]!["review"]!.GetValue<string>().ShouldBe("Resenha pública");
        panel["status"]!.GetValue<string>().ShouldBe("Read");
        AssertNoPrivateData(panelRaw, secrets.Where(s => s != "Lido").ToArray());

        var jpn = JsonNode.Parse(await viewer.Client.GetStringAsync($"/api/profiles/{owner.Handle}/maps/authors/countries/JPN"))!;
        jpn["bookCount"]!.GetValue<int>().ShouldBe(0);
        jpn["books"]!.AsArray().Count.ShouldBe(0);

        var origins = JsonNode.Parse(await viewer.Client.GetStringAsync($"/api/profiles/{owner.Handle}/maps/origins/countries/PRT"))!;
        origins["authors"]!.AsArray().Count.ShouldBe(0);
        origins["books"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Profile_maps_match_the_owners_own_map_and_the_review_exists_only_on_the_profile_endpoint()
    {
        var viewer = await UserAsync();
        var owner = await UserAsync(visibility: MapVisibility.Public);
        await ShelveAsync(owner.UserId, "Um", "BRA", "BRA", ReadingStatus.Read, new DateOnly(2025, 1, 10), review: "minha-resenha");
        await ShelveAsync(owner.UserId, "Dois", "BRA", "PRT", ReadingStatus.Read, new DateOnly(2025, 2, 10));

        foreach (var type in new[] { "authors", "origins" })
        {
            var own = JsonNode.Parse(await owner.Client.GetStringAsync($"/api/maps/{type}"))!;
            var theirs = JsonNode.Parse(await viewer.Client.GetStringAsync($"/api/profiles/{owner.Handle}/maps/{type}"))!;
            theirs.ToJsonString().ShouldBe(own.ToJsonString());
        }

        // The private panel endpoint never carried the review; the profile panel does.
        (await owner.Client.GetStringAsync("/api/maps/authors/countries/BRA")).ShouldNotContain("minha-resenha");
        (await viewer.Client.GetStringAsync($"/api/profiles/{owner.Handle}/maps/authors/countries/BRA")).ShouldContain("minha-resenha");
    }

    [Fact]
    public async Task Profile_map_endpoints_404_for_private_owners_even_with_an_invalid_iso_and_validate_iso_for_public_ones()
    {
        var viewer = await UserAsync();
        var hidden = await UserAsync(visibility: MapVisibility.Private);
        var open = await UserAsync(visibility: MapVisibility.Public);

        (await viewer.Client.GetAsync($"/api/profiles/{hidden.Handle}/maps/authors")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await viewer.Client.GetAsync($"/api/profiles/{hidden.Handle}/maps/authors/countries/ZZZ")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var bad = await viewer.Client.GetAsync($"/api/profiles/{open.Handle}/maps/authors/countries/ZZZ");
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BodyAsync(bad))["code"]!.GetValue<string>().ShouldBe("country.unknown_iso");
        (await viewer.Client.GetAsync($"/api/profiles/{open.Handle}/maps/continents")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------ follow

    [Fact]
    public async Task Follow_flow_updates_counts_flags_and_lists_and_is_idempotent()
    {
        var ana = await UserAsync("Ana Seguidora");
        var bia = await UserAsync("Bia Pública", MapVisibility.Public);
        ana.Client.DefaultRequestHeaders.Authorization.ShouldNotBeNull();

        // Ana is private (but has a handle): she can still follow a public profile.
        (await ana.Client.PostAsync($"/api/profiles/{bia.Handle}/follow", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ana.Client.PostAsync($"/api/profiles/{bia.Handle.ToUpperInvariant()}/follow", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var profile = (await ana.Client.GetFromJsonAsync<JsonNode>($"/api/profiles/{bia.Handle}", Json))!;
        profile["followersCount"]!.GetValue<int>().ShouldBe(1);
        profile["isFollowing"]!.GetValue<bool>().ShouldBeTrue();
        profile["followsYou"]!.GetValue<bool>().ShouldBeFalse();

        // Bia's own lists: Ana is private so she is omitted from Bia's /me/followers, but counted.
        var biasFollowers = (await bia.Client.GetFromJsonAsync<JsonNode>("/api/me/followers", Json))!;
        biasFollowers["items"]!.AsArray().Count.ShouldBe(0);
        (await bia.Client.GetFromJsonAsync<JsonNode>("/api/auth/me", Json))!["followersCount"]!.GetValue<int>().ShouldBe(1);

        // Ana opts in publicly; now she appears, and Bia can follow her back.
        (await ana.Client.PutAsJsonAsync("/api/auth/me", new { name = "Ana Seguidora", mapVisibility = "Public" }, Json)).StatusCode.ShouldBe(HttpStatusCode.OK);
        biasFollowers = (await bia.Client.GetFromJsonAsync<JsonNode>("/api/me/followers", Json))!;
        biasFollowers["items"]!.AsArray().Select(i => i!["handle"]!.GetValue<string>()).ShouldBe([ana.Handle]);
        biasFollowers["items"]![0]!["isFollowing"]!.GetValue<bool>().ShouldBeFalse();
        (await bia.Client.PostAsync($"/api/profiles/{ana.Handle}/follow", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        biasFollowers = (await bia.Client.GetFromJsonAsync<JsonNode>("/api/me/followers", Json))!;
        biasFollowers["items"]![0]!["isFollowing"]!.GetValue<bool>().ShouldBeTrue();
        biasFollowers["items"]![0]!["followsYou"]!.GetValue<bool>().ShouldBeTrue();
        biasFollowers["totalCount"]!.GetValue<int>().ShouldBe(1);

        var raw = await ana.Client.GetStringAsync($"/api/profiles/{bia.Handle}/followers?page=1&pageSize=5");
        AssertNoPrivateData(raw, ana.Email, bia.Email);
        var page = JsonNode.Parse(raw)!;
        page["page"]!.GetValue<int>().ShouldBe(1);
        page["pageSize"]!.GetValue<int>().ShouldBe(5);
        page["items"]![0]!["handle"]!.GetValue<string>().ShouldBe(ana.Handle);
        (await ana.Client.GetFromJsonAsync<JsonNode>($"/api/profiles/{ana.Handle}/following", Json))!["items"]![0]!["handle"]!
            .GetValue<string>().ShouldBe(bia.Handle);

        // Unfollow twice: always 204; counts return to zero.
        (await ana.Client.DeleteAsync($"/api/profiles/{bia.Handle}/follow")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ana.Client.DeleteAsync($"/api/profiles/{bia.Handle}/follow")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ana.Client.DeleteAsync("/api/profiles/handle_que_nao_existe/follow")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ana.Client.GetFromJsonAsync<JsonNode>($"/api/profiles/{bia.Handle}", Json))!["followersCount"]!.GetValue<int>().ShouldBe(0);
    }

    [Fact]
    public async Task Follow_rules_self_private_and_unknown()
    {
        var me = await UserAsync(visibility: MapVisibility.Private);
        var hidden = await UserAsync(visibility: MapVisibility.Private);

        var self = await me.Client.PostAsync($"/api/profiles/{me.Handle}/follow", null);
        self.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BodyAsync(self))["code"]!.GetValue<string>().ShouldBe("follow.self");

        var privateTarget = await me.Client.PostAsync($"/api/profiles/{hidden.Handle}/follow", null);
        privateTarget.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BodyAsync(privateTarget))["code"]!.GetValue<string>().ShouldBe("profile.not_found");
        (await me.Client.PostAsync("/api/profiles/nao_existe_mesmo/follow", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await me.Client.GetFromJsonAsync<JsonNode>("/api/auth/me", Json))!["followingCount"]!.GetValue<int>().ShouldBe(0);
    }

    [Fact]
    public async Task Followers_list_is_paginated_and_third_party_lists_omit_non_public_accounts()
    {
        var star = await UserAsync(visibility: MapVisibility.Public);
        var fans = new List<Session>();
        for (var i = 0; i < 3; i++)
        {
            var fan = await UserAsync(visibility: MapVisibility.Public);
            fans.Add(fan);
            (await fan.Client.PostAsync($"/api/profiles/{star.Handle}/follow", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var quiet = await UserAsync(visibility: MapVisibility.Private);
        (await quiet.Client.PostAsync($"/api/profiles/{star.Handle}/follow", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var viewer = await UserAsync();

        var first = (await viewer.Client.GetFromJsonAsync<JsonNode>($"/api/profiles/{star.Handle}/followers?page=1&pageSize=2", Json))!;
        var second = (await viewer.Client.GetFromJsonAsync<JsonNode>($"/api/profiles/{star.Handle}/followers?page=2&pageSize=2", Json))!;

        first["items"]!.AsArray().Count.ShouldBe(2);
        second["items"]!.AsArray().Count.ShouldBe(1);
        first["totalCount"]!.GetValue<int>().ShouldBe(3);
        first["totalPages"]!.GetValue<int>().ShouldBe(2);
        var handles = first["items"]!.AsArray().Concat(second["items"]!.AsArray()).Select(i => i!["handle"]!.GetValue<string>()).ToList();
        handles.ShouldNotContain(quiet.Handle);
        handles.ShouldBe(fans.Select(f => f.Handle).ToList(), ignoreOrder: true);
        (await viewer.Client.GetFromJsonAsync<JsonNode>($"/api/profiles/{star.Handle}", Json))!["followersCount"]!.GetValue<int>().ShouldBe(4);
    }

    // ------------------------------------------------------------ share + OpenAPI

    [Fact]
    public async Task Share_requires_a_non_private_profile()
    {
        var u = await UserAsync(visibility: MapVisibility.Private);

        var blocked = await u.Client.PostAsJsonAsync("/api/maps/share", new { mapType = "Authors" }, Json);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await BodyAsync(blocked))["code"]!.GetValue<string>().ShouldBe("share.private_profile");

        foreach (var visibility in new[] { "LinkOnly", "Public" })
        {
            (await u.Client.PutAsJsonAsync("/api/auth/me", new { name = "Leitora", mapVisibility = visibility }, Json)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await u.Client.PostAsJsonAsync("/api/maps/share", new { mapType = "Authors" }, Json)).StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        }
    }

    [Fact]
    public async Task OpenApi_documents_profile_endpoints_and_schemas()
    {
        var document = JsonNode.Parse(await factory.CreateClient().GetStringAsync("/openapi/v1.json"))!;
        var paths = document["paths"]!;
        foreach (var (path, method) in new[]
                 {
                     ("/api/profiles", "get"), ("/api/profiles/{handle}", "get"), ("/api/profiles/{handle}/follow", "post"),
                     ("/api/profiles/{handle}/follow", "delete"), ("/api/profiles/{handle}/followers", "get"),
                     ("/api/profiles/{handle}/following", "get"), ("/api/profiles/{handle}/maps/{type}", "get"),
                     ("/api/profiles/{handle}/maps/{type}/countries/{iso}", "get"), ("/api/me/followers", "get"), ("/api/me/following", "get"),
                 })
        {
            paths[path]![method].ShouldNotBeNull($"{method} {path}");
        }

        var schemas = document["components"]!["schemas"]!;
        PropertyNames(schemas["ProfileSummaryDto"]!).ShouldBe(["handle", "name", "followersCount", "readBooksCount", "isFollowing", "followsYou"], ignoreOrder: true);
        PropertyNames(schemas["ProfileDto"]!).ShouldBe(
            ["handle", "name", "followersCount", "readBooksCount", "isFollowing", "followsYou", "followingCount",
             "exploredCountriesAuthors", "exploredCountriesOrigins", "memberSince", "isSelf"], ignoreOrder: true);
        PropertyNames(schemas["ProfileBookDto"]!).ShouldBe(["bookId", "title", "coverUrl", "authors", "finishedDate", "rating", "review"], ignoreOrder: true);
        PropertyNames(schemas["ProfileCountryAuthorDto"]!).ShouldBe(["authorId", "name", "countryIso", "countryName", "books"], ignoreOrder: true);
        PropertyNames(schemas["ProfileCountryDetailDto"]!).ShouldBe(
            ["mapType", "iso", "name", "continent", "bookCount", "authorCount", "books", "authors", "status"], ignoreOrder: true);
        PropertyNames(schemas["UserProfileDto"]!).ShouldContain("handle");
        PropertyNames(schemas["UserProfileDto"]!).ShouldContain("followersCount");
        PropertyNames(schemas["UserProfileDto"]!).ShouldContain("followingCount");
        PropertyNames(schemas["UpdateProfileRequest"]!).ShouldBe(["name", "mapVisibility", "handle"], ignoreOrder: true);
        schemas["MapVisibility"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["Private", "LinkOnly", "Public"]);
        paths["/api/profiles"]!["get"]!["responses"]!["429"].ShouldNotBeNull();
        paths["/api/auth/me"]!["put"]!["responses"]!["409"].ShouldNotBeNull();
    }

    private static IEnumerable<string> PropertyNames(JsonNode schema) => schema["properties"]!.AsObject().Select(p => p.Key);
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasLiterario.Application.Auth;
using AtlasLiterario.Application.Authors;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasLiterario.API.IntegrationTests;

/// <summary>v1.11: GET /authors/duplicates and POST /authors/merge (Moderator only) over HTTP.</summary>
[Trait("Category", "Integration")]
public sealed class AuthorMergeEndpointTests(AtlasApiFactory factory) : IClassFixture<AtlasApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<(HttpClient Client, Guid UserId)> UserAsync(UserRole role)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { name = "Pessoa", email = $"m-{Guid.NewGuid():N}@example.com", password = "segredo123" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        if (role != UserRole.User)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(u => u.Id == auth.User.Id)).SetRole(role, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        return (client, auth.User.Id);
    }

    private async Task<(Guid Target, Guid Source, string Name)> SeedDuplicatesAsync()
    {
        var name = $"Autora {Guid.NewGuid():N}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var target = new Author(name, now);
        var source = new Author(name, now.AddMinutes(1));
        var book = new Book("Livro", now);
        db.Authors.AddRange(target, source);
        db.Books.Add(book);
        db.BookAuthors.Add(new BookAuthor(book.Id, source.Id, 0));
        await db.SaveChangesAsync();
        return (target.Id, source.Id, name);
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    [Fact]
    public async Task Common_user_gets_403_with_code_and_anonymous_gets_401()
    {
        var (user, _) = await UserAsync(UserRole.User);
        var (target, source, _) = await SeedDuplicatesAsync();

        var list = await user.GetAsync("/api/authors/duplicates");
        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(list)).ShouldBe("author.moderator_required");

        var merge = await user.PostAsJsonAsync("/api/authors/merge", new { targetId = target, sourceIds = new[] { source } });
        merge.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(merge)).ShouldBe("author.moderator_required");

        (await factory.CreateClient().GetAsync("/api/authors/duplicates")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Moderator_lists_duplicates_merges_them_and_gets_the_contract_errors()
    {
        var (moderator, _) = await UserAsync(UserRole.Moderator);
        var (target, source, name) = await SeedDuplicatesAsync();

        var groups = (await moderator.GetFromJsonAsync<List<AuthorDuplicateGroupDto>>("/api/authors/duplicates", Json))!;
        var group = groups.Single(g => g.NormalizedName == Author.NormalizeName(name));
        group.Authors.Select(a => a.Id).ShouldBe([source, target]); // the one with a book comes first

        var invalid = await moderator.PostAsJsonAsync("/api/authors/merge", new { targetId = target, sourceIds = new[] { target } });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(invalid)).ShouldBe("validation.failed");
        JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").TryGetProperty("sourceIds", out _)
            .ShouldBeTrue();

        var missing = await moderator.PostAsJsonAsync("/api/authors/merge", new { targetId = target, sourceIds = new[] { Guid.NewGuid() } });
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(missing)).ShouldBe("author.not_found");

        var ok = await moderator.PostAsJsonAsync("/api/authors/merge", new { targetId = target, sourceIds = new[] { source } });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var result = (await ok.Content.ReadFromJsonAsync<AuthorMergeResultDto>(Json))!;
        result.ShouldBe(new AuthorMergeResultDto(target, 1, 1, 0, 0));

        var again = await moderator.PostAsJsonAsync("/api/authors/merge", new { targetId = target, sourceIds = new[] { source } });
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(again)).ShouldBe("author.not_found");
    }
}

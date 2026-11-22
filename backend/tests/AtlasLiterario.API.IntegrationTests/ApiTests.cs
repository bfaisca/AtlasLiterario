using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AtlasLiterario.Application.Auth;
using AtlasLiterario.Application.Countries;

namespace AtlasLiterario.API.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class ApiTests(AtlasApiFactory factory) : IClassFixture<AtlasApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<AuthResponse> RegisterAsync(string email)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/register", new { name = "Ana", email, password = "segredo123" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
    }

    [Fact]
    public async Task Health_endpoint_is_public()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Register_login_and_me_flow()
    {
        var email = $"ana-{Guid.NewGuid():N}@example.com";
        var registered = await RegisterAsync(email);
        registered.RefreshToken.Length.ShouldBe(43);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "segredo123" });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = (await login.Content.ReadFromJsonAsync<AuthResponse>(Json))!;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var me = await _client.SendAsync(request);

        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonNode.Parse(await me.Content.ReadAsStringAsync())!;
        body["email"]!.GetValue<string>().ShouldBe(email);
        body["mapVisibility"]!.GetValue<string>().ShouldBe("Private"); // enums as strings
        body["createdAt"]!.GetValue<string>().ShouldEndWith("Z");      // UTC instants with Z
    }

    [Fact]
    public async Task Duplicate_registration_returns_problem_details_with_code()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(email);

        var response = await _client.PostAsJsonAsync(
            "/api/auth/register", new { name = "Ana", email, password = "segredo123" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem["code"]!.GetValue<string>().ShouldBe("auth.email_taken");
        problem["status"]!.GetValue<int>().ShouldBe(409);
    }

    [Fact]
    public async Task Invalid_credentials_return_401_with_code()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", new { email = "nobody@example.com", password = "segredo123" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>()
            .ShouldBe("auth.invalid_credentials");
    }

    [Fact]
    public async Task Validation_errors_return_400_with_field_errors()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/register", new { name = "Ana", email = "ana@example.com", password = "curta" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem["code"]!.GetValue<string>().ShouldBe("validation.failed");
        problem["errors"]!["password"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Missing_body_fields_return_400_with_code()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "ana@example.com" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>()
            .ShouldBe("validation.failed");
    }

    [Fact]
    public async Task Private_endpoints_require_a_token()
    {
        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>()
            .ShouldBe("auth.unauthorized");
    }

    [Fact]
    public async Task Countries_are_public_and_complete()
    {
        var countries = await _client.GetFromJsonAsync<List<CountryDto>>("/api/countries", Json);

        countries!.Count.ShouldBe(250);
        countries.Single(c => c.Iso == "JPN").NamePt.ShouldBe("Japão");
    }

    [Fact]
    public async Task Not_yet_implemented_contract_endpoints_return_501_problem()
    {
        var session = await RegisterAsync($"stub-{Guid.NewGuid():N}@example.com");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/maps/share"); // Etapa 6
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>()
            .ShouldBe("feature.not_implemented");
    }

    [Fact]
    public async Task OpenApi_document_exposes_contract_with_bearer_scheme_and_string_enums()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        document["components"]!["securitySchemes"]!["Bearer"].ShouldNotBeNull();
        document["paths"]!["/api/library"].ShouldNotBeNull();
        document["paths"]!["/api/public/maps/{token}"].ShouldNotBeNull();
        var statusEnum = document["components"]!["schemas"]!["ReadingStatus"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>());
        statusEnum.ShouldBe(["WantToRead", "Reading", "Read", "Abandoned"]);
    }
}

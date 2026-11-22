using AtlasLiterario.Application.Authors;
using AtlasLiterario.Application.Tests.Fakes;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Tests.Authors;

public sealed class AuthorSearchTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2025, 3, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDb.Create();
    private readonly FakeCurrentUser _user = new() { UserId = Guid.NewGuid() };
    private Dictionary<string, short> _countryIds = [];

    public async Task InitializeAsync()
    {
        await _db.Database.EnsureCreatedAsync();
        _countryIds = await _db.Countries.ToDictionaryAsync(c => c.IsoCode, c => c.Id);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private AuthorService Service() => new(_db, _user, new FakeClock());

    private Author Add(string name, params (string Iso, AuthorCountryRelationship Relation)[] countries)
    {
        var author = new Author(name, Now);
        _db.Authors.Add(author);
        foreach (var (iso, relation) in countries)
        {
            _db.AuthorCountries.Add(new AuthorCountry(author.Id, _countryIds[iso], relation, GeoSource.Wikidata, 0.9m, false, null, Now));
        }

        return author;
    }

    [Fact]
    public async Task Matches_without_accent_or_case_by_prefix_of_the_name_or_of_any_word()
    {
        Add("Gabriel García Márquez");
        Add("Gabriela Mistral");
        Add("Mario Vargas Llosa");
        Add("Margarida Garcia");
        await _db.SaveChangesAsync();

        (await Service().SearchAsync("garcía", null, default)).Select(a => a.Name)
            .ShouldBe(["Margarida Garcia", "Gabriel García Márquez"], ignoreOrder: true);
        (await Service().SearchAsync("  GABRIEL ", null, default)).Select(a => a.Name)
            .ShouldBe(["Gabriel García Márquez", "Gabriela Mistral"]); // whole-name prefix, then name order
        (await Service().SearchAsync("marq", null, default)).ShouldHaveSingleItem().Name.ShouldBe("Gabriel García Márquez");
        (await Service().SearchAsync("arcia", null, default)).ShouldBeEmpty(); // infix is not a prefix
    }

    [Fact]
    public async Task Whole_name_prefix_comes_first_then_alphabetical()
    {
        Add("Zeca Lima");
        Add("Ana Lima");
        Add("Lima Barreto");
        Add("Lia Lima");
        await _db.SaveChangesAsync();

        (await Service().SearchAsync("lima", null, default)).Select(a => a.Name)
            .ShouldBe(["Lima Barreto", "Ana Lima", "Lia Lima", "Zeca Lima"]);
    }

    [Fact]
    public async Task Limit_defaults_to_ten_and_is_applied()
    {
        for (var i = 0; i < 12; i++)
        {
            Add($"Autor {i:00}");
        }

        await _db.SaveChangesAsync();

        (await Service().SearchAsync("autor", null, default)).Count.ShouldBe(10);
        (await Service().SearchAsync("autor", 3, default)).Select(a => a.Name).ShouldBe(["Autor 00", "Autor 01", "Autor 02"]);
        (await Service().SearchAsync("autor", 20, default)).Count.ShouldBe(12);
    }

    [Theory]
    [InlineData(null, null, "q")]
    [InlineData("", null, "q")]
    [InlineData(" a ", null, "q")]
    [InlineData("ab", 0, "limit")]
    [InlineData("ab", 21, "limit")]
    [InlineData("ab", -1, "limit")]
    public async Task Invalid_query_or_limit_is_a_validation_error_on_that_field(string? q, int? limit, string field)
    {
        var ex = await Should.ThrowAsync<ValidationException>(() => Service().SearchAsync(q, limit, default));

        ex.Code.ShouldBe("validation.failed");
        ex.Errors.ShouldContainKey(field);
    }

    [Fact]
    public async Task Both_fields_are_reported_together()
    {
        var ex = await Should.ThrowAsync<ValidationException>(() => Service().SearchAsync("a", 99, default));

        ex.Errors.Keys.ShouldBe(["q", "limit"], ignoreOrder: true);
    }

    [Fact]
    public async Task Like_wildcards_in_the_term_are_literal()
    {
        Add("100% Autor");
        Add("Ana_Maria");
        Add("Anaxmaria");
        Add("Carlos");
        await _db.SaveChangesAsync();

        (await Service().SearchAsync("%%", null, default)).ShouldBeEmpty();
        (await Service().SearchAsync("__", null, default)).ShouldBeEmpty();
        (await Service().SearchAsync("ana_", null, default)).ShouldHaveSingleItem().Name.ShouldBe("Ana_Maria");
        (await Service().SearchAsync("100%", null, default)).ShouldHaveSingleItem().Name.ShouldBe("100% Autor");
        (await Service().SearchAsync("a\\", null, default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task No_match_returns_an_empty_list()
    {
        Add("Machado de Assis");
        await _db.SaveChangesAsync();

        (await Service().SearchAsync("zzz", null, default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Nationalities_are_citizenship_only_in_portuguese_with_a_stable_order()
    {
        var kafka = Add("Franz Kafka", ("CZE", AuthorCountryRelationship.Citizenship), ("AUT", AuthorCountryRelationship.Citizenship),
            ("DEU", AuthorCountryRelationship.BirthPlace));
        Add("Franz Unknown", ("BRA", AuthorCountryRelationship.Residence));
        Add("Franz Nothing");
        await _db.SaveChangesAsync();

        var result = await Service().SearchAsync("franz", null, default);

        result.Select(a => a.Name).ShouldBe(["Franz Kafka", "Franz Nothing", "Franz Unknown"]);
        var found = result[0];
        found.Id.ShouldBe(kafka.Id);
        found.NationalityIsos.ShouldBe(["AUT", "CZE"]);
        found.NationalityNames.ShouldBe(["Áustria", "Tchéquia"]);
        result[1].NationalityIsos.ShouldBeEmpty();
        result[2].NationalityIsos.ShouldBeEmpty();
        result[2].NationalityNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task Requires_an_authenticated_user()
    {
        _user.UserId = null;

        await Should.ThrowAsync<UnauthorizedException>(() => Service().SearchAsync("ab", null, default));
    }
}

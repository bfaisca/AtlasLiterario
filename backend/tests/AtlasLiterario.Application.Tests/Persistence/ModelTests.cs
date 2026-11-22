using AtlasLiterario.Application.Countries;
using AtlasLiterario.Application.Tests.Fakes;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AtlasLiterario.Application.Tests.Persistence;

public sealed class ModelTests
{
    [Fact]
    public async Task Seed_contains_all_iso_countries_with_unique_codes()
    {
        await using var db = TestDb.Create();
        await db.Database.EnsureCreatedAsync();

        var countries = await new CountryService(db).GetAllAsync(CancellationToken.None);

        countries.Count.ShouldBe(250); // 249 ISO 3166-1 + Kosovo (XKX)
        countries.Select(c => c.Iso).Distinct().Count().ShouldBe(250);
        countries.ShouldAllBe(c => c.Iso.Length == 3 && c.Iso2.Length == 2 && c.GeoJsonId == c.Iso);
        countries.Single(c => c.Iso == "BRA").NamePt.ShouldBe("Brasil");
    }

    [Fact]
    public async Task Historical_mappings_point_to_seeded_countries()
    {
        await using var db = TestDb.Create();
        await db.Database.EnsureCreatedAsync();

        var mappings = await db.HistoricalCountryMappings.Include(m => m.Country).ToListAsync();

        mappings.ShouldNotBeEmpty();
        mappings.ShouldAllBe(m => m.Country != null);
        mappings.Single(m => m.WikidataQid == "Q28513").Country!.IsoCode.ShouldBe("AUT");
    }

    [Theory]
    [InlineData("IsoCode2", "iso_code2")]
    [InlineData("GeoJsonId", "geo_json_id")]
    [InlineData("Isbn13", "isbn13")]
    [InlineData("WikidataQid", "wikidata_qid")]
    [InlineData("UpdatedByUserId", "updated_by_user_id")]
    public void Snake_case_conversion(string input, string expected)
    {
        AppDbContext.ToSnakeCase(input).ShouldBe(expected);
    }

    [Fact]
    public void Relational_model_uses_snake_case_tables_and_columns()
    {
        // Npgsql provider without a connection: only the model is built.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options;
        using var db = new AppDbContext(options);

        var userBook = db.Model.FindEntityType(typeof(UserBook))!;
        userBook.GetTableName().ShouldBe("user_book");
        userBook.FindProperty(nameof(UserBook.FinishedDate))!.GetColumnName().ShouldBe("finished_date");
        db.Model.FindEntityType(typeof(User))!.GetTableName().ShouldBe("user");
    }

    [Fact]
    public void Profile_and_follow_schema_has_unique_handle_composite_key_check_and_cascades()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options;
        using var db = new AppDbContext(options);

        var user = db.Model.FindEntityType(typeof(User))!;
        var handle = user.FindProperty(nameof(User.Handle))!;
        handle.GetColumnName().ShouldBe("handle");
        handle.GetMaxLength().ShouldBe(30);
        handle.IsNullable.ShouldBeTrue();
        user.GetIndexes().Single(i => i.Properties.Single().Name == nameof(User.Handle)).IsUnique.ShouldBeTrue();

        var follow = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(UserFollow))!;
        follow.GetTableName().ShouldBe("user_follow");
        follow.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName()).ShouldBe(["follower_id", "followed_id"]);
        follow.GetCheckConstraints().Single().Sql.ShouldBe("follower_id <> followed_id");
        follow.GetIndexes().Single().Properties.Single().Name.ShouldBe(nameof(UserFollow.FollowedId));
        follow.GetForeignKeys().Count().ShouldBe(2);
        follow.GetForeignKeys().ShouldAllBe(f => f.DeleteBehavior == DeleteBehavior.Cascade);
    }
}

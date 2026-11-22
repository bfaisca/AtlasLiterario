using System.Reflection;
using AtlasLiterario.Application.Maps;
using AtlasLiterario.Application.Profiles;
using AtlasLiterario.Application.Statistics;
using AtlasLiterario.Application.Tests.Fakes;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Tests.Profiles;

public sealed class ProfileServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2025, 3, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDb.Create();
    private readonly FakeCurrentUser _me = new();
    private readonly FakeClock _clock = new();
    private Dictionary<string, short> _countryIds = [];
    private int _seq;

    public async Task InitializeAsync()
    {
        await _db.Database.EnsureCreatedAsync();
        _countryIds = await _db.Countries.ToDictionaryAsync(c => c.IsoCode, c => c.Id);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private ProfileService Sut() => new(_db, _me, _clock);

    /// <summary>Creates a user; <paramref name="handle"/> null = no handle (and never Public).</summary>
    private async Task<User> UserAsync(string name, string? handle, MapVisibility visibility = MapVisibility.Public)
    {
        var user = new User(name, $"u{++_seq}@example.com", "hash", Now);
        user.UpdateProfile(name, visibility, handle, Now);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task FollowAsync(User follower, User followed, DateTimeOffset? at = null)
    {
        _db.UserFollows.Add(new UserFollow(follower.Id, followed.Id, at ?? Now));
        await _db.SaveChangesAsync();
    }

    private async Task<UserBook> ShelveAsync(
        User owner, string title, string authorIso, string originIso, ReadingStatus status, DateOnly? finished = null, string? review = null)
    {
        var author = new Author($"Autor de {title}", Now);
        var book = new Book(title, Now);
        _db.Authors.Add(author);
        _db.Books.Add(book);
        _db.BookAuthors.Add(new BookAuthor(book.Id, author.Id, 0));
        _db.AuthorCountries.Add(new AuthorCountry(
            author.Id, _countryIds[authorIso], AuthorCountryRelationship.Citizenship, GeoSource.Wikidata, 0.9m, false, null, Now));
        _db.BookOriginCountries.Add(new BookOriginCountry(book.Id, _countryIds[originIso], GeoSource.Wikidata, 0.9m, false, null, Now));
        var item = new UserBook(owner.Id, book.Id, status, 4, review, new DateOnly(2024, 1, 1), finished, Now);
        _db.UserBooks.Add(item);
        await _db.SaveChangesAsync();
        return item;
    }

    private static async Task<NotFoundException> NotFoundAsync(Func<Task> action) =>
        await Should.ThrowAsync<NotFoundException>(action);

    // ---------------------------------------------------------------- search

    [Fact]
    public async Task Search_matches_handle_prefix_and_any_name_word_ignoring_accent_and_case()
    {
        var me = await UserAsync("Eu", "eu_mesmo");
        _me.UserId = me.Id;
        await UserAsync("José Álvares", "jose");
        await UserAsync("Maria José", "maria");
        await UserAsync("Joana", "joanabook");
        await UserAsync("Pedro", "pedro");

        (await Sut().SearchAsync("jose", null, default)).Select(p => p.Handle).ShouldBe(["jose", "maria"]);
        (await Sut().SearchAsync("ÁLV", null, default)).Select(p => p.Handle).ShouldBe(["jose"]);
        (await Sut().SearchAsync("  JOANAB ", null, default)).Select(p => p.Handle).ShouldBe(["joanabook"]);
        (await Sut().SearchAsync("oana", null, default)).ShouldBeEmpty(); // prefix only, never infix
    }

    [Fact]
    public async Task Search_orders_exact_handle_then_handle_prefix_then_name()
    {
        _me.UserId = (await UserAsync("Eu", "eu_mesmo")).Id;
        await UserAsync("Ana Beatriz", "zz_ana_name");   // name match (sorted by name: "ana beatriz" < "ana clara")
        await UserAsync("Zeca", "anabela");              // handle prefix
        await UserAsync("Outra", "ana");                 // exact handle
        await UserAsync("Ana Clara", "clara");           // name prefix

        var found = await Sut().SearchAsync("ana", null, default);

        found.Select(p => p.Handle).ShouldBe(["ana", "anabela", "zz_ana_name", "clara"]);
    }

    [Fact]
    public async Task Search_returns_only_public_profiles_and_never_the_caller()
    {
        var me = await UserAsync("Carla Eu", "carla_eu");
        _me.UserId = me.Id;
        await UserAsync("Carla Privada", "carla_priv", MapVisibility.Private);
        await UserAsync("Carla Link", "carla_link", MapVisibility.LinkOnly);
        await UserAsync("Carla Sem Apelido", null, MapVisibility.Private);
        await UserAsync("Carla Publica", "carla_pub");

        var found = await Sut().SearchAsync("carla", null, default);

        found.Select(p => p.Handle).ShouldBe(["carla_pub"]);
    }

    [Fact]
    public async Task Search_treats_like_wildcards_literally()
    {
        _me.UserId = (await UserAsync("Eu", "eu_mesmo")).Id;
        await UserAsync("Ana", "ana_1");
        await UserAsync("Bia", "bia50");

        (await Sut().SearchAsync("%%", null, default)).ShouldBeEmpty();
        (await Sut().SearchAsync("a_", null, default)).ShouldBeEmpty();   // '_' is not "any char" ("an..." would match)
        (await Sut().SearchAsync("ana_", null, default)).Select(p => p.Handle).ShouldBe(["ana_1"]);
        (await Sut().SearchAsync("bia%", null, default)).ShouldBeEmpty();
        (await Sut().SearchAsync("\\\\", null, default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_honors_limit_and_validates_q_and_limit()
    {
        _me.UserId = (await UserAsync("Eu", "eu_mesmo")).Id;
        for (var i = 0; i < 12; i++)
        {
            await UserAsync($"Leitor {i:D2}", $"leitor{i:D2}");
        }

        (await Sut().SearchAsync("leitor", null, default)).Count.ShouldBe(10);
        (await Sut().SearchAsync("leitor", 3, default)).Count.ShouldBe(3);
        (await Sut().SearchAsync("leitor", 20, default)).Count.ShouldBe(12);

        var shortQ = await Should.ThrowAsync<ValidationException>(() => Sut().SearchAsync(" a ", null, default));
        shortQ.Errors.ShouldContainKey("q");
        (await Should.ThrowAsync<ValidationException>(() => Sut().SearchAsync(null, null, default))).Errors.ShouldContainKey("q");
        foreach (var bad in new[] { 0, -1, 21 })
        {
            (await Should.ThrowAsync<ValidationException>(() => Sut().SearchAsync("leitor", bad, default))).Errors.ShouldContainKey("limit");
        }
    }

    [Fact]
    public async Task Search_summaries_carry_counts_and_relative_flags_and_only_read_books_count()
    {
        var me = await UserAsync("Eu", "eu_mesmo");
        _me.UserId = me.Id;
        var ana = await UserAsync("Ana", "ana");
        var bia = await UserAsync("Bia", "bia");
        await FollowAsync(me, ana);
        await FollowAsync(ana, me);
        await FollowAsync(bia, ana);
        await ShelveAsync(ana, "L1", "BRA", "BRA", ReadingStatus.Read, new DateOnly(2025, 1, 1));
        await ShelveAsync(ana, "L2", "BRA", "BRA", ReadingStatus.Reading);
        await ShelveAsync(ana, "L3", "BRA", "BRA", ReadingStatus.WantToRead);

        var found = (await Sut().SearchAsync("an", null, default)).Single();

        found.ShouldBe(new ProfileSummaryDto("ana", "Ana", FollowersCount: 2, ReadBooksCount: 1, IsFollowing: true, FollowsYou: true));
        (await Sut().SearchAsync("bi", null, default)).Single().ShouldBe(new ProfileSummaryDto("bia", "Bia", 0, 0, false, false));
    }

    // ---------------------------------------------------------------- profile

    [Fact]
    public async Task Non_public_and_unknown_profiles_look_exactly_alike_to_others_but_the_owner_sees_their_own()
    {
        var me = await UserAsync("Eu", "eu_mesmo");
        await UserAsync("Priv", "priv", MapVisibility.Private);
        await UserAsync("Link", "linkonly", MapVisibility.LinkOnly);
        var owner = await UserAsync("Dona", "dona", MapVisibility.Private);
        _me.UserId = me.Id;

        var privateEx = await NotFoundAsync(() => Sut().GetAsync("priv", default));
        var linkEx = await NotFoundAsync(() => Sut().GetAsync("linkonly", default));
        var unknownEx = await NotFoundAsync(() => Sut().GetAsync("nao_existe", default));
        new[] { privateEx, linkEx, unknownEx }.Select(e => (e.Code, e.Message)).Distinct().Count().ShouldBe(1);
        privateEx.Code.ShouldBe("profile.not_found");

        _me.UserId = owner.Id;
        var own = await Sut().GetAsync("DONA", default); // handle is case-insensitive
        own.IsSelf.ShouldBeTrue();
        own.IsFollowing.ShouldBeFalse();
        own.FollowsYou.ShouldBeFalse();
    }

    [Fact]
    public async Task Profile_header_has_counts_explored_countries_member_since_and_only_read_books()
    {
        var me = await UserAsync("Eu", "eu_mesmo");
        var ana = await UserAsync("Ana", "ana");
        _me.UserId = me.Id;
        await ShelveAsync(ana, "A", "BRA", "PRT", ReadingStatus.Read, new DateOnly(2025, 1, 1));
        await ShelveAsync(ana, "B", "ARG", "ARG", ReadingStatus.Read, new DateOnly(2025, 2, 1));
        await ShelveAsync(ana, "C", "JPN", "JPN", ReadingStatus.Reading);
        await ShelveAsync(ana, "D", "FRA", "FRA", ReadingStatus.Abandoned);
        await FollowAsync(me, ana);
        await FollowAsync(ana, me);

        var profile = await Sut().GetAsync("ana", default);

        profile.ReadBooksCount.ShouldBe(2);
        profile.ExploredCountriesAuthors.ShouldBe(2);
        profile.ExploredCountriesOrigins.ShouldBe(2);
        profile.FollowersCount.ShouldBe(1);
        profile.FollowingCount.ShouldBe(1);
        profile.IsFollowing.ShouldBeTrue();
        profile.FollowsYou.ShouldBeTrue();
        profile.IsSelf.ShouldBeFalse();
        profile.MemberSince.ShouldBe(Now);

        // Same numbers as the owner's own dashboard (same rules, owner's id).
        _me.UserId = ana.Id;
        var dashboard = await new StatisticsService(_db, _me, _clock).GetDashboardAsync(default);
        dashboard.CountriesByAuthors.ShouldBe(profile.ExploredCountriesAuthors);
        dashboard.CountriesByOrigins.ShouldBe(profile.ExploredCountriesOrigins);
        dashboard.TotalReadBooks.ShouldBe(profile.ReadBooksCount);
    }

    // ---------------------------------------------------------------- follow

    [Fact]
    public async Task Follow_and_unfollow_are_idempotent_and_update_counts()
    {
        var me = await UserAsync("Eu", "eu_mesmo", MapVisibility.Private);
        var ana = await UserAsync("Ana", "ana");
        _me.UserId = me.Id;

        await Sut().FollowAsync("ana", default);
        await Sut().FollowAsync("ANA", default);
        (await _db.UserFollows.CountAsync()).ShouldBe(1);
        (await Sut().GetAsync("ana", default)).FollowersCount.ShouldBe(1);
        (await Sut().GetAsync("ana", default)).IsFollowing.ShouldBeTrue();

        await Sut().UnfollowAsync("ana", default);
        await Sut().UnfollowAsync("ana", default);
        await Sut().UnfollowAsync("inexistente", default);
        (await _db.UserFollows.CountAsync()).ShouldBe(0);
        (await Sut().GetAsync("ana", default)).FollowersCount.ShouldBe(0);
        _ = ana;
    }

    [Fact]
    public async Task Cannot_follow_yourself_even_when_private()
    {
        var me = await UserAsync("Eu", "eu_mesmo", MapVisibility.Private);
        _me.UserId = me.Id;

        var ex = await Should.ThrowAsync<ValidationException>(() => Sut().FollowAsync("eu_mesmo", default));

        ex.Code.ShouldBe("follow.self");
        (await _db.UserFollows.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Following_a_non_public_or_unknown_profile_is_404_and_creates_nothing()
    {
        var me = await UserAsync("Eu", "eu_mesmo", MapVisibility.Private);
        await UserAsync("Priv", "priv", MapVisibility.Private);
        await UserAsync("Link", "linkonly", MapVisibility.LinkOnly);
        _me.UserId = me.Id;

        foreach (var handle in new[] { "priv", "linkonly", "ninguem" })
        {
            (await NotFoundAsync(() => Sut().FollowAsync(handle, default))).Code.ShouldBe("profile.not_found");
        }

        (await _db.UserFollows.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Unfollow_still_works_after_the_profile_became_private_and_the_list_hides_it()
    {
        var me = await UserAsync("Eu", "eu_mesmo", MapVisibility.Private);
        var ana = await UserAsync("Ana", "ana");
        await FollowAsync(me, ana);
        _me.UserId = me.Id;
        (await Sut().GetMyFollowingAsync(null, null, default)).Items.Select(p => p.Handle).ShouldBe(["ana"]);

        ana.UpdateProfile("Ana", MapVisibility.Private, Now);
        await _db.SaveChangesAsync();

        (await Sut().GetMyFollowingAsync(null, null, default)).Items.ShouldBeEmpty(); // Private accounts are omitted
        await NotFoundAsync(() => Sut().GetAsync("ana", default));
        await Sut().UnfollowAsync("ana", default);
        (await _db.UserFollows.CountAsync()).ShouldBe(0);
    }

    // ---------------------------------------------------------------- lists

    [Fact]
    public async Task Followers_and_following_lists_are_paged_newest_first_with_relative_flags()
    {
        var me = await UserAsync("Eu", "eu_mesmo", MapVisibility.Private);
        var star = await UserAsync("Star", "star");
        var fans = new List<User>();
        for (var i = 0; i < 5; i++)
        {
            var fan = await UserAsync($"Fan {i}", $"fan{i}");
            fans.Add(fan);
            await FollowAsync(fan, star, Now.AddMinutes(i));
        }

        var privateFan = await UserAsync("Fan Priv", "fanpriv", MapVisibility.Private);
        await FollowAsync(privateFan, star, Now.AddMinutes(10));
        await FollowAsync(me, fans[4]);        // I follow fan4
        await FollowAsync(fans[3], me);        // fan3 follows me
        _me.UserId = me.Id;

        var page1 = await Sut().GetFollowersAsync("star", 1, 2, default);
        var page3 = await Sut().GetFollowersAsync("star", 3, 2, default);

        page1.Items.Select(p => p.Handle).ShouldBe(["fan4", "fan3"]);
        page1.TotalCount.ShouldBe(5);   // the private follower is omitted from the list...
        page1.TotalPages.ShouldBe(3);
        page1.Items[0].IsFollowing.ShouldBeTrue();
        page1.Items[1].FollowsYou.ShouldBeTrue();
        page3.Items.Select(p => p.Handle).ShouldBe(["fan0"]);
        (await Sut().GetAsync("star", default)).FollowersCount.ShouldBe(6); // ...but still counted in followersCount

        (await Sut().GetFollowingAsync("fan4", null, null, default)).Items.Select(p => p.Handle).ShouldBe(["star"]);
        (await NotFoundAsync(() => Sut().GetFollowersAsync("fanpriv", null, null, default))).Code.ShouldBe("profile.not_found");
        (await NotFoundAsync(() => Sut().GetFollowingAsync("nao", null, null, default))).Code.ShouldBe("profile.not_found");
    }

    [Fact]
    public async Task Me_lists_work_even_when_my_own_profile_is_not_public()
    {
        var me = await UserAsync("Eu", null, MapVisibility.Private);
        var ana = await UserAsync("Ana", "ana");
        var bia = await UserAsync("Bia", "bia", MapVisibility.Private);
        await FollowAsync(ana, me);
        await FollowAsync(bia, me);
        await FollowAsync(me, ana);
        _me.UserId = me.Id;

        var followers = await Sut().GetMyFollowersAsync(null, null, default);
        var following = await Sut().GetMyFollowingAsync(null, null, default);

        followers.Items.Select(p => p.Handle).ShouldBe(["ana"]);
        followers.Items[0].IsFollowing.ShouldBeTrue();
        followers.Items[0].FollowsYou.ShouldBeTrue();
        following.Items.Select(p => p.Handle).ShouldBe(["ana"]);
    }

    // ---------------------------------------------------------------- maps

    [Fact]
    public async Task Profile_maps_equal_the_owners_own_read_maps_and_ignore_other_statuses()
    {
        var me = await UserAsync("Eu", "eu_mesmo", MapVisibility.Private);
        var ana = await UserAsync("Ana", "ana");
        await ShelveAsync(ana, "A", "BRA", "PRT", ReadingStatus.Read, new DateOnly(2025, 1, 1));
        await ShelveAsync(ana, "B", "BRA", "BRA", ReadingStatus.Read, new DateOnly(2025, 2, 1));
        await ShelveAsync(ana, "C", "JPN", "JPN", ReadingStatus.Reading);
        await ShelveAsync(ana, "D", "FRA", "FRA", ReadingStatus.WantToRead);
        await ShelveAsync(ana, "E", "ARG", "ARG", ReadingStatus.Abandoned);
        await ShelveAsync(me, "Meu", "DEU", "DEU", ReadingStatus.Read, new DateOnly(2025, 3, 1));

        _me.UserId = me.Id;
        var viaProfile = new[]
        {
            await Sut().GetMapAsync("ana", MapType.Authors, default),
            await Sut().GetMapAsync("ana", MapType.Origins, default),
        };
        _me.UserId = ana.Id;
        var own = new MapService(_db, _me);
        var viaOwner = new[]
        {
            await own.GetMapAsync(MapType.Authors, null, default),
            await own.GetMapAsync(MapType.Origins, null, default),
        };

        viaProfile.ShouldBe(viaOwner, ignoreOrder: false, comparer: new MapDataComparer());
        viaProfile[0].Countries.Select(c => c.Iso).ShouldBe(["BRA"]);
        viaProfile[0].Status.ShouldBe("Read");
        viaProfile[1].Countries.Select(c => c.Iso).OrderBy(i => i).ShouldBe(["BRA", "PRT"]);
    }

    [Fact]
    public async Task Profile_maps_of_a_non_public_profile_are_404_and_the_owner_can_read_their_own()
    {
        var me = await UserAsync("Eu", "eu_mesmo", MapVisibility.Private);
        var ana = await UserAsync("Ana", "ana", MapVisibility.Private);
        await ShelveAsync(ana, "A", "BRA", "BRA", ReadingStatus.Read, new DateOnly(2025, 1, 1));
        _me.UserId = me.Id;

        await NotFoundAsync(() => Sut().GetMapAsync("ana", MapType.Authors, default));
        await NotFoundAsync(() => Sut().GetCountryDetailAsync("ana", MapType.Authors, "BRA", default));
        await NotFoundAsync(() => Sut().GetCountryDetailAsync("ana", MapType.Authors, "XXX", default)); // still 404, not 400

        _me.UserId = ana.Id;
        (await Sut().GetMapAsync("ana", MapType.Authors, default)).ExploredCountries.ShouldBe(1);
    }

    [Fact]
    public async Task Country_panel_has_reviews_authors_and_books_of_read_items_only_and_no_library_item_id()
    {
        var me = await UserAsync("Eu", "eu_mesmo", MapVisibility.Private);
        var ana = await UserAsync("Ana", "ana");
        await ShelveAsync(ana, "Lido", "BRA", "BRA", ReadingStatus.Read, new DateOnly(2025, 2, 1), review: "Excelente!");
        await ShelveAsync(ana, "Lido sem resenha", "BRA", "BRA", ReadingStatus.Read, new DateOnly(2025, 1, 1));
        await ShelveAsync(ana, "Lendo", "BRA", "BRA", ReadingStatus.Reading, review: "SEGREDO em andamento");
        await ShelveAsync(ana, "Quero", "BRA", "BRA", ReadingStatus.WantToRead, review: "SEGREDO desejo");
        await ShelveAsync(ana, "Abandonado", "BRA", "BRA", ReadingStatus.Abandoned, review: "SEGREDO abandono");
        _me.UserId = me.Id;

        var authors = await Sut().GetCountryDetailAsync("ana", MapType.Authors, "bra", default);
        var origins = await Sut().GetCountryDetailAsync("ana", MapType.Origins, "BRA", default);

        authors.Status.ShouldBe("Read");
        authors.BookCount.ShouldBe(2);
        authors.Books.Select(b => b.Title).ShouldBe(["Lido", "Lido sem resenha"]);
        authors.Books[0].Review.ShouldBe("Excelente!");
        authors.Books[0].Rating.ShouldBe(4);
        authors.Books[1].Review.ShouldBeNull();
        authors.Authors.Count.ShouldBe(2);
        authors.Authors.SelectMany(a => a.Books).Select(b => b.Title).Order().ShouldBe(["Lido", "Lido sem resenha"]);
        authors.Authors.SelectMany(a => a.Books).Single(b => b.Title == "Lido").Review.ShouldBe("Excelente!");
        origins.Authors.ShouldBeEmpty();
        origins.Books.Select(b => b.Title).ShouldBe(["Lido", "Lido sem resenha"]);

        // Nothing from non-Read items leaks anywhere in the payload.
        System.Text.Json.JsonSerializer.Serialize(authors).ShouldNotContain("SEGREDO");
        System.Text.Json.JsonSerializer.Serialize(origins).ShouldNotContain("SEGREDO");

        // No library item id (nor any user id) in the public DTOs.
        foreach (var type in new[] { typeof(ProfileBookDto), typeof(ProfileCountryAuthorDto), typeof(ProfileCountryDetailDto), typeof(ProfileDto), typeof(ProfileSummaryDto) })
        {
            foreach (var name in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name))
            {
                name.ShouldNotContain("LibraryItem");
                name.ShouldNotContain("Email");
                new[] { "Id", "UserId", "OwnerId", "StartDate" }.ShouldNotContain(name);
            }
        }
    }

    [Fact]
    public async Task Country_panel_validates_the_iso_after_the_profile_check()
    {
        var me = await UserAsync("Eu", "eu_mesmo", MapVisibility.Private);
        await UserAsync("Ana", "ana");
        _me.UserId = me.Id;

        (await Should.ThrowAsync<ValidationException>(() => Sut().GetCountryDetailAsync("ana", MapType.Authors, "ZZZ", default)))
            .Code.ShouldBe("country.unknown_iso");
        var empty = await Sut().GetCountryDetailAsync("ana", MapType.Origins, "BRA", default);
        empty.BookCount.ShouldBe(0);
        empty.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task Users_do_not_see_each_others_books_through_a_profile()
    {
        var me = await UserAsync("Eu", "eu_mesmo", MapVisibility.Private);
        var ana = await UserAsync("Ana", "ana");
        var bia = await UserAsync("Bia", "bia");
        await ShelveAsync(ana, "Da Ana", "BRA", "BRA", ReadingStatus.Read, new DateOnly(2025, 1, 1), "r-ana");
        await ShelveAsync(bia, "Da Bia", "JPN", "JPN", ReadingStatus.Read, new DateOnly(2025, 1, 1), "r-bia");
        _me.UserId = me.Id;

        var anaMap = await Sut().GetMapAsync("ana", MapType.Authors, default);
        var anaJpn = await Sut().GetCountryDetailAsync("ana", MapType.Authors, "JPN", default);

        anaMap.Countries.Select(c => c.Iso).ShouldBe(["BRA"]);
        anaJpn.Books.ShouldBeEmpty();
    }

    private sealed class MapDataComparer : IEqualityComparer<MapDataDto>
    {
        public bool Equals(MapDataDto? x, MapDataDto? y) =>
            x is not null && y is not null
            && x.MapType == y.MapType && x.TotalReadBooks == y.TotalReadBooks && x.ExploredCountries == y.ExploredCountries
            && x.MaxBookCount == y.MaxBookCount && x.UnknownBookCount == y.UnknownBookCount && x.Status == y.Status
            && x.Countries.SequenceEqual(y.Countries);

        public int GetHashCode(MapDataDto obj) => obj.MapType.GetHashCode();
    }
}

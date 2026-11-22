using AtlasLiterario.Application.Auth;
using AtlasLiterario.Application.Tests.Fakes;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AtlasLiterario.Application.Tests.Auth;

public sealed class AuthHandleTests : IDisposable
{
    private readonly AppDbContext _db = TestDb.Create();
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeClock _clock = new();
    private readonly AuthService _sut;

    public AuthHandleTests()
    {
        _sut = new AuthService(_db, new FakePasswordHasher(), new FakeTokenService(), _clock, _currentUser, new FakeEmailSender());
    }

    public void Dispose() => _db.Dispose();

    private async Task<UserProfileDto> RegisterAsync(string email)
    {
        var auth = await _sut.RegisterAsync(new RegisterRequest("Ana", email, "segredo123"), default);
        return auth.User;
    }

    private async Task<UserProfileDto> UpdateAsync(Guid userId, MapVisibility visibility, string? handle, bool provide = true)
    {
        _currentUser.UserId = userId;
        var request = new UpdateProfileRequest("Ana", visibility);
        if (provide)
        {
            request = request with { Handle = handle };
        }

        return await _sut.UpdateProfileAsync(request, default);
    }

    [Fact]
    public async Task New_accounts_have_no_handle_zero_counts_and_stay_private()
    {
        var user = await RegisterAsync("ana@example.com");

        user.Handle.ShouldBeNull();
        user.FollowersCount.ShouldBe(0);
        user.FollowingCount.ShouldBe(0);
        user.MapVisibility.ShouldBe(MapVisibility.Private);
    }

    [Fact]
    public async Task Handle_is_normalized_and_returned_by_me()
    {
        var user = await RegisterAsync("ana@example.com");

        var updated = await UpdateAsync(user.Id, MapVisibility.Private, "  Ana_Leitora ");

        updated.Handle.ShouldBe("ana_leitora");
        _currentUser.UserId = user.Id;
        (await _sut.GetProfileAsync(default)).Handle.ShouldBe("ana_leitora");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("tem espaco")]
    [InlineData("Ação")]
    [InlineData("admin")]
    [InlineData("ME")]
    public async Task Invalid_or_reserved_handle_is_400_with_errors_handle(string handle)
    {
        var user = await RegisterAsync("ana@example.com");

        var ex = await Should.ThrowAsync<ValidationException>(() => UpdateAsync(user.Id, MapVisibility.Private, handle));

        ex.Code.ShouldBe("profile.handle_invalid");
        ex.Errors.ShouldContainKey("handle");
        (await _db.Users.SingleAsync()).Handle.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_uniqueness_ignores_case_and_is_a_409()
    {
        var ana = await RegisterAsync("ana@example.com");
        var bia = await RegisterAsync("bia@example.com");
        await UpdateAsync(ana.Id, MapVisibility.Private, "leitora");

        var ex = await Should.ThrowAsync<ConflictException>(() => UpdateAsync(bia.Id, MapVisibility.Private, "LEITORA"));

        ex.Code.ShouldBe("profile.handle_taken");
    }

    [Fact]
    public async Task Keeping_your_own_handle_is_not_a_conflict_and_changing_it_releases_the_old_one()
    {
        var ana = await RegisterAsync("ana@example.com");
        var bia = await RegisterAsync("bia@example.com");
        await UpdateAsync(ana.Id, MapVisibility.Private, "leitora");

        (await UpdateAsync(ana.Id, MapVisibility.LinkOnly, "leitora")).Handle.ShouldBe("leitora");
        await UpdateAsync(ana.Id, MapVisibility.Private, "outra");

        (await UpdateAsync(bia.Id, MapVisibility.Private, "leitora")).Handle.ShouldBe("leitora");
    }

    [Fact]
    public async Task Public_requires_a_handle()
    {
        var user = await RegisterAsync("ana@example.com");

        var ex = await Should.ThrowAsync<ValidationException>(() => UpdateAsync(user.Id, MapVisibility.Public, null, provide: false));

        ex.Code.ShouldBe("profile.handle_required");
        (await _db.Users.SingleAsync()).MapVisibility.ShouldBe(MapVisibility.Private);
    }

    [Fact]
    public async Task Public_with_a_handle_in_the_same_request_works_and_omitted_handle_keeps_it()
    {
        var user = await RegisterAsync("ana@example.com");

        (await UpdateAsync(user.Id, MapVisibility.Public, "ana")).MapVisibility.ShouldBe(MapVisibility.Public);

        var kept = await UpdateAsync(user.Id, MapVisibility.Public, null, provide: false);
        kept.Handle.ShouldBe("ana");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Removing_the_handle_while_public_is_refused_but_allowed_when_not_public(string? remove)
    {
        var user = await RegisterAsync("ana@example.com");
        await UpdateAsync(user.Id, MapVisibility.Public, "ana");

        var ex = await Should.ThrowAsync<ValidationException>(() => UpdateAsync(user.Id, MapVisibility.Public, remove));
        ex.Code.ShouldBe("profile.handle_required");
        (await _db.Users.SingleAsync()).Handle.ShouldBe("ana");

        var done = await UpdateAsync(user.Id, MapVisibility.Private, remove);
        done.Handle.ShouldBeNull();
    }

    [Fact]
    public async Task Counts_follow_the_follow_table()
    {
        var ana = await RegisterAsync("ana@example.com");
        var bia = await RegisterAsync("bia@example.com");
        _db.UserFollows.Add(new UserFollow(bia.Id, ana.Id, _clock.UtcNow));
        await _db.SaveChangesAsync();

        _currentUser.UserId = ana.Id;
        var profile = await _sut.GetProfileAsync(default);

        profile.FollowersCount.ShouldBe(1);
        profile.FollowingCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_unique_index_violation_on_save_becomes_409_handle_taken()
    {
        // InMemory has no unique index: simulate the race where another request wins between the check and the save.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"atlas-race-{Guid.NewGuid()}")
            .AddInterceptors(new FailUpdateInterceptor())
            .Options;
        await using var db = new AppDbContext(options);
        var user = new User("Ana", "ana@example.com", "hash", _clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync(); // inserts are allowed by the interceptor; updates fail
        var service = new AuthService(
            db, new FakePasswordHasher(), new FakeTokenService(), _clock, new FakeCurrentUser { UserId = user.Id }, new FakeEmailSender());

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            service.UpdateProfileAsync(new UpdateProfileRequest("Ana", MapVisibility.Private) { Handle = "corrida" }, default));

        ex.Code.ShouldBe("profile.handle_taken");
    }

    private sealed class FailUpdateInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<User>().Any(e => e.State == EntityState.Modified))
            {
                throw new DbUpdateException("duplicate key value violates unique constraint \"ix_user_handle\"");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

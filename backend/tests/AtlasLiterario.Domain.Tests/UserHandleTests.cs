using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.Domain.Tests;

public sealed class UserHandleTests
{
    private static readonly DateTimeOffset Now = new(2025, 3, 14, 12, 0, 0, TimeSpan.Zero);

    private static User NewUser() => new("Ana", "ana@example.com", "hash", Now);

    [Theory]
    [InlineData("  Ana_Silva ", "ana_silva")]
    [InlineData("LEITORA99", "leitora99")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Handle_is_trimmed_and_lower_cased_and_blank_means_none(string? input, string? expected) =>
        User.NormalizeHandle(input).ShouldBe(expected);

    [Theory]
    [InlineData("ab")]                                  // too short
    [InlineData("abcdefghijklmnopqrstuvwxyz1234567")]   // 33 chars
    [InlineData("com espaco")]
    [InlineData("acentuação")]
    [InlineData("ana-silva")]
    [InlineData("ana.silva")]
    [InlineData("@ana")]
    [InlineData("an\na")]
    [InlineData("admin")]
    [InlineData("atlas")]
    [InlineData("api")]
    [InlineData("me")]
    [InlineData("profiles")]
    [InlineData("support")]
    [InlineData("moderator")]
    public void Invalid_or_reserved_handles_are_rejected_with_a_field_error(string handle)
    {
        var ex = Should.Throw<ValidationException>(() => User.ValidateHandle(User.NormalizeHandle(handle) ?? handle));

        ex.Code.ShouldBe("profile.handle_invalid");
        ex.Errors.ShouldContainKey("handle");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("a_b_c_1")]
    [InlineData("123")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234")] // exactly 30
    public void Valid_handles_are_accepted(string handle) => Should.NotThrow(() => User.ValidateHandle(handle));

    [Fact]
    public void New_user_is_private_without_handle()
    {
        var user = NewUser();

        user.MapVisibility.ShouldBe(MapVisibility.Private);
        user.Handle.ShouldBeNull();
    }

    [Fact]
    public void Public_requires_a_handle()
    {
        var user = NewUser();

        var ex = Should.Throw<ValidationException>(() => user.UpdateProfile("Ana", MapVisibility.Public, Now));

        ex.Code.ShouldBe("profile.handle_required");
    }

    [Fact]
    public void Handle_cannot_be_removed_while_public()
    {
        var user = NewUser();
        user.ChangeHandle("ana", Now);
        user.UpdateProfile("Ana", MapVisibility.Public, Now);

        Should.Throw<ValidationException>(() => user.ChangeHandle(null, Now)).Code.ShouldBe("profile.handle_required");
        user.Handle.ShouldBe("ana");

        user.UpdateProfile("Ana", MapVisibility.Private, Now);
        user.ChangeHandle("", Now);
        user.Handle.ShouldBeNull();
    }

    [Fact]
    public void Enum_values_keep_their_numbers_and_public_is_appended()
    {
        ((int)MapVisibility.Private).ShouldBe(0);
        ((int)MapVisibility.LinkOnly).ShouldBe(1);
        ((int)MapVisibility.Public).ShouldBe(2);
    }

    [Fact]
    public void A_user_cannot_follow_themselves()
    {
        var id = Guid.NewGuid();

        Should.Throw<ValidationException>(() => new UserFollow(id, id, Now)).Code.ShouldBe("follow.self");
    }
}

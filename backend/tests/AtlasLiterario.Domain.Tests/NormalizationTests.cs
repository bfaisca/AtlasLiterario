using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.Domain.Tests;

public sealed class NormalizationTests
{
    [Fact]
    public void Author_name_is_normalized_without_accents_case_or_extra_spaces()
    {
        Author.NormalizeName("  Gabriel   García  Márquez ").ShouldBe("gabriel garcia marquez");
    }

    [Fact]
    public void User_email_is_trimmed_and_lower_cased()
    {
        var user = new User("Ana", "  Ana.Silva@Example.COM ", "hash", DateTimeOffset.UtcNow);

        user.Email.ShouldBe("ana.silva@example.com");
    }

    [Fact]
    public void User_name_cannot_be_blank()
    {
        Should.Throw<ValidationException>(() => new User("  ", "a@b.com", "hash", DateTimeOffset.UtcNow))
            .Code.ShouldBe("user.invalid_name");
    }

    [Fact]
    public void Manual_geo_relation_is_verified_with_full_confidence()
    {
        var relation = BookOriginCountry.CreateManual(Guid.NewGuid(), 76, Guid.NewGuid(), DateTimeOffset.UtcNow);

        relation.Confidence.ShouldBe(1m);
        relation.IsVerified.ShouldBeTrue();
        relation.IsManual.ShouldBeTrue();
    }

    [Fact]
    public void Geo_relation_confidence_must_be_between_0_and_1()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AuthorCountry(
            Guid.NewGuid(), 76, Enums.AuthorCountryRelationship.Citizenship, Enums.GeoSource.Wikidata, 1.5m, false,
            null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Refresh_token_is_inactive_after_revocation_or_expiration()
    {
        var now = DateTimeOffset.UtcNow;
        var token = new RefreshToken(Guid.NewGuid(), "hash", now, now.AddDays(30));

        token.IsActive(now).ShouldBeTrue();
        token.IsActive(now.AddDays(31)).ShouldBeFalse();

        token.Revoke(now, "next");
        token.IsActive(now).ShouldBeFalse();
        token.ReplacedByTokenHash.ShouldBe("next");
    }
}

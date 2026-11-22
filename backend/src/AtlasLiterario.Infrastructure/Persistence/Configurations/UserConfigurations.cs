using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtlasLiterario.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("user");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Name).HasMaxLength(User.MaxNameLength).IsRequired();

        // E-mail is stored lower-cased by the domain, so a plain unique index gives case-insensitive uniqueness.
        builder.Property(u => u.Email).HasMaxLength(User.MaxEmailLength).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();

        // Public nickname: stored lower-cased by the domain; NULL for accounts that never chose one (NULLs do not collide).
        builder.Property(u => u.Handle).HasMaxLength(User.MaxHandleLength);
        builder.HasIndex(u => u.Handle).IsUnique();

        // Only used by the profile search; accounts that predate it stay '' until their first profile update (they are Private).
        builder.Property(u => u.NormalizedName).HasMaxLength(User.MaxNameLength).HasDefaultValue(string.Empty).IsRequired();

        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.Role).HasDefaultValue(UserRole.User).IsRequired();
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_token");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(128);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(t => t.IsRevoked);
    }
}

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_token");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MapShareConfiguration : IEntityTypeConfiguration<MapShare>
{
    public void Configure(EntityTypeBuilder<MapShare> builder)
    {
        builder.ToTable("map_share");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Token).HasMaxLength(MapShare.TokenLength).IsRequired();
        builder.HasIndex(s => s.Token).IsUnique();
        builder.HasIndex(s => new { s.UserId, s.MapType });
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(s => s.IsActive);
    }
}

internal sealed class UserFollowConfiguration : IEntityTypeConfiguration<UserFollow>
{
    public void Configure(EntityTypeBuilder<UserFollow> builder)
    {
        builder.ToTable("user_follow", t => t.HasCheckConstraint("ck_user_follow_not_self", "follower_id <> followed_id"));
        builder.HasKey(f => new { f.FollowerId, f.FollowedId });
        builder.HasIndex(f => f.FollowedId); // followers of a user
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.FollowerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.FollowedId).OnDelete(DeleteBehavior.Cascade);
    }
}

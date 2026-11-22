using AtlasLiterario.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtlasLiterario.Infrastructure.Persistence.Configurations;

internal sealed class UserBookConfiguration : IEntityTypeConfiguration<UserBook>
{
    public void Configure(EntityTypeBuilder<UserBook> builder)
    {
        builder.ToTable("user_book", t =>
        {
            t.HasCheckConstraint("ck_user_book_rating", "rating IS NULL OR (rating >= 1 AND rating <= 5)");
            t.HasCheckConstraint(
                "ck_user_book_dates", "start_date IS NULL OR finished_date IS NULL OR start_date <= finished_date");
        });
        builder.HasKey(ub => ub.Id);
        builder.Property(ub => ub.Id).ValueGeneratedNever();
        // Review: text column; the 10,000-character limit is enforced by the domain.
        builder.Property(ub => ub.Review).HasColumnType("text");

        builder.HasIndex(ub => new { ub.UserId, ub.BookId }).IsUnique();
        builder.HasIndex(ub => new { ub.UserId, ub.Status });
        builder.HasIndex(ub => new { ub.UserId, ub.FinishedDate });

        builder.HasOne<User>().WithMany().HasForeignKey(ub => ub.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ub => ub.Book).WithMany().HasForeignKey(ub => ub.BookId).OnDelete(DeleteBehavior.Restrict);
    }
}

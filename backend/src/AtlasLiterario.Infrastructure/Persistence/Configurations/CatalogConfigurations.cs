using AtlasLiterario.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtlasLiterario.Infrastructure.Persistence.Configurations;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("book");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Title).HasMaxLength(Book.MaxTitleLength).IsRequired();
        builder.Property(b => b.Subtitle).HasMaxLength(500);
        builder.Property(b => b.Isbn13).HasMaxLength(13).IsFixedLength();
        builder.Property(b => b.Isbn10).HasMaxLength(10).IsFixedLength();
        builder.Property(b => b.Publisher).HasMaxLength(300);
        builder.Property(b => b.CoverUrl).HasMaxLength(1000);
        builder.Property(b => b.OriginalLanguage).HasMaxLength(10);
        builder.Property(b => b.Genres).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");

        builder.HasIndex(b => b.Isbn13).IsUnique().HasFilter("isbn13 IS NOT NULL");
        builder.HasIndex(b => b.Isbn10);

        builder.HasMany(b => b.ExternalIdentifiers).WithOne().HasForeignKey(i => i.BookId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuthorConfiguration : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> builder)
    {
        builder.ToTable("author");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Name).HasMaxLength(Author.MaxNameLength).IsRequired();

        // Replaces the index on lower(name) from database.md: normalized (lower-case, accent-free) column.
        builder.Property(a => a.NormalizedName).HasMaxLength(Author.MaxNameLength).IsRequired();
        builder.HasIndex(a => a.NormalizedName);

        builder.HasMany(a => a.ExternalIdentifiers).WithOne().HasForeignKey(i => i.AuthorId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BookAuthorConfiguration : IEntityTypeConfiguration<BookAuthor>
{
    public void Configure(EntityTypeBuilder<BookAuthor> builder)
    {
        builder.ToTable("book_author");
        builder.HasKey(ba => new { ba.BookId, ba.AuthorId });
        builder.HasIndex(ba => ba.AuthorId);
        builder.HasOne(ba => ba.Book).WithMany(b => b.Authors).HasForeignKey(ba => ba.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ba => ba.Author).WithMany(a => a.Books).HasForeignKey(ba => ba.AuthorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExternalBookIdentifierConfiguration : IEntityTypeConfiguration<ExternalBookIdentifier>
{
    public void Configure(EntityTypeBuilder<ExternalBookIdentifier> builder)
    {
        builder.ToTable("external_book_identifier");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.ExternalId).HasMaxLength(ExternalBookIdentifier.MaxExternalIdLength).IsRequired();
        builder.HasIndex(i => new { i.Provider, i.ExternalId }).IsUnique();
        builder.HasIndex(i => i.BookId);
    }
}

internal sealed class ExternalAuthorIdentifierConfiguration : IEntityTypeConfiguration<ExternalAuthorIdentifier>
{
    public void Configure(EntityTypeBuilder<ExternalAuthorIdentifier> builder)
    {
        builder.ToTable("external_author_identifier");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.ExternalId).HasMaxLength(ExternalAuthorIdentifier.MaxExternalIdLength).IsRequired();
        builder.HasIndex(i => new { i.Provider, i.ExternalId }).IsUnique();
        builder.HasIndex(i => i.AuthorId);
    }
}

internal sealed class ExternalLookupCacheConfiguration : IEntityTypeConfiguration<ExternalLookupCache>
{
    public void Configure(EntityTypeBuilder<ExternalLookupCache> builder)
    {
        builder.ToTable("external_lookup_cache");
        builder.HasKey(c => c.Key);
        builder.Property(c => c.Key).HasMaxLength(ExternalLookupCache.MaxKeyLength);
        builder.Property(c => c.Payload).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(c => c.ExpiresAt);
    }
}

internal sealed class BookCoverConfiguration : IEntityTypeConfiguration<BookCover>
{
    public void Configure(EntityTypeBuilder<BookCover> builder)
    {
        builder.ToTable("book_cover", t => t.HasCheckConstraint("ck_book_cover_size", "size_bytes > 0 AND size_bytes <= 2097152"));
        builder.HasKey(c => c.BookId);
        builder.Property(c => c.BookId).ValueGeneratedNever();
        builder.Property(c => c.ContentType).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Data).HasColumnType("bytea").IsRequired();
        builder.Property(c => c.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.HasOne<Book>().WithOne().HasForeignKey<BookCover>(c => c.BookId).OnDelete(DeleteBehavior.Cascade);
    }
}

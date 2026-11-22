using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtlasLiterario.Infrastructure.Persistence.Configurations;

internal sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("country");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.NamePt).HasMaxLength(100).IsRequired();
        builder.Property(c => c.IsoCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(c => c.IsoCode2).HasMaxLength(2).IsFixedLength().IsRequired();
        builder.Property(c => c.GeoJsonId).HasMaxLength(10).IsRequired();
        builder.Property(c => c.WikidataQid).HasMaxLength(20);

        builder.HasIndex(c => c.IsoCode).IsUnique();
        builder.HasIndex(c => c.IsoCode2).IsUnique();
        builder.HasIndex(c => c.WikidataQid).IsUnique();

        builder.HasData(CountrySeedData.All);
    }
}

internal sealed class HistoricalCountryMappingConfiguration : IEntityTypeConfiguration<HistoricalCountryMapping>
{
    public void Configure(EntityTypeBuilder<HistoricalCountryMapping> builder)
    {
        builder.ToTable("historical_country_mapping");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.WikidataQid).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Note).HasMaxLength(300);
        builder.HasIndex(m => m.WikidataQid);
        builder.HasIndex(m => new { m.WikidataQid, m.CountryId }).IsUnique();
        builder.HasOne(m => m.Country).WithMany().HasForeignKey(m => m.CountryId).OnDelete(DeleteBehavior.Restrict);

        builder.HasData(HistoricalCountryMappingSeedData.All);
    }
}

internal sealed class AuthorCountryConfiguration : IEntityTypeConfiguration<AuthorCountry>
{
    public void Configure(EntityTypeBuilder<AuthorCountry> builder)
    {
        builder.ToTable("author_country", t =>
            t.HasCheckConstraint("ck_author_country_confidence", "confidence >= 0 AND confidence <= 1"));
        builder.HasKey(ac => new { ac.AuthorId, ac.CountryId, ac.RelationshipType });
        builder.Property(ac => ac.Confidence).HasPrecision(3, 2);
        builder.HasIndex(ac => new { ac.CountryId, ac.RelationshipType });
        builder.HasOne(ac => ac.Author).WithMany(a => a.Countries).HasForeignKey(ac => ac.AuthorId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ac => ac.Country).WithMany().HasForeignKey(ac => ac.CountryId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(ac => ac.IsManual);
    }
}

internal sealed class BookOriginCountryConfiguration : IEntityTypeConfiguration<BookOriginCountry>
{
    public void Configure(EntityTypeBuilder<BookOriginCountry> builder)
    {
        builder.ToTable("book_origin_country", t =>
            t.HasCheckConstraint("ck_book_origin_country_confidence", "confidence >= 0 AND confidence <= 1"));
        builder.HasKey(bo => new { bo.BookId, bo.CountryId });
        builder.Property(bo => bo.Confidence).HasPrecision(3, 2);
        builder.HasIndex(bo => bo.CountryId);
        builder.HasOne(bo => bo.Book).WithMany(b => b.OriginCountries).HasForeignKey(bo => bo.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(bo => bo.Country).WithMany().HasForeignKey(bo => bo.CountryId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(bo => bo.IsManual);
    }
}

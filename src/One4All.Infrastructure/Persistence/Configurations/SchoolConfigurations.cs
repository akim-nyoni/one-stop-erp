using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Schools;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class FeeStructureConfiguration : IEntityTypeConfiguration<FeeStructure>
{
    public void Configure(EntityTypeBuilder<FeeStructure> b)
    {
        b.ToTable("fee_structure", Schemas.School);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Grade).HasMaxLength(40);
        b.Property(x => x.Term).HasMaxLength(40);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.DeferredIncomeAccountCode).HasMaxLength(8);
        b.Ignore(x => x.DefersIncome);
        b.Ignore(x => x.CompulsoryTotal);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.FeeStructureId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class FeeItemConfiguration : IEntityTypeConfiguration<FeeItem>
{
    public void Configure(EntityTypeBuilder<FeeItem> b)
    {
        b.ToTable("fee_item", Schemas.School);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(200);
    }
}

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> b)
    {
        b.ToTable("student", Schemas.School);
        b.HasKey(x => x.Id);
        b.Property(x => x.Stream).HasMaxLength(20);
        b.Property(x => x.DailySpendLimit).HasPrecision(19, 4);
        b.Ignore(x => x.PayerCustomerId);
        b.Property(x => x.AdmissionNumber).HasMaxLength(40);
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.Grade).HasMaxLength(40);
        b.Property(x => x.BursaryType).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.AdmissionNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => new { x.CompanyId, x.Grade });
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class TransportRouteConfiguration : IEntityTypeConfiguration<TransportRoute>
{
    public void Configure(EntityTypeBuilder<TransportRoute> b)
    {
        b.ToTable("transport_route", Schemas.School);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.FeePerTerm).HasPrecision(19, 4);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class HostelConfiguration : IEntityTypeConfiguration<Hostel>
{
    public void Configure(EntityTypeBuilder<Hostel> b)
    {
        b.ToTable("hostel", Schemas.School);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.FeePerTerm).HasPrecision(19, 4);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class StudentWalletEntryConfiguration : IEntityTypeConfiguration<StudentWalletEntry>
{
    public void Configure(EntityTypeBuilder<StudentWalletEntry> b)
    {
        b.ToTable("wallet_entry", Schemas.School);
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Description).HasMaxLength(160);
        b.Property(x => x.JournalNumber).HasMaxLength(40);
        b.HasIndex(x => new { x.StudentId, x.Date });
    }
}

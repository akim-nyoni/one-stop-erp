using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Pos;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class PosTerminalConfiguration : IEntityTypeConfiguration<PosTerminal>
{
    public void Configure(EntityTypeBuilder<PosTerminal> b)
    {
        b.ToTable("terminal", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class TillSessionConfiguration : IEntityTypeConfiguration<TillSession>
{
    public void Configure(EntityTypeBuilder<TillSession> b)
    {
        b.ToTable("till_session", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.OpeningFloat).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.TerminalId, x.ZReadNumber });
    }
}

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.ToTable("sale", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.SaleNumber).HasMaxLength(40);
        b.Property(x => x.BaseCurrency).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.TotalNet);
        b.Ignore(x => x.TotalTax);
        b.Ignore(x => x.TotalGross);
        b.Ignore(x => x.NetTenderedBase);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Tenders).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Tenders).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.SaleNumber }).IsUnique();
        b.HasIndex(x => x.TillSessionId);
    }
}

internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> b)
    {
        b.ToTable("sale_line", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 6);
        b.Property(x => x.LineNet).HasPrecision(19, 4);
        b.Property(x => x.TaxAmount).HasPrecision(19, 4);
    }
}

internal sealed class SaleTenderConfiguration : IEntityTypeConfiguration<SaleTender>
{
    public void Configure(EntityTypeBuilder<SaleTender> b)
    {
        b.ToTable("sale_tender", Schemas.PointOfSale);
        b.HasIndex(x => x.StaffEmployeeId);
        b.HasKey(x => x.Id);
        b.Property(x => x.TenderType).HasConversion<int>();
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.ExchangeRate).HasPrecision(19, 6);
        b.Property(x => x.BaseAmount).HasPrecision(19, 4);
        b.Property(x => x.AccountCode).HasMaxLength(8);
    }
}

internal sealed class PosEventConfiguration : IEntityTypeConfiguration<PosEvent>
{
    public void Configure(EntityTypeBuilder<PosEvent> b)
    {
        b.ToTable("event", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.SaleNumber).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.OriginalAmount).HasPrecision(19, 4);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.Property(x => x.CustomerReference).HasMaxLength(60);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Outcome).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.Kind, x.EventDate });
        b.HasIndex(x => new { x.CompanyId, x.SaleNumber });
    }
}

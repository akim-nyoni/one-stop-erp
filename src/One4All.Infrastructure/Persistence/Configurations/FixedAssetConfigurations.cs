using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Crm;
using One4All.Core.FixedAssets;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class FixedAssetConfiguration : IEntityTypeConfiguration<FixedAsset>
{
    public void Configure(EntityTypeBuilder<FixedAsset> b)
    {
        b.ToTable("asset", Schemas.FixedAssets);
        b.HasKey(x => x.Id);
        b.Property(x => x.AssetNumber).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.AssetClass).HasMaxLength(60);
        b.Property(x => x.Custodian).HasMaxLength(120);
        b.Property(x => x.Location).HasMaxLength(120);
        b.Property(x => x.Method).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.ReducingBalanceRatePercent).HasPrecision(9, 4);
        b.Property(x => x.CostAccountCode).HasMaxLength(8);
        b.Property(x => x.AccumulatedDepreciationAccountCode).HasMaxLength(8);
        b.Property(x => x.DepreciationExpenseAccountCode).HasMaxLength(8);
        b.Ignore(x => x.NetBookValue);
        b.Ignore(x => x.DepreciableAmount);
        b.HasIndex(x => new { x.CompanyId, x.AssetNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class AssetMaintenanceConfiguration : IEntityTypeConfiguration<AssetMaintenance>
{
    public void Configure(EntityTypeBuilder<AssetMaintenance> b)
    {
        b.ToTable("asset_maintenance", Schemas.FixedAssets);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Provider).HasMaxLength(120);
        b.Property(x => x.Cost).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.AssetId, x.Date });
    }
}

internal sealed class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> b)
    {
        b.ToTable("opportunity", Schemas.Crm);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.ContactName).HasMaxLength(200);
        b.Property(x => x.Stage).HasConversion<int>();
        b.Property(x => x.ProbabilityPercent).HasPrecision(9, 4);
        b.Ignore(x => x.IsOpen);
        b.Ignore(x => x.WeightedValue);
        b.HasIndex(x => new { x.CompanyId, x.Stage });
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

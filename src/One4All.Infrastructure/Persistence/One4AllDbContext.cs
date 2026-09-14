using Microsoft.EntityFrameworkCore;
using One4All.Core.Accounting;
using One4All.Core.Audit;
using One4All.Core.Common;
using One4All.Core.Licensing;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence.Converters;

namespace One4All.Infrastructure.Persistence;

/// <summary>
/// The application's EF Core write-model context. Entity configurations are discovered from
/// this assembly. Cross-cutting conventions required by the brief are applied centrally here:
/// <list type="bullet">
///   <item><description>money amounts as <c>numeric(19,4)</c>, the default for <see cref="decimal"/>;</description></item>
///   <item><description><see cref="DateTimeOffset"/> stored as <c>timestamptz</c> (Npgsql default);</description></item>
///   <item><description>optimistic concurrency via PostgreSQL's system <c>xmin</c> column.</description></item>
/// </list>
/// Individual properties that need finer scale (rates and unit prices at <c>numeric(19,6)</c>,
/// quantities at <c>numeric(19,4)</c>) override the default in their entity configuration.
/// </summary>
public class One4AllDbContext : DbContext
{
    /// <summary>Creates the context with the given options.</summary>
    /// <param name="options">EF Core options, typically configured to use Npgsql.</param>
    public One4AllDbContext(DbContextOptions<One4AllDbContext> options)
        : base(options)
    {
    }

    /// <summary>Companies (sets of books).</summary>
    public DbSet<Company> Companies => Set<Company>();

    /// <summary>Branches / sites.</summary>
    public DbSet<Branch> Branches => Set<Branch>();

    /// <summary>Currency master.</summary>
    public DbSet<Currency> Currencies => Set<Currency>();

    /// <summary>Exchange rates.</summary>
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    /// <summary>Fiscal years.</summary>
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();

    /// <summary>Number sequences.</summary>
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    /// <summary>Users.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Roles.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Groups.</summary>
    public DbSet<Group> Groups => Set<Group>();

    /// <summary>Permission catalogue.</summary>
    public DbSet<Permission> Permissions => Set<Permission>();

    /// <summary>The append-only audit change log.</summary>
    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();

    /// <summary>Installed licence tokens (active + history).</summary>
    public DbSet<InstalledLicence> InstalledLicences => Set<InstalledLicence>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Map the xmin system column as the concurrency token for every entity deriving from Entity.
        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property<uint>(nameof(Entity.RowVersion))
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            }
        }

        // Entity type configurations (IEntityTypeConfiguration<T>) are discovered here as modules land.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(One4AllDbContext).Assembly);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Every money amount defaults to numeric(19,4). Rates/prices/quantities override per-property.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);

        // Bound string columns to a sane default; long text opts in explicitly.
        configurationBuilder.Properties<string>().HaveMaxLength(256);

        // Domain value objects mapped to primitive columns.
        configurationBuilder.Properties<CurrencyCode>().HaveConversion<CurrencyCodeConverter>().HaveMaxLength(3);
    }
}

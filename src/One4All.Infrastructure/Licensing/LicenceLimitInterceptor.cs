using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using One4All.Application.Abstractions.Licensing;
using One4All.Core.Licensing;

namespace One4All.Infrastructure.Licensing;

/// <summary>A save refused because it would take the installation past a licence limit.</summary>
public sealed class LicenceLimitException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The refusal.</param>
    public LicenceLimitException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Enforces the licence's count limits at the point of creation (brief §11.5), whichever service, screen or import adds the record: before a
/// save that adds warehouses, municipal properties, housing stands, waiting-list applicants, students or employees, the count after the save
/// is checked against the licence (module limit first, else the global one); over it, nothing is saved. Inactive when the licence is not
/// enforced.
/// </summary>
public sealed class LicenceLimitInterceptor : SaveChangesInterceptor
{
    private static readonly (Type Entity, string Key, string? Module)[] Limited =
    [
        (typeof(Core.Inventory.Warehouse), "warehouses", null),
        (typeof(Core.Municipal.Property), "properties", LicenceModules.Municipal),
        (typeof(Core.Housing.HousingStand), "stands", LicenceModules.Housing),
        (typeof(Core.Housing.HousingApplicant), "waiting_list", LicenceModules.Housing),
        (typeof(Core.Schools.Student), "students", LicenceModules.School),
        (typeof(Core.Payroll.Employee), "employees", LicenceModules.Payroll),
    ];

    private readonly ILicenceService _licence;

    /// <summary>Creates the interceptor.</summary>
    /// <param name="licence">The licence.</param>
    public LicenceLimitInterceptor(ILicenceService licence) => _licence = licence;

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Check(DbContext? context)
    {
        if (context is null || !_licence.Enforced)
        {
            return;
        }

        foreach ((Type entity, string key, string? module) in Limited)
        {
            int added = context.ChangeTracker.Entries().Count(e => e.State == EntityState.Added && e.Entity.GetType() == entity);
            if (added == 0)
            {
                continue;
            }

            int existing = entity == typeof(Core.Inventory.Warehouse) ? context.Set<Core.Inventory.Warehouse>().Count()
                : entity == typeof(Core.Municipal.Property) ? context.Set<Core.Municipal.Property>().Count()
                : entity == typeof(Core.Housing.HousingStand) ? context.Set<Core.Housing.HousingStand>().Count()
                : entity == typeof(Core.Housing.HousingApplicant) ? context.Set<Core.Housing.HousingApplicant>().Count()
                : entity == typeof(Core.Schools.Student) ? context.Set<Core.Schools.Student>().Count()
                : context.Set<Core.Payroll.Employee>().Count(e => e.Status != Core.Payroll.EmployeeStatus.Terminated);
            if (_licence.WithinLimit(key, existing + added, module) is { IsFailure: true } refused)
            {
                throw new LicenceLimitException(refused.Error.Message);
            }
        }

        CheckTransactions(context);
        CheckBudgetDepartments(context);
    }

    // Transactions a year (brief §11.3 "transactions_per_year"): journals dated in a calendar year, across the installation.
    private void CheckTransactions(DbContext context)
    {
        foreach (IGrouping<int, Core.Accounting.JournalEntry> year in context.ChangeTracker.Entries<Core.Accounting.JournalEntry>()
                     .Where(e => e.State == EntityState.Added).Select(e => e.Entity).GroupBy(j => j.PostingDate.Year))
        {
            DateOnly from = new(year.Key, 1, 1), to = new(year.Key, 12, 31);
            int existing = context.Set<Core.Accounting.JournalEntry>().IgnoreQueryFilters().Count(j => j.PostingDate >= from && j.PostingDate <= to);
            if (_licence.WithinLimit("transactions_per_year", existing + year.Count()) is { IsFailure: true } refused)
            {
                throw new LicenceLimitException(refused.Error.Message);
            }
        }
    }

    // Budget departments (brief §11.3: "budget_departments", the budgeting module's "departments"): the distinct departments budgeted for.
    private void CheckBudgetDepartments(DbContext context)
    {
        List<string> adding = [.. context.ChangeTracker.Entries<Core.Budgeting.Budget>().Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity.Department).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
        if (adding.Count == 0)
        {
            return;
        }

        List<string> existing = [.. context.Set<Core.Budgeting.Budget>().IgnoreQueryFilters().Where(b => b.Department != null).Select(b => b.Department!).Distinct().ToList()
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        int count = existing.Count + adding.Count(d => !existing.Contains(d, StringComparer.OrdinalIgnoreCase));
        foreach (Core.Common.Result check in new[] { _licence.WithinLimit("budget_departments", count), _licence.WithinLimit("departments", count, LicenceModules.Budget) })
        {
            if (check.IsFailure)
            {
                throw new LicenceLimitException(check.Error.Message);
            }
        }
    }
}

using Microsoft.EntityFrameworkCore;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Reporting;
using One4All.Reporting.Model;

namespace One4All.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds the shipped system report definitions into <c>rpt.report_definition</c> (brief §13.2). Idempotent:
/// inserts a system definition when its code is missing, and refreshes the JSON when it changes. System
/// definitions are read-only to customers, who copy them to a company/user version to edit.
/// </summary>
public sealed class ReportCatalogueSeeder
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the seeder.</summary>
    /// <param name="db">The database context.</param>
    public ReportCatalogueSeeder(One4AllDbContext db) => _db = db;

    /// <summary>Synchronises the system report catalogue.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of definitions inserted or updated.</returns>
    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        int changed = 0;
        foreach (ReportDefinition definition in SystemDefinitions())
        {
            string json = ReportDefinitionSerializer.Serialize(definition);
            ReportDefinitionRecord? existing = await _db.ReportDefinitions
                .FirstOrDefaultAsync(r => r.Code == definition.Code && r.Scope == ReportScope.System, ct)
                .ConfigureAwait(false);

            if (existing is null)
            {
                _db.ReportDefinitions.Add(new ReportDefinitionRecord(definition.Code, definition.Name, json, ReportScope.System, module: definition.Module));
                changed++;
            }
            else if (Canonicalize(existing.DefinitionJson) != json)
            {
                // The column is jsonb, which Postgres re-normalises on write, so the stored text never equals
                // the serializer's output byte-for-byte. Compare canonically (round-trip through the same
                // serializer) so an unchanged definition is not needlessly rewritten every run.
                existing.UpdateDefinition(json);
                changed++;
            }
        }

        if (changed > 0)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return changed;
    }

    /// <summary>Re-serialises stored JSON through the same serializer so jsonb normalisation is ignored.</summary>
    private static string Canonicalize(string storedJson)
    {
        try
        {
            return ReportDefinitionSerializer.Serialize(ReportDefinitionSerializer.Deserialize(storedJson));
        }
        catch (Exception)
        {
            // Unparseable stored JSON is treated as different so it gets refreshed.
            return string.Empty;
        }
    }

    private static IEnumerable<ReportDefinition> SystemDefinitions()
    {
        yield return SystemReports.TrialBalance();
        yield return SystemReports.CustomerStatement();
        yield return SystemReports.IncomeStatement();
        yield return SystemReports.BalanceSheet();
        yield return SystemReports.AgedDebtors();
        yield return SystemReports.AgedCreditors();
        yield return SystemReports.StockValuation();
        yield return SystemReports.AssetRegister();
        yield return SystemReports.PayrollSummary();
        yield return SystemReports.PropertyRegister();
        yield return SystemReports.BudgetVsActual();
        yield return SystemReports.PosSales();
        yield return SystemReports.JobProfitability();
        yield return SystemReports.ProgressCertificates();
        yield return SystemReports.HousingStands();
        yield return SystemReports.StudentRegister();
        yield return SystemReports.OpenPurchaseOrders();
        yield return SystemReports.Menu();
        yield return SystemReports.TableStatus();
        yield return SystemReports.KitchenTickets();
        yield return SystemReports.TipAllocations();
        yield return SystemReports.MenuEngineering();
        yield return SystemReports.Reservations();
        yield return SystemReports.Waitlist();
        yield return SystemReports.TableGroups();
        yield return SystemReports.FloorPlan();
        yield return SystemReports.SectionSales();
        yield return SystemReports.VoidsAndComps();
        yield return SystemReports.OrderTypeSales();
        yield return SystemReports.Deliveries();
        yield return SystemReports.ServerSales();
        yield return SystemReports.CategorySales();

        // Cashiering & Receipting catalogue (brief §RCP-16, the RC series).
        foreach (ReportDefinition definition in ReceiptingReports.All())
        {
            yield return definition;
        }
    }
}

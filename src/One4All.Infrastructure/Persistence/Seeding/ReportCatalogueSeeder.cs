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
            else if (existing.DefinitionJson != json)
            {
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

    private static IEnumerable<ReportDefinition> SystemDefinitions()
    {
        yield return SystemReports.TrialBalance();
        yield return SystemReports.CustomerStatement();
    }
}

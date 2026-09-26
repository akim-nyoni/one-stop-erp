using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.JobCosting;
using One4All.Core.Pos;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Supplies live rows for the point-of-sale and job-costing reports (POS sales summary, job profitability
/// and interim payment certificates) from their entities.
/// </summary>
public sealed class PosJobReportDataProvider : IReportDataProvider
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "PosSales", "JobProfitability", "ProgressCertificates", "JobWip",
    };

    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public PosJobReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => Commands.Contains(command);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        return command.ToUpperInvariant() switch
        {
            "POSSALES" => await PosSalesAsync(companyId, parameters, ct).ConfigureAwait(false),
            "JOBPROFITABILITY" => await JobProfitabilityAsync(companyId, ct).ConfigureAwait(false),
            "PROGRESSCERTIFICATES" => await ProgressCertificatesAsync(companyId, parameters, ct).ConfigureAwait(false),
            "JOBWIP" => await JobWipAsync(companyId, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PosSalesAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<Sale> sales = await _db.Set<Sale>().Include(s => s.Lines)
            .Where(s => s.CompanyId == companyId && s.SaleDate >= from && s.SaleDate <= to)
            .OrderBy(s => s.SaleNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        return sales.Select(s => Row(new()
        {
            ["SaleNumber"] = s.SaleNumber,
            ["SaleDate"] = s.SaleDate.ToDateTime(TimeOnly.MinValue),
            ["TotalNet"] = s.TotalNet,
            ["TotalTax"] = s.TotalTax,
            ["TotalGross"] = s.TotalGross,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> JobProfitabilityAsync(long companyId, CancellationToken ct)
    {
        List<JobContract> contracts = await _db.Set<JobContract>().Include(c => c.Costs)
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.ContractNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        return contracts.Select(c => Row(new()
        {
            ["ContractNumber"] = c.ContractNumber,
            ["Name"] = c.Name,
            ["ContractValue"] = c.ContractValue,
            ["BudgetCost"] = c.BudgetCost,
            ["ActualCost"] = c.ActualCost,
            ["CertifiedToDate"] = c.CertifiedToDate,
            ["PercentComplete"] = c.PercentComplete,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> JobWipAsync(long companyId, CancellationToken ct)
    {
        List<JobContract> contracts = await _db.Set<JobContract>().Include(c => c.Costs)
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.ContractNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        return contracts.Select(c =>
        {
            // Revenue recognised on a cost-to-cost (input) basis vs what has actually been certified/billed.
            decimal recognised = decimal.Round(c.ContractValue * c.PercentComplete / 100m, 2, MidpointRounding.ToEven);
            decimal wip = recognised - c.CertifiedToDate; // + under-billed (contract asset), − over-billed (contract liability)
            return Row(new()
            {
                ["ContractNumber"] = c.ContractNumber,
                ["Name"] = c.Name,
                ["ContractValue"] = c.ContractValue,
                ["ActualCost"] = c.ActualCost,
                ["PercentComplete"] = c.PercentComplete,
                ["RevenueRecognised"] = recognised,
                ["CertifiedToDate"] = c.CertifiedToDate,
                ["WipPosition"] = wip,
            });
        }).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ProgressCertificatesAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<ProgressCertificate> certificates = await _db.Set<ProgressCertificate>()
            .Where(c => c.CompanyId == companyId && c.Date >= from && c.Date <= to)
            .OrderBy(c => c.CertificateNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        return certificates.Select(c => Row(new()
        {
            ["CertificateNumber"] = c.CertificateNumber,
            ["Date"] = c.Date.ToDateTime(TimeOnly.MinValue),
            ["Gross"] = c.GrossThisCertificate,
            ["Retention"] = c.RetentionThisCertificate,
            ["Net"] = c.NetPayable,
        })).ToList();
    }

    private static IReadOnlyDictionary<string, object?> Row(Dictionary<string, object?> values) => values;
}

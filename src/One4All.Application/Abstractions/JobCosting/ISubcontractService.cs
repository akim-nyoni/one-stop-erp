using One4All.Core.Common;

namespace One4All.Application.Abstractions.JobCosting;

/// <summary>A subcontract certificate to post.</summary>
public sealed class SubcontractCertificateRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the subcontract.</summary>
    public long SubcontractId { get; init; }

    /// <summary>Gets the certificate number (also the supplier invoice number).</summary>
    public string Number { get; init; } = string.Empty;

    /// <summary>Gets the date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gets the cumulative value of work done.</summary>
    public decimal WorkDoneToDate { get; init; }

    /// <summary>Gets the job cost account the work is charged to.</summary>
    public string CostAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the retention payable account.</summary>
    public string RetentionPayableAccountCode { get; init; } = string.Empty;
}

/// <summary>The posted certificate.</summary>
/// <param name="Gross">The gross this certificate.</param>
/// <param name="Retention">The retention held.</param>
/// <param name="Payable">The amount invoiced to the supplier ledger.</param>
public readonly record struct SubcontractCertificateResult(decimal Gross, decimal Retention, decimal Payable);

/// <summary>Subcontracts and plant on contracts (brief §14 K 192, 194).</summary>
public interface ISubcontractService
{
    /// <summary>Lets a subcontract to a supplier on a contract.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The main contract.</param>
    /// <param name="supplierId">The subcontractor.</param>
    /// <param name="reference">The reference (unique per company).</param>
    /// <param name="description">The package.</param>
    /// <param name="value">The value.</param>
    /// <param name="retentionPercent">The retention percentage (0–100).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subcontract id, or an error.</returns>
    Task<Result<long>> LetAsync(long companyId, long contractId, long supplierId, string reference, string description, decimal value, decimal retentionPercent, CancellationToken ct = default);

    /// <summary>
    /// Certifies work: the gross this certificate is the work to date less what was certified before; the supplier ledger is invoiced the
    /// gross less retention (Dr job cost, Cr creditors) and the retention is held (Dr job cost, Cr retention payable); the job's subcontract
    /// cost head takes the gross. Work to date cannot go backwards or past the subcontract value.
    /// </summary>
    /// <param name="request">The certificate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The certificate, or an error.</returns>
    Task<Result<SubcontractCertificateResult>> CertifyAsync(SubcontractCertificateRequest request, CancellationToken ct = default);

    /// <summary>Releases held retention to the subcontractor: invoices it to the supplier ledger against the retention payable account.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="subcontractId">The subcontract.</param>
    /// <param name="amount">The amount released.</param>
    /// <param name="date">The date.</param>
    /// <param name="retentionPayableAccountCode">The retention payable account.</param>
    /// <param name="documentNumber">The release document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> ReleaseRetentionAsync(long companyId, long subcontractId, decimal amount, DateOnly date, string retentionPayableAccountCode, string documentNumber, CancellationToken ct = default);

    /// <summary>Logs plant hours on a job, charging the job's plant cost head at the plant's hire rate.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="plantItemId">The plant.</param>
    /// <param name="contractId">The job.</param>
    /// <param name="date">The day.</param>
    /// <param name="hours">The hours (0–24).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> LogPlantHoursAsync(long companyId, long plantItemId, long contractId, DateOnly date, decimal hours, CancellationToken ct = default);

    /// <summary>Logs a plant running cost (fuel, repairs, servicing) for utilisation and recovery analysis.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="plantItemId">The plant.</param>
    /// <param name="date">The day.</param>
    /// <param name="amount">The cost.</param>
    /// <param name="description">What it was.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> LogPlantCostAsync(long companyId, long plantItemId, DateOnly date, decimal amount, string description, CancellationToken ct = default);
}

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
/// <param name="AdvanceRecovered">The advance recovered from it.</param>
/// <param name="Withheld">The withholding tax deducted from it.</param>
public readonly record struct SubcontractCertificateResult(decimal Gross, decimal Retention, decimal Payable, decimal AdvanceRecovered = 0m, decimal Withheld = 0m);

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
    /// <param name="costHead">The job cost head the work is captured under (default "Subcontract").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subcontract id, or an error.</returns>
    Task<Result<long>> LetAsync(long companyId, long contractId, long supplierId, string reference, string description, decimal value, decimal retentionPercent, string? costHead = null,
        CancellationToken ct = default);

    /// <summary>
    /// Certifies work: the gross this certificate is the work to date less what was certified before; the supplier ledger is invoiced the
    /// gross less retention (Dr job cost, Cr creditors) and the retention is held (Dr job cost, Cr retention payable); the job's subcontract
    /// cost head takes the gross. Work to date cannot go backwards or past the subcontract value.
    /// </summary>
    /// <param name="request">The certificate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The certificate, or an error.</returns>
    Task<Result<SubcontractCertificateResult>> CertifyAsync(SubcontractCertificateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Pays the subcontractor an advance (mobilisation): Dr the advance account, Cr bank. A share of each later certificate's gross is
    /// recovered against it until it is cleared, reducing what is invoiced to the supplier ledger. Another advance while one is outstanding
    /// adds to it at the new recovery share.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="subcontractId">The subcontract.</param>
    /// <param name="amount">The advance.</param>
    /// <param name="recoveryPercent">The share of each certificate's gross recovered (1–100).</param>
    /// <param name="date">The payment date.</param>
    /// <param name="bankAccountCode">The bank paid from.</param>
    /// <param name="advanceAccountCode">The advances-to-subcontractors asset account.</param>
    /// <param name="documentNumber">The payment document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number.</returns>
    Task<Result<string>> PayAdvanceAsync(long companyId, long subcontractId, decimal amount, decimal recoveryPercent, DateOnly date, string bankAccountCode, string advanceAccountCode, string documentNumber,
        CancellationToken ct = default);

    /// <summary>
    /// Sets (or clears, with null) the withholding tax deducted from the subcontract's certificates. It applies to the certificate's gross
    /// unless the subcontractor's tax clearance is valid on the certificate date; the tax is owed to the revenue authority and a withholding
    /// certificate is issued, so it is returned and remitted with payment withholding. Pay such invoices without withholding again.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="subcontractId">The subcontract.</param>
    /// <param name="withholdingTaxCode">The withholding tax code, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetWithholdingAsync(long companyId, long subcontractId, string? withholdingTaxCode, CancellationToken ct = default);

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

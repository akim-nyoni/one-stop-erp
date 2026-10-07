using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>A report to burst and deliver.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="ReportCode">The report.</param>
/// <param name="KeyField">The field it is split by.</param>
public sealed record BurstRequest(long CompanyId, string ReportCode, string KeyField)
{
    /// <summary>Its parameters.</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();

    /// <summary>A field of the rows holding the email address to send each slice to.</summary>
    public string? RecipientField { get; init; }

    /// <summary>Otherwise: the key is a customer (receivables) or supplier (payables) code, sent to the contacts who receive statements / remittances.</summary>
    public TradingLedger? Ledger { get; init; }

    /// <summary>The output (PDF by default).</summary>
    public ReportFormat Format { get; init; } = ReportFormat.Pdf;

    /// <summary>Who runs it (their report rights and scope apply); null for a system run.</summary>
    public long? UserId { get; init; }
}

/// <summary>One slice's delivery.</summary>
/// <param name="Key">The key.</param>
/// <param name="Recipients">Who it went to (empty when nobody could be found).</param>
/// <param name="FileName">The attachment.</param>
/// <param name="BatchId">The despatch batch it was queued in.</param>
public sealed record BurstDelivery(string Key, IReadOnlyList<string> Recipients, string FileName, long? BatchId);

/// <summary>
/// Bursting (brief §13.3): one report run split by a key and each part emailed as its own document — statements to each customer,
/// bills to each ratepayer, remittances to each supplier — to the address in the rows or the trader's contacts.
/// </summary>
public interface IReportBurstService
{
    /// <summary>Bursts the report and queues each part to its recipients.</summary>
    /// <param name="request">What to burst and how to address it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each key's delivery (keys nobody could be found for are listed with no recipients).</returns>
    Task<Result<IReadOnlyList<BurstDelivery>>> BurstAsync(BurstRequest request, CancellationToken ct = default);
}

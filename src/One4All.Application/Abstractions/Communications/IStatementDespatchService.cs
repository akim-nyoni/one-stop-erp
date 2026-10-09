using One4All.Core.Common;

namespace One4All.Application.Abstractions.Communications;

/// <summary>What a statement despatch queued.</summary>
/// <param name="BatchId">The despatch batch, when anything was queued.</param>
/// <param name="Queued">Statements queued.</param>
/// <param name="NoEmail">Customers with a balance but no email address.</param>
/// <param name="BelowMinimum">Customers whose balance was below the minimum.</param>
public sealed record StatementDespatch(long? BatchId, int Queued, int NoEmail, int BelowMinimum);

/// <summary>
/// Bulk customer statements by email (brief §8: bulk despatch of statements): every customer owing at least the minimum
/// as at a date, with an email address, gets the statement merged into the statement template (fields Name, Account,
/// Balance, Currency, AsAt and Statement — the printed statement text) and queued as one despatch batch.
/// </summary>
public interface IStatementDespatchService
{
    /// <summary>Queues the statements.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAt">The statement date.</param>
    /// <param name="templateCode">The email template.</param>
    /// <param name="language">The language.</param>
    /// <param name="minimumBalance">The smallest balance worth a statement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was queued, or why nothing could be.</returns>
    Task<Result<StatementDespatch>> QueueAsync(long companyId, DateOnly asAt, string templateCode, string language, decimal minimumBalance, CancellationToken ct = default);

    /// <summary>Queues the statements of some customers only (a billing run's ratepayers).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAt">The statement date.</param>
    /// <param name="templateCode">The email template.</param>
    /// <param name="language">The language.</param>
    /// <param name="minimumBalance">The smallest balance worth a statement.</param>
    /// <param name="customerIds">The customers.</param>
    /// <param name="title">The despatch batch title.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was queued, or why nothing could be.</returns>
    Task<Result<StatementDespatch>> QueueAsync(long companyId, DateOnly asAt, string templateCode, string language, decimal minimumBalance, IReadOnlyCollection<long> customerIds, string title,
        CancellationToken ct = default);
}

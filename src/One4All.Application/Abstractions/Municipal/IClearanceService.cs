using One4All.Application.Municipal;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>An issued rates clearance certificate.</summary>
/// <param name="CertificateNumber">The certificate number.</param>
/// <param name="StandNumber">The property's stand number.</param>
/// <param name="IssuedOn">When it was issued.</param>
/// <param name="ValidUntil">The last day it is valid.</param>
/// <param name="AdvanceHeld">The advance charges paid.</param>
public sealed record IssuedClearance(string CertificateNumber, string StandNumber, DateOnly IssuedOn, DateOnly ValidUntil, decimal AdvanceHeld);

/// <summary>
/// Rates clearance for property transfers (brief §4.9): quotes the arrears plus months of charges in advance a property
/// must pay, and issues a certificate, valid for a set number of days, only once nothing is due.
/// </summary>
public interface IClearanceService
{
    /// <summary>Quotes what clearance needs for a property at a date.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="date">The application date.</param>
    /// <param name="monthsInAdvance">The months of charges required in advance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The quote, or why there is none.</returns>
    Task<Result<ClearanceQuote>> QuoteAsync(long companyId, long propertyId, DateOnly date, int monthsInAdvance, CancellationToken ct = default);

    /// <summary>Issues a certificate when nothing is due; refuses with the amount due otherwise.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="date">The issue date.</param>
    /// <param name="monthsInAdvance">The months of charges required in advance.</param>
    /// <param name="validDays">How long the certificate is valid.</param>
    /// <param name="userId">Who issues it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The certificate, or why it cannot be issued.</returns>
    Task<Result<IssuedClearance>> IssueAsync(long companyId, long propertyId, DateOnly date, int monthsInAdvance, int validDays, long userId, CancellationToken ct = default);
}

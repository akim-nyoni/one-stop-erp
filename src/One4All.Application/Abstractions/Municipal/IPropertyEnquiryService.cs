using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A service on the enquiry desk.</summary>
/// <param name="ServiceType">The service.</param>
/// <param name="TariffCode">The tariff it is linked to.</param>
/// <param name="Connected">Whether it is running (not cut off or ended).</param>
/// <param name="StartDate">When it started.</param>
/// <param name="EndDate">When it ended, if it has.</param>
/// <param name="MeterSerial">Its meter, if metered.</param>
/// <param name="LastReading">The meter's last reading.</param>
/// <param name="LastReadingDate">When it was read.</param>
public sealed record EnquiryService(ServiceType ServiceType, string TariffCode, bool Connected, DateOnly StartDate, DateOnly? EndDate, string? MeterSerial, decimal? LastReading, DateOnly? LastReadingDate);

/// <summary>A recent transaction on the ratepayer's account.</summary>
/// <param name="Date">The document date.</param>
/// <param name="DocumentNumber">The document.</param>
/// <param name="ItemType">Invoice, receipt, credit note and so on.</param>
/// <param name="Amount">The amount, positive for charges and negative for payments and credits.</param>
/// <param name="Outstanding">What is still open on it.</param>
public sealed record EnquiryTransaction(DateOnly Date, string DocumentNumber, OpenItemType ItemType, decimal Amount, decimal Outstanding);

/// <summary>An open disconnection or reconnection order.</summary>
/// <param name="OrderNumber">The order.</param>
/// <param name="Kind">Disconnect or reconnect.</param>
/// <param name="ServiceType">The service.</param>
/// <param name="RaisedOn">When it was raised.</param>
public sealed record EnquiryOrder(string OrderNumber, ServiceOrderKind Kind, ServiceType ServiceType, DateOnly RaisedOn);

/// <summary>A licence held by the ratepayer or for the property.</summary>
/// <param name="LicenceNumber">The licence.</param>
/// <param name="Kind">The kind.</param>
/// <param name="ExpiresOn">Its expiry.</param>
/// <param name="Status">Where it stands.</param>
public sealed record EnquiryLicence(string LicenceNumber, CouncilLicenceKind Kind, DateOnly ExpiresOn, CouncilLicenceStatus Status);

/// <summary>The ratepayer's active payment arrangement.</summary>
/// <param name="TotalAmount">The total to pay.</param>
/// <param name="InstalmentAmount">The monthly instalment.</param>
/// <param name="StartDate">The first instalment.</param>
public sealed record EnquiryArrangement(decimal TotalAmount, decimal InstalmentAmount, DateOnly StartDate);

/// <summary>Everything the enquiry desk shows about a property.</summary>
/// <param name="StandNumber">The stand.</param>
/// <param name="Ward">The ward.</param>
/// <param name="Valuation">The value in force on the enquiry date.</param>
/// <param name="RatepayerCode">The ratepayer's account.</param>
/// <param name="RatepayerName">The ratepayer's name.</param>
/// <param name="Owners">The owners on the enquiry date.</param>
/// <param name="Services">The services.</param>
/// <param name="Balance">The ratepayer's balance (negative when in credit).</param>
/// <param name="Overdue">The part of it past due, less unallocated credits.</param>
/// <param name="RecentTransactions">The latest transactions, newest first.</param>
/// <param name="IndigentSubsidyPercent">The indigent subsidy, if registered.</param>
/// <param name="Arrangement">The active payment arrangement, if any.</param>
/// <param name="OpenOrders">Open disconnection and reconnection orders.</param>
/// <param name="Licences">Licences held by the ratepayer or for the property.</param>
/// <param name="ClearanceValidUntil">The latest clearance certificate's expiry, if one is still valid.</param>
public sealed record PropertyEnquiry(
    string StandNumber, string Ward, decimal Valuation, string RatepayerCode, string RatepayerName, IReadOnlyList<OwnerEntry> Owners, IReadOnlyList<EnquiryService> Services,
    decimal Balance, decimal Overdue, IReadOnlyList<EnquiryTransaction> RecentTransactions, decimal? IndigentSubsidyPercent, EnquiryArrangement? Arrangement,
    IReadOnlyList<EnquiryOrder> OpenOrders, IReadOnlyList<EnquiryLicence> Licences, DateOnly? ClearanceValidUntil);

/// <summary>
/// The consumer enquiry desk (brief §4.9): one read-only view of a property — owners and ratepayer, services with their
/// tariffs and meters, the account balance and what is overdue, recent bills and payments, and the indigent subsidy,
/// payment arrangement, open service orders, licences and clearance on it.
/// </summary>
public interface IPropertyEnquiryService
{
    /// <summary>Builds the enquiry view of a property.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="asAtDate">The enquiry date.</param>
    /// <param name="recentTransactions">How many recent transactions to show.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The view, or why there is none.</returns>
    Task<Result<PropertyEnquiry>> EnquireAsync(long companyId, long propertyId, DateOnly asAtDate, int recentTransactions = 10, CancellationToken ct = default);
}

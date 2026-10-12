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
    IReadOnlyList<EnquiryOrder> OpenOrders, IReadOnlyList<EnquiryLicence> Licences, DateOnly? ClearanceValidUntil)
{
    /// <summary>Gets the current tenants, occupiers and managing agents.</summary>
    public IReadOnlyList<PropertyPartyEntry> Parties { get; init; } = [];

    /// <summary>Gets the correspondence address, when separate from the property.</summary>
    public string? CorrespondenceAddress { get; init; }

    /// <summary>Gets the complaints and service tickets logged against the property, newest first.</summary>
    public IReadOnlyList<EnquiryTicket> Tickets { get; init; } = [];

    /// <summary>Gets the meters' reading history, newest first.</summary>
    public IReadOnlyList<EnquiryReading> Readings { get; init; } = [];

    /// <summary>Gets the messages sent to and received from the ratepayer and the property's parties, newest first.</summary>
    public IReadOnlyList<EnquiryMessage> Correspondence { get; init; } = [];
}

/// <summary>A complaint or service ticket on the enquiry desk.</summary>
/// <param name="Reference">The ticket reference.</param>
/// <param name="Category">Its category.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="ReportedAt">When it was reported.</param>
/// <param name="Description">What was reported.</param>
public sealed record EnquiryTicket(string Reference, string Category, Core.Municipal.TicketStatus Status, DateTimeOffset ReportedAt, string Description);

/// <summary>A meter reading on the enquiry desk.</summary>
/// <param name="MeterSerial">The meter.</param>
/// <param name="Date">The reading date.</param>
/// <param name="Reading">The register reading.</param>
/// <param name="Consumption">The consumption since the previous reading.</param>
/// <param name="Kind">Actual or estimated.</param>
/// <param name="Accepted">Whether it passed validation.</param>
public sealed record EnquiryReading(string MeterSerial, DateOnly Date, decimal Reading, decimal Consumption, Core.Municipal.MeterReadingKind Kind, bool Accepted);

/// <summary>A message to or from the ratepayer or a party of the property.</summary>
/// <param name="At">When it was sent or received.</param>
/// <param name="Outgoing">Whether we sent it.</param>
/// <param name="Channel">E-mail, SMS, WhatsApp …</param>
/// <param name="Party">The address it went to or came from.</param>
/// <param name="Subject">The subject, or the start of the text.</param>
/// <param name="Status">Its delivery status (outgoing) or Received.</param>
public sealed record EnquiryMessage(DateTimeOffset At, bool Outgoing, string Channel, string Party, string Subject, string Status);

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

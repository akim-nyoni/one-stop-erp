using One4All.Application.MasterData;
using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.MasterData;

/// <summary>A new company to set up for one or more industries.</summary>
public sealed class IndustrySetupRequest
{
    /// <summary>Gets the company code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the company name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the base currency.</summary>
    public CurrencyCode BaseCurrency { get; init; }

    /// <summary>Gets the industries (Core is always included).</summary>
    public IReadOnlyList<Vertical> Verticals { get; init; } = [];

    /// <summary>Gets the first day of the first fiscal year (twelve monthly periods, opened).</summary>
    public DateOnly FiscalYearStart { get; init; }

    /// <summary>Gets the standard VAT rate.</summary>
    public decimal VatRatePercent { get; init; } = 15m;

    /// <summary>Gets a value indicating whether to add demonstration customers, suppliers and items.</summary>
    public bool WithDemoData { get; init; }
}

/// <summary>What a setup created (anything already there is left as it was and not counted).</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Accounts">Ledger accounts added.</param>
/// <param name="TaxCodes">Tax codes added.</param>
/// <param name="Warehouses">Warehouses added.</param>
/// <param name="Roles">Starter roles added.</param>
/// <param name="DemoRecords">Demonstration customers, suppliers and items added.</param>
public sealed record IndustrySetupResult(long CompanyId, int Accounts, int TaxCodes, int Warehouses, int Roles, int DemoRecords, int ItemCategories = 0, int Terminals = 0,
    int FeaturedReports = 0, int Layouts = 0);

/// <summary>
/// The Industry Setup Wizard (brief §12): creates a company for chosen industries - chart of accounts, tax codes, warehouses, a fiscal
/// year opened for posting, starter roles and optional demo data - and adds further industries later. Industries add to one another;
/// anything that already exists is kept.
/// </summary>
public interface IIndustrySetupService
{
    /// <summary>Creates and sets up a company.</summary>
    /// <param name="request">The setup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was created.</returns>
    Task<Result<IndustrySetupResult>> SetUpCompanyAsync(IndustrySetupRequest request, CancellationToken ct = default);

    /// <summary>Adds an industry to an existing company.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="vertical">The industry.</param>
    /// <param name="vatRatePercent">The standard VAT rate for any VAT code it adds.</param>
    /// <param name="withDemoData">Whether to add its demo data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was added.</returns>
    Task<Result<IndustrySetupResult>> AddVerticalAsync(long companyId, Vertical vertical, decimal vatRatePercent = 15m, bool withDemoData = false, CancellationToken ct = default);
}

using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.System;

namespace One4All.Application.Abstractions.MasterData;

/// <summary>A new ledger account.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Code">The account code.</param>
/// <param name="Name">The name.</param>
/// <param name="Type">The account type.</param>
/// <param name="ControlType">Makes it a control account (posted only by its ledger), if given.</param>
/// <param name="ReportCategory">The reporting category, if not the type's default.</param>
public sealed record NewAccount(long CompanyId, string Code, string Name, AccountType Type, ControlAccountType? ControlType = null, string? ReportCategory = null, bool BudgetEnabled = false);

/// <summary>A new tax code with its first rate.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Code">The code.</param>
/// <param name="Name">The name.</param>
/// <param name="Type">The tax type.</param>
/// <param name="RatePercent">The rate.</param>
/// <param name="EffectiveFrom">When the rate starts.</param>
/// <param name="OutputAccountCode">The output (or withholding payable) account.</param>
/// <param name="InputAccountCode">The input account, if recoverable.</param>
public sealed record NewTaxCode(long CompanyId, string Code, string Name, TaxType Type, decimal RatePercent, DateOnly EffectiveFrom, string? OutputAccountCode, string? InputAccountCode);

/// <summary>A new stock or service item.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Code">The item code.</param>
/// <param name="Description">The description.</param>
/// <param name="Type">Stock, service and so on.</param>
/// <param name="RevenueAccountCode">Where its sales post.</param>
/// <param name="CostOfSalesAccountCode">Where its cost of sales posts.</param>
/// <param name="StandardCost">A standard cost, if standard-costed.</param>
/// <param name="Costing">How its stock is costed (brief §3.6): weighted average, FIFO, standard or specific identification.</param>
/// <param name="Tracking">Batch or serial tracking, if any.</param>
/// <param name="ShelfLifeDays">Days from receipt to expiry for a batch received without one.</param>
/// <param name="PriceVarianceAccountCode">Where purchase price variances go (standard costing).</param>
/// <param name="UsageVarianceAccountCode">Where usage variances go (standard costing).</param>
/// <param name="Category">The item category.</param>
public sealed record NewItem(long CompanyId, string Code, string Description, ItemType Type, string? RevenueAccountCode, string? CostOfSalesAccountCode, decimal? StandardCost = null,
    CostingMethod Costing = CostingMethod.WeightedAverage, ItemTracking Tracking = ItemTracking.None, int? ShelfLifeDays = null, string? PriceVarianceAccountCode = null,
    string? UsageVarianceAccountCode = null, string? Category = null);

/// <summary>
/// Creates the master data the rest of the system runs on (brief §4.17 System Manager): companies, branches, fiscal years, the chart of
/// accounts, tax codes, currencies and rates, warehouses, cost centres and departments, customers, suppliers and items. Every code is
/// trimmed, upper-cased and unique in its scope (<c>Master.Duplicate</c>); referenced accounts must exist (<c>Master.AccountNotFound</c>).
/// </summary>
public interface IMasterDataService
{
    /// <summary>Creates a company with a head-office branch; held to the licence's company limit.</summary>
    /// <param name="code">The company code.</param>
    /// <param name="name">The name.</param>
    /// <param name="baseCurrency">The base currency (must exist).</param>
    /// <param name="timeZoneId">The time zone.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The company id.</returns>
    Task<Result<long>> CreateCompanyAsync(string code, string name, CurrencyCode baseCurrency, string timeZoneId = "UTC", CancellationToken ct = default);

    /// <summary>Creates a branch.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The branch id.</returns>
    Task<Result<long>> CreateBranchAsync(long companyId, string code, string name, CancellationToken ct = default);

    /// <summary>Creates a fiscal year of monthly periods from its start (periods start closed; open them with the period service).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The year code (e.g. 2027).</param>
    /// <param name="startDate">The first day.</param>
    /// <param name="months">How many monthly periods (1–24, usually 12).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The fiscal year id.</returns>
    Task<Result<long>> CreateFiscalYearAsync(long companyId, string code, DateOnly startDate, int months = 12, CancellationToken ct = default);

    /// <summary>Creates a fiscal year on a calendar: monthly periods, or 4-4-5 weeks (52 weeks in 12 periods); optionally with an adjustment period.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The year code.</param>
    /// <param name="startDate">The first day.</param>
    /// <param name="calendar">Monthly or 4-4-5.</param>
    /// <param name="months">Monthly periods (monthly calendar only).</param>
    /// <param name="adjustmentPeriod">Whether to add the adjustment ("13th") period on the last day.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The year id.</returns>
    Task<Result<long>> CreateFiscalYearAsync(long companyId, string code, DateOnly startDate, Core.System.FiscalCalendar calendar, int months, bool adjustmentPeriod, CancellationToken ct = default);

    /// <summary>Adds a ledger account.</summary>
    /// <param name="account">The account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account id.</returns>
    Task<Result<long>> CreateAccountAsync(NewAccount account, CancellationToken ct = default);

    /// <summary>Adds a tax code with its first rate.</summary>
    /// <param name="taxCode">The tax code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tax code id.</returns>
    Task<Result<long>> CreateTaxCodeAsync(NewTaxCode taxCode, CancellationToken ct = default);

    /// <summary>Adds a currency.</summary>
    /// <param name="code">The ISO code.</param>
    /// <param name="name">The name.</param>
    /// <param name="symbol">The symbol.</param>
    /// <param name="decimalPlaces">Minor-unit places.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> CreateCurrencyAsync(CurrencyCode code, string name, string symbol, int decimalPlaces = 2, CancellationToken ct = default);

    /// <summary>Sets an exchange rate from a date (replacing one already set for that day and type).</summary>
    /// <param name="from">From currency.</param>
    /// <param name="to">To currency.</param>
    /// <param name="type">The rate type.</param>
    /// <param name="effectiveFrom">The date.</param>
    /// <param name="rate">The rate (positive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetExchangeRateAsync(CurrencyCode from, CurrencyCode to, ExchangeRateType type, DateOnly effectiveFrom, decimal rate, CancellationToken ct = default);

    /// <summary>Adds a warehouse.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The warehouse id.</returns>
    Task<Result<long>> CreateWarehouseAsync(long companyId, string code, string name, CancellationToken ct = default);

    /// <summary>Adds a cost centre (code of at most three characters, as GL analysis carries it).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The id.</returns>
    Task<Result<long>> CreateCostCentreAsync(long companyId, string code, string name, CancellationToken ct = default);

    /// <summary>Adds a department (code of at most three characters).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The id.</returns>
    Task<Result<long>> CreateDepartmentAsync(long companyId, string code, string name, CancellationToken ct = default);

    /// <summary>Adds a fund (vote), or renames an existing one and sets whether it is restricted and active.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="isRestricted">Whether its money is ring-fenced.</param>
    /// <param name="isActive">Whether it takes postings.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The fund id, or why not.</returns>
    Task<Result<long>> SaveFundAsync(long companyId, string code, string name, bool isRestricted, bool isActive = true, CancellationToken ct = default);

    /// <summary>Adds a project, or renames an existing one and sets its fund and whether it is open.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="fundCode">The fund financing it, if one (must exist).</param>
    /// <param name="isActive">Whether it takes postings (false closes it).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The project id, or why not.</returns>
    Task<Result<long>> SaveProjectAsync(long companyId, string code, string name, string? fundCode, bool isActive = true, CancellationToken ct = default);

    /// <summary>Adds a customer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The account code.</param>
    /// <param name="name">The name.</param>
    /// <param name="currency">The trading currency.</param>
    /// <param name="creditLimit">The credit limit (raised later only through approval).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The customer id.</returns>
    Task<Result<long>> CreateCustomerAsync(long companyId, string code, string name, CurrencyCode currency, decimal creditLimit = 0m, CancellationToken ct = default);

    /// <summary>Adds a supplier.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The account code.</param>
    /// <param name="name">The name.</param>
    /// <param name="currency">The trading currency.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The supplier id.</returns>
    Task<Result<long>> CreateSupplierAsync(long companyId, string code, string name, CurrencyCode currency, CancellationToken ct = default);

    /// <summary>Adds an item.</summary>
    /// <param name="item">The item.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The item id.</returns>
    Task<Result<long>> CreateItemAsync(NewItem item, CancellationToken ct = default);
}

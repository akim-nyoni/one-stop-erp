using Microsoft.EntityFrameworkCore;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.AccountingTests;

/// <summary>Seeds a minimal but realistic company, fiscal calendar and chart of accounts for posting tests.</summary>
public static class Scenario
{
    /// <summary>USD base currency.</summary>
    public static readonly CurrencyCode Usd = CurrencyCode.Parse("USD");

    /// <summary>EUR transaction currency (for multi-currency tests).</summary>
    public static readonly CurrencyCode Eur = CurrencyCode.Parse("EUR");

    /// <summary>Seeds the scenario and returns the created company id.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="openPeriods">Whether to open all periods for the GL/AR/POS modules.</param>
    /// <returns>The company id.</returns>
    public static async Task<long> SeedAsync(One4AllDbContext db, bool openPeriods = true)
    {
        // Currencies and the FX rate are global reference data shared by the collection's single
        // database — seed them only once.
        if (!await EntityFrameworkQueryableExtensions.AnyAsync(db.Currencies, c => c.Code == Usd))
        {
            db.Currencies.Add(new Currency(Usd, "US Dollar", "$"));
        }

        if (!await EntityFrameworkQueryableExtensions.AnyAsync(db.Currencies, c => c.Code == Eur))
        {
            db.Currencies.Add(new Currency(Eur, "Euro", "€"));
        }

        if (!await EntityFrameworkQueryableExtensions.AnyAsync(db.ExchangeRates, r => r.FromCurrency == Eur && r.ToCurrency == Usd && r.RateType == ExchangeRateType.Daily))
        {
            db.ExchangeRates.Add(new ExchangeRate(Eur, Usd, ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 1.10m));
        }

        if (!await EntityFrameworkQueryableExtensions.AnyAsync(db.ExchangeRates, r => r.FromCurrency == Eur && r.ToCurrency == Usd && r.RateType == ExchangeRateType.ClosingRate))
        {
            db.ExchangeRates.Add(new ExchangeRate(Eur, Usd, ExchangeRateType.ClosingRate, new DateOnly(2026, 6, 30), 1.20m));
        }

        await db.SaveChangesAsync();

        // Each test gets its own company (unique code) so tests on the shared database don't collide.
        string code = "TC" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        Company company = new(code, "Test Trading Co", Usd, "UTC");
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        FiscalYear year = new(company.Id, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        for (int month = 1; month <= 12; month++)
        {
            DateOnly start = new(2026, month, 1);
            DateOnly end = start.AddMonths(1).AddDays(-1);
            FiscalPeriod period = year.AddPeriod(month, start.ToString("MMMM"), start, end);
            if (openPeriods)
            {
                foreach (string module in new[] { "GL", "AR", "AP", "CB", "POS", "INV", "POP", "VOU", "RES" })
                {
                    period.SetStatus(module, PeriodStatus.Open);
                }
            }
        }

        db.FiscalYears.Add(year);

        // Chart of accounts.
        db.Accounts.Add(new LedgerAccount(company.Id, "1000", "Cash on Hand", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(company.Id, "1100", "Bank", AccountType.Asset));

        LedgerAccount arControl = new(company.Id, "1200", "Accounts Receivable Control", AccountType.Asset);
        arControl.MakeControlAccount(ControlAccountType.AccountsReceivable);
        db.Accounts.Add(arControl);

        LedgerAccount vatOutput = new(company.Id, "2200", "VAT Output", AccountType.Liability);
        vatOutput.MakeControlAccount(ControlAccountType.VatOutput);
        db.Accounts.Add(vatOutput);

        LedgerAccount apControl = new(company.Id, "2100", "Accounts Payable Control", AccountType.Liability);
        apControl.MakeControlAccount(ControlAccountType.AccountsPayable);
        db.Accounts.Add(apControl);

        LedgerAccount stockControl = new(company.Id, "1400", "Inventory", AccountType.Asset);
        stockControl.MakeControlAccount(ControlAccountType.Stock);
        db.Accounts.Add(stockControl);

        LedgerAccount grni = new(company.Id, "2150", "Goods Received Not Invoiced", AccountType.Liability);
        grni.MakeControlAccount(ControlAccountType.GoodsReceivedNotInvoiced);
        db.Accounts.Add(grni);

        db.Accounts.Add(new LedgerAccount(company.Id, "4000", "Sales", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(company.Id, "5000", "Cost of Sales", AccountType.Expense));
        db.Accounts.Add(new LedgerAccount(company.Id, "3200", "Retained Earnings", AccountType.Equity));
        db.Accounts.Add(new LedgerAccount(company.Id, "2300", "Gift Card Liability", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(company.Id, "4900", "Breakage Income", AccountType.Income));

        // Housing / land inventory (IAS 2): WIP → stands available → cost of land sales.
        db.Accounts.Add(new LedgerAccount(company.Id, "1450", "Land & Housing Stock (WIP)", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(company.Id, "1460", "Stands Available for Sale", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(company.Id, "4100", "Land & Housing Sales", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(company.Id, "5100", "Cost of Land Sales", AccountType.Expense));
        db.Accounts.Add(new LedgerAccount(company.Id, "4200", "School Fees Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(company.Id, "4300", "Contract Revenue", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(company.Id, "1250", "Retention Receivable", AccountType.Asset));

        // Fixed assets: cost, accumulated depreciation (contra-asset), expense, disposal gain/loss.
        db.Accounts.Add(new LedgerAccount(company.Id, "1600", "Motor Vehicles at Cost", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(company.Id, "1650", "Accumulated Depreciation", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(company.Id, "6000", "Depreciation Expense", AccountType.Expense));
        db.Accounts.Add(new LedgerAccount(company.Id, "7300", "Profit on Disposal", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(company.Id, "7400", "Loss on Disposal", AccountType.Expense));

        // Payroll: gross wages expense, statutory/deduction liabilities and net-pay control.
        db.Accounts.Add(new LedgerAccount(company.Id, "6100", "Salaries & Wages", AccountType.Expense));
        db.Accounts.Add(new LedgerAccount(company.Id, "2400", "PAYE Payable", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(company.Id, "2410", "Pension Payable", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(company.Id, "2420", "Net Pay Control", AccountType.Liability));

        // Receipting: sundry income, deposits, suspense and cash shortages/overages.
        db.Accounts.Add(new LedgerAccount(company.Id, "4400", "Sundry Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(company.Id, "2500", "Consumer Deposits", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(company.Id, "2600", "Suspense / Unallocated Control", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(company.Id, "7500", "Cash Shortages & Overages", AccountType.Expense));
        db.Accounts.Add(new LedgerAccount(company.Id, "1050", "Cash in Transit", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(company.Id, "1010", "Cash in Safe", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(company.Id, "6300", "Petty Cash Expense", AccountType.Expense));
        db.Accounts.Add(new LedgerAccount(company.Id, "4600", "Service Charge Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(company.Id, "2450", "Tips Payable", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(company.Id, "4700", "Discounts Allowed", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(company.Id, "5200", "Stock Wastage", AccountType.Expense));
        db.Accounts.Add(new LedgerAccount(company.Id, "1300", "Agent Cash Control", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(company.Id, "6200", "Agent Commission", AccountType.Expense));

        // Foreign-currency bank (EUR) for FX revaluation, plus FX gain/loss control accounts.
        LedgerAccount foreignBank = new(company.Id, "1150", "Bank (EUR)", AccountType.Asset) { CurrencyRestriction = Eur };
        db.Accounts.Add(foreignBank);

        LedgerAccount fxGain = new(company.Id, "7100", "FX Gain", AccountType.Income);
        fxGain.MakeControlAccount(ControlAccountType.FxGain);
        db.Accounts.Add(fxGain);

        LedgerAccount fxLoss = new(company.Id, "7200", "FX Loss", AccountType.Expense);
        fxLoss.MakeControlAccount(ControlAccountType.FxLoss);
        db.Accounts.Add(fxLoss);

        await db.SaveChangesAsync();
        return company.Id;
    }
}

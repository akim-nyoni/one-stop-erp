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
                period.SetStatus("GL", PeriodStatus.Open);
                period.SetStatus("AR", PeriodStatus.Open);
                period.SetStatus("POS", PeriodStatus.Open);
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

        db.Accounts.Add(new LedgerAccount(company.Id, "4000", "Sales", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(company.Id, "5000", "Cost of Sales", AccountType.Expense));
        db.Accounts.Add(new LedgerAccount(company.Id, "3200", "Retained Earnings", AccountType.Equity));

        await db.SaveChangesAsync();
        return company.Id;
    }
}

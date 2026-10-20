using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.MasterData;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers creating a company from nothing through the master data service: company and head office, a fiscal year opened for posting,
/// the chart of accounts, a tax code, dimensions, traders and an item, with duplicates and missing references refused, and a journal
/// posted in the new company.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class MasterDataTests
{
    private readonly AccountingFixture _fixture;

    public MasterDataTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_company_is_set_up_from_nothing_and_can_post()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await Scenario.SeedAsync(db); // currencies
        IMasterDataService master = scope.ServiceProvider.GetRequiredService<IMasterDataService>();
        string code = "MD" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        (await master.CreateCompanyAsync(code, "New Co", CurrencyCode.Parse("XXQ"))).Error.Code.Should().Be("Master.CurrencyNotFound");
        long companyId = (await master.CreateCompanyAsync(code.ToLowerInvariant(), "New Co", Scenario.Usd)).Value;
        (await master.CreateCompanyAsync(code, "Again", Scenario.Usd)).Error.Code.Should().Be("Master.Duplicate");
        db.Branches.Should().ContainSingle(b => b.CompanyId == companyId && b.IsHeadOffice);

        long year = (await master.CreateFiscalYearAsync(companyId, "2026", new DateOnly(2026, 1, 1))).Value;
        (await master.CreateFiscalYearAsync(companyId, "2026B", new DateOnly(2026, 7, 1))).Error.Code.Should().Be("Master.YearOverlaps");
        (await scope.ServiceProvider.GetRequiredService<IPeriodService>().OpenYearAsync(companyId, year, ["GL"])).IsSuccess.Should().BeTrue();

        (await master.CreateAccountAsync(new NewAccount(companyId, "1100", "Bank", AccountType.Asset))).IsSuccess.Should().BeTrue();
        (await master.CreateAccountAsync(new NewAccount(companyId, "1100", "Bank again", AccountType.Asset))).Error.Code.Should().Be("Master.Duplicate");
        (await master.CreateAccountAsync(new NewAccount(companyId, "1200", "Debtors", AccountType.Asset, ControlAccountType.AccountsReceivable))).IsSuccess.Should().BeTrue();
        (await master.CreateAccountAsync(new NewAccount(companyId, "2200", "VAT output", AccountType.Liability, ControlAccountType.VatOutput))).IsSuccess.Should().BeTrue();
        (await master.CreateAccountAsync(new NewAccount(companyId, "4000", "Sales", AccountType.Income))).IsSuccess.Should().BeTrue();
        (await master.CreateAccountAsync(new NewAccount(companyId, "123456789", "Too long", AccountType.Income))).Error.Code.Should().Be("Master.Invalid");

        (await master.CreateTaxCodeAsync(new NewTaxCode(companyId, "VAT15", "VAT 15%", TaxType.Standard, 15m, new DateOnly(2026, 1, 1), "2299", null))).Error.Code.Should().Be("Master.AccountNotFound");
        (await master.CreateTaxCodeAsync(new NewTaxCode(companyId, "VAT15", "VAT 15%", TaxType.Standard, 15m, new DateOnly(2026, 1, 1), "2200", null))).IsSuccess.Should().BeTrue();
        (await master.CreateWarehouseAsync(companyId, "main", "Main store")).IsSuccess.Should().BeTrue();
        (await master.CreateCostCentreAsync(companyId, "ADMN", "Too long")).Error.Code.Should().Be("Master.Invalid");
        (await master.CreateCostCentreAsync(companyId, "ADM", "Administration")).IsSuccess.Should().BeTrue();
        (await master.CreateDepartmentAsync(companyId, "FIN", "Finance")).IsSuccess.Should().BeTrue();
        (await master.CreateCustomerAsync(companyId, "c001", "First customer", Scenario.Usd, 1_000m)).IsSuccess.Should().BeTrue();
        (await master.CreateSupplierAsync(companyId, "S001", "First supplier", Scenario.Usd)).IsSuccess.Should().BeTrue();
        (await master.CreateItemAsync(new NewItem(companyId, "WIDGET", "Widget", ItemType.Stock, "4000", "5000"))).Error.Code.Should().Be("Master.AccountNotFound");
        (await master.CreateItemAsync(new NewItem(companyId, "WIDGET", "Widget", ItemType.Stock, "4000", null))).IsSuccess.Should().BeTrue();
        (await master.CreateItemAsync(new NewItem(companyId, "STD-1", "Standard part", ItemType.Stock, "4000", null, Costing: CostingMethod.Standard))).Error.Code.Should().NotBeNullOrEmpty();
        long batch = (await master.CreateItemAsync(new NewItem(companyId, "VAX-1", "Vaccine", ItemType.Stock, "4000", null, Costing: CostingMethod.Fifo, Tracking: ItemTracking.Batch, ShelfLifeDays: 180,
            Category: "pharma"))).Value;
        Item vaccine = await db.Items.AsNoTracking().SingleAsync(x => x.Id == batch);
        (vaccine.CostingMethod, vaccine.Tracking, vaccine.ShelfLifeDays, vaccine.Category).Should().Be((CostingMethod.Fifo, ItemTracking.Batch, 180, "PHARMA"));

        // An item keyed by name, unit and barcode alone takes its accounts from another item in the category; a barcode belongs to one item.
        long syringe = (await master.CreateItemAsync(new NewItem(companyId, "SYR-1", "Syringe", ItemType.Stock, null, null, Category: "Pharma", Unit: "box", Barcode: "6001234567890"))).Value;
        Item boxed = await db.Items.AsNoTracking().SingleAsync(x => x.Id == syringe);
        (boxed.RevenueAccountCode, boxed.UnitOfMeasure, boxed.Barcode).Should().Be(("4000", "BOX", "6001234567890"));
        (await master.CreateItemAsync(new NewItem(companyId, "SYR-2", "Syringe 2", ItemType.Stock, null, null, Barcode: "6001234567890"))).Error.Code.Should().Be("Master.BarcodeInUse");

        // The new company posts.
        (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 2, 1), TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false,
            Lines = [new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 50m }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 50m }],
        })).IsSuccess.Should().BeTrue();

        // A currency and a rate (currencies are shared, so the code may already exist from an earlier run).
        Core.Common.Result currency = await master.CreateCurrencyAsync(CurrencyCode.Parse("ZWG"), "Zimbabwe Gold", "ZiG");
        (currency.IsSuccess || currency.Error.Code == "Master.Duplicate").Should().BeTrue();
        (await master.SetExchangeRateAsync(CurrencyCode.Parse("ZWG"), Scenario.Usd, ExchangeRateType.Spot, new DateOnly(2026, 2, 1), 0.037m)).IsSuccess.Should().BeTrue();
        (await master.SetExchangeRateAsync(CurrencyCode.Parse("ZWG"), Scenario.Usd, ExchangeRateType.Spot, new DateOnly(2026, 2, 1), 0.038m)).IsSuccess.Should().BeTrue();
        db.ExchangeRates.Count(r => r.FromCurrency == CurrencyCode.Parse("ZWG") && r.EffectiveFrom == new DateOnly(2026, 2, 1) && r.RateType == ExchangeRateType.Spot).Should().Be(1);
    }
}

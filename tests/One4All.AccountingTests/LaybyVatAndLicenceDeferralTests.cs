using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers two revenue rules: a completed lay-bye at tax-inclusive prices accounts for its output tax when the sale is made; and an annual council
/// licence fee is held in deferred income and earned month by month over the licence year.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LaybyVatAndLicenceDeferralTests
{
    private readonly AccountingFixture _fixture;

    public LaybyVatAndLicenceDeferralTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_completed_layby_accounts_for_output_tax_and_licence_fees_are_earned_over_the_year()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "2486", "Lay-bye Deposits", AccountType.Liability), new LedgerAccount(companyId, "2485", "Licence Fees in Advance", AccountType.Liability),
            new LedgerAccount(companyId, "4985", "Licence Fees", AccountType.Income));
        TaxCode vat = new(companyId, "LVAT", "VAT 15%", TaxType.Standard) { OutputAccountCode = "2200" };
        vat.AddRate(new DateOnly(2020, 1, 1), 15m);
        db.Set<TaxCode>().Add(vat);
        Item radio = new(companyId, "LV-RADIO", "Radio") { CostOfSalesAccountCode = "5000" };
        Warehouse shop = new(companyId, "LVSH", "Shop");
        Customer customer = new(companyId, "LV-C", "Lay-bye Buyer", Scenario.Usd);
        db.AddRange(radio, shop, customer);
        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = radio.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 3, 1), Quantity = 2m, UnitCost = 400m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        ILaybyService laybys = scope.ServiceProvider.GetRequiredService<ILaybyService>();
        LaybyRequest Plan(string number, string? taxCode) => new()
        {
            CompanyId = companyId, CustomerId = customer.Id, PlanNumber = number, WarehouseId = shop.Id, OpenedOn = new DateOnly(2026, 3, 2), FinalDate = new DateOnly(2026, 4, 30),
            Deposit = 115m, MinimumDepositPercent = 10m, ForfeitPercent = 0m, BankAccountCode = "1100", DepositAccountCode = "2486", RevenueAccountCode = "4000",
            ForfeitureAccountCode = "4000", Lines = [new LaybyLineRequest(radio.Id, 1m, 1150m)], TaxCode = taxCode,
        };
        (await laybys.OpenAsync(Plan("LV-0", "NOPE"))).Error.Code.Should().Be("Layby.TaxCode");
        long plan = (await laybys.OpenAsync(Plan("LV-1", "lvat"))).Value;
        (await laybys.PayAsync(companyId, plan, new DateOnly(2026, 3, 20), 1035m, "1100")).Value.Completed.Should().BeTrue();
        List<JournalLine> completion = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.Narrative == "Lay-bye LV-1 completed").SelectMany(j => j.Lines).ToListAsync();
        completion.Single(l => l.AccountCode == "2200").BaseCredit.Should().Be(150m, "1,150 includes 150 of VAT at 15%");
        completion.Single(l => l.AccountCode == "4000").BaseCredit.Should().Be(1000m);

        // A year's trading licence: the fee is held and earned a twelfth a month.
        ICouncilLicensingService licences = scope.ServiceProvider.GetRequiredService<ICouncilLicensingService>();
        IssuedLicence issued = (await licences.IssueAsync(new IssueLicenceRequest
        {
            CompanyId = companyId, Kind = CouncilLicenceKind.Business, HolderCustomerId = customer.Id, Description = "Corner shop", IssuedOn = new DateOnly(2026, 1, 1), CycleMonths = 12,
            Fee = 1200m, RevenueAccountCode = "4985", DeferredIncomeAccountCode = "2485",
        })).Value;
        decimal Balance(string account) => db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == account)
            .Sum(l => l.BaseCredit - l.BaseDebit);
        Balance("2485").Should().Be(1200m);
        Balance("4985").Should().Be(0m);
        (await scope.ServiceProvider.GetRequiredService<IDeferredIncomeService>().RecogniseAsync(companyId, new DateOnly(2026, 3, 31))).IsSuccess.Should().BeTrue();
        Balance("4985").Should().Be(295.89m, "January to March is 90 of the licence year's 365 days");
        (await db.Set<CouncilLicence>().AsNoTracking().SingleAsync(l => l.CompanyId == companyId && l.LicenceNumber == issued.LicenceNumber)).DeferredIncomeAccountCode.Should().Be("2485");
    }
}

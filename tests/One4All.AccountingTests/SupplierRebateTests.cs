using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Procurement;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers supplier rebate accrual: on a 1% / 2%-from-1,000 agreement, 600 bought by February accrues 6; another 500 in
/// March crosses the tier, so the rebate on all 1,100 is 22 and March books 16; a rerun books nothing; a 200 credit note
/// takes spend back under the tier, so the rebate falls to 9 and 13 is reversed; another supplier's purchases count for
/// nothing.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierRebateAccountingTests
{
    private readonly AccountingFixture _fixture;

    public SupplierRebateAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Rebates_accrue_on_purchases_and_follow_the_tiers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "1490", "Rebates Receivable", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(companyId, "5190", "Purchase Rebates", AccountType.Expense));
        Supplier supplier = new(companyId, "RB-SUP", "Rebating Wholesaler", Scenario.Usd);
        Supplier other = new(companyId, "RB-OTH", "No Agreement", Scenario.Usd);
        db.Suppliers.AddRange(supplier, other);
        await db.SaveChangesAsync();

        IRebateService rebates = scope.ServiceProvider.GetRequiredService<IRebateService>();
        (await rebates.CreateAsync(companyId, new RebateAgreementRequest(supplier.Id, "RB-2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            [new RebateTier(0m, 1m), new RebateTier(1_000m, 2m)]))).IsSuccess.Should().BeTrue();

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        async Task Buy(Supplier from, string number, DateOnly date, decimal amount) => (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = from.Id, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Usd, NetAmount = amount, ExpenseAccountCode = "6800",
        })).IsSuccess.Should().BeTrue();

        await Buy(supplier, "RB-I1", new DateOnly(2026, 2, 10), 600m);
        await Buy(other, "RB-X1", new DateOnly(2026, 2, 11), 5_000m);
        (await rebates.AccrueAsync(companyId, new DateOnly(2026, 2, 28), "1490", "5190")).Value.Should().Equal(new RebatePosition("RB-2026", 600m, 1m, 6m, 6m));

        await Buy(supplier, "RB-I2", new DateOnly(2026, 3, 10), 500m);
        (await rebates.AccrueAsync(companyId, new DateOnly(2026, 3, 31), "1490", "5190")).Value.Should().Equal(new RebatePosition("RB-2026", 1_100m, 2m, 22m, 16m));
        (await rebates.AccrueAsync(companyId, new DateOnly(2026, 3, 31), "1490", "5190")).Value.Single().Booked.Should().Be(0m);

        (await ap.PostCreditNoteAsync(new PurchaseCreditNoteRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "RB-C1", DocumentDate = new DateOnly(2026, 4, 5), Currency = Scenario.Usd, NetAmount = 200m, ExpenseAccountCode = "6800",
        })).IsSuccess.Should().BeTrue();
        (await rebates.AccrueAsync(companyId, new DateOnly(2026, 4, 30), "1490", "5190")).Value.Should().Equal(new RebatePosition("RB-2026", 900m, 1m, 9m, -13m));

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 4, 30))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "1490").Debit.Should().Be(9m);
        tb.Rows.Single(r => r.AccountCode == "5190").Credit.Should().Be(9m);
    }
}

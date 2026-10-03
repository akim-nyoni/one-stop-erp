using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers sales commission: a rep paid 5% on invoiced revenue earns 50 on a 1,000 + 150 VAT invoice (tax left out); a
/// rep paid 2% on collections earns 6 on a 300 receipt but nothing on a write-off; an unassigned customer earns nobody
/// anything. The March accrual books 56, a rerun books nothing, and a late 200 invoice adds 10 on the next run.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SalesCommissionTests
{
    private readonly AccountingFixture _fixture;

    public SalesCommissionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Commission_is_earned_by_basis_and_accrued_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2489", "Commission Payable", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(companyId, "6950", "Bad Debts", AccountType.Expense));
        SalesRep onSales = new(companyId, "REP-VAL", "Sells a lot", CommissionBasis.Invoiced, 5m);
        SalesRep onCash = new(companyId, "REP-CASH", "Collects well", CommissionBasis.Collected, 2m);
        db.AddRange(onSales, onCash);
        await db.SaveChangesAsync();
        Customer a = new(companyId, "COM-A", "Rep Sales Customer", Scenario.Usd) { SalesRepId = onSales.Id };
        Customer b = new(companyId, "COM-B", "Rep Cash Customer", Scenario.Usd) { SalesRepId = onCash.Id };
        Customer c = new(companyId, "COM-C", "House Account", Scenario.Usd);
        db.Customers.AddRange(a, b, c);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        async Task<long> Invoice(Customer customer, string number, DateOnly date, decimal net, decimal tax = 0m) => (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Usd, NetAmount = net, SalesAccountCode = "4000",
            TaxAmount = tax, TaxAccountCode = tax == 0m ? null : "2200",
        })).Value.OpenItemId;

        await Invoice(a, "COM-I1", new DateOnly(2026, 3, 5), 1_000m, 150m);
        long bInvoice = await Invoice(b, "COM-I2", new DateOnly(2026, 3, 6), 500m);
        await Invoice(c, "COM-I3", new DateOnly(2026, 3, 7), 400m);
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = b.Id, DocumentNumber = "COM-R1", ReceiptDate = new DateOnly(2026, 3, 15), Currency = Scenario.Usd, Amount = 300m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        (await ar.WriteOffAsync(companyId, bInvoice, new DateOnly(2026, 3, 25), "6950", "Disputed balance")).IsSuccess.Should().BeTrue();

        ICommissionService commission = scope.ServiceProvider.GetRequiredService<ICommissionService>();
        DateOnly march = new(2026, 3, 31);
        (await commission.AccrueAsync(companyId, march, "6200", "2489")).Value.Select(l => (l.RepCode, l.Earned, l.Booked)).Should().Equal(
            ("REP-CASH", 6m, 6m),
            ("REP-VAL", 50m, 50m));
        (await commission.AccrueAsync(companyId, march, "6200", "2489")).Value.Should().OnlyContain(l => l.Booked == 0m);

        await Invoice(a, "COM-I4", new DateOnly(2026, 3, 20), 200m);
        (await commission.AccrueAsync(companyId, march, "6200", "2489")).Value.Single(l => l.RepCode == "REP-VAL").Booked.Should().Be(10m);

        CommissionStatement statement = (await commission.StatementAsync(companyId, onSales.Id, new DateOnly(2026, 3, 1), march)).Value;
        statement.Lines.Should().Equal(new CommissionLine(a.Id, "COM-A", 1_200m, 60m));

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], march)).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "6200").Debit.Should().Be(66m);
        tb.Rows.Single(r => r.AccountCode == "2489").Credit.Should().Be(66m);
    }
}

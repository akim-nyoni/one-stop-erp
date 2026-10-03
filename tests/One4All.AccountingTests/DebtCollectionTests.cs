using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers debt collection: a debtor with nothing overdue cannot be handed over; one owing 500 overdue is, and cannot be
/// handed over twice; a summons is recorded and listed on the register; the write-off cannot be approved before it is
/// asked for or by the person who asked; once approved both invoices are written off and the case is closed.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DebtCollectionTests
{
    private readonly AccountingFixture _fixture;

    public DebtCollectionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Handed_over_debts_are_summonsed_and_written_off_with_approval()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "6950", "Bad Debts", AccountType.Expense));
        Customer debtor = new(companyId, "DC-BAD", "Absconded Ltd", Scenario.Usd) { PaymentTermsDays = 0 };
        Customer current = new(companyId, "DC-OK", "Pays On Time", Scenario.Usd) { PaymentTermsDays = 60 };
        db.Customers.AddRange(debtor, current);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach ((Customer customer, string number, decimal amount) in new[] { (debtor, "DC-I1", 300m), (debtor, "DC-I2", 200m), (current, "DC-I3", 400m) })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = amount, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        IDebtCollectionService debts = scope.ServiceProvider.GetRequiredService<IDebtCollectionService>();
        DateOnly handover = new(2026, 3, 10);
        (await debts.HandOverAsync(companyId, current.Id, "Smith & Partners", handover)).Error.Code.Should().Be("Debt.NothingOverdue");
        string caseNumber = (await debts.HandOverAsync(companyId, debtor.Id, "Smith & Partners", handover)).Value;
        (await debts.HandOverAsync(companyId, debtor.Id, "Other Attorneys", handover)).Error.Code.Should().Be("Debt.CaseOpen");

        (await debts.RecordSummonsAsync(companyId, caseNumber, "MC-2026-118", "Magistrates Court, Harare", new DateOnly(2026, 3, 20), new DateOnly(2026, 4, 15))).IsSuccess.Should().BeTrue();
        (await debts.SummonsRegisterAsync(companyId)).Should().Equal(
            new SummonsEntry(caseNumber, "DC-BAD", "MC-2026-118", "Magistrates Court, Harare", new DateOnly(2026, 3, 20), new DateOnly(2026, 4, 15), 500m));

        DateOnly writeOff = new(2026, 5, 31);
        (await debts.ApproveWriteOffAsync(companyId, caseNumber, 72, writeOff, "6950")).Error.Code.Should().Be("Debt.NoRequest");
        (await debts.RequestWriteOffAsync(companyId, caseNumber, 71, "Company deregistered; no assets")).IsSuccess.Should().BeTrue();
        (await debts.ApproveWriteOffAsync(companyId, caseNumber, 71, writeOff, "6950")).Error.Code.Should().Be("Debt.SelfApproval");
        (await debts.ApproveWriteOffAsync(companyId, caseNumber, 72, writeOff, "6950")).Value.Should().Be(new CaseWriteOff(caseNumber, 2, 500m));
        (await db.Set<DebtCase>().AsNoTracking().SingleAsync(c => c.CaseNumber == caseNumber && c.CompanyId == companyId)).Stage.Should().Be(DebtCaseStage.WrittenOff);
        (await debts.CloseAsync(companyId, caseNumber, DebtCaseStage.Settled, writeOff)).Error.Code.Should().Be("Debt.CaseClosed");

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], writeOff)).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "6950").Debit.Should().Be(500m);
    }
}

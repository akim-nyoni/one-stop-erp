using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers supplier payment run approval: with approval required a direct run is refused; a saved proposal lists everyone due (a held
/// supplier left out with the reason); a supplier is part-paid (oldest invoice first) and another left out; the proposer cannot
/// approve; a second person's approval pays exactly the approved amounts and the run is closed.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PaymentRunApprovalTests
{
    private readonly AccountingFixture _fixture;

    public PaymentRunApprovalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_saved_run_is_edited_and_approved_by_a_second_person()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).PaymentRunApprovalRequired = true;
        Supplier paper = new(companyId, "PA-1", "Paper Mill", Scenario.Usd);
        Supplier ink = new(companyId, "PA-2", "Ink Works", Scenario.Usd);
        Supplier glue = new(companyId, "PA-3", "Glue Co", Scenario.Usd);
        Supplier held = new(companyId, "PA-4", "Held Ltd", Scenario.Usd) { OnHold = true };
        db.Suppliers.AddRange(paper, ink, glue, held);
        await db.SaveChangesAsync();
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        async Task Invoice(Supplier s, string number, int day, decimal amount)
            => (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = s.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 9, day), Currency = Scenario.Usd, NetAmount = amount, ExpenseAccountCode = "5000",
            })).IsSuccess.Should().BeTrue();
        await Invoice(paper, "PA-I1", 1, 100m);
        await Invoice(paper, "PA-I2", 5, 60m);
        await Invoice(ink, "PA-I3", 2, 40m);
        await Invoice(glue, "PA-I4", 3, 25m);
        await Invoice(held, "PA-I5", 4, 30m);

        IPaymentRunService runs = scope.ServiceProvider.GetRequiredService<IPaymentRunService>();
        PaymentRunRequest request = new() { CompanyId = companyId, PaymentDate = new DateOnly(2026, 10, 1), DueOnOrBefore = new DateOnly(2026, 10, 31), BankAccountCode = "1100", Currency = "usd" };
        (await runs.RunAsync(request)).Error.Code.Should().Be("Ap.RunApprovalRequired");

        SavedPaymentRun saved = (await runs.SaveProposalAsync(request, 71)).Value;
        saved.Number.Should().Be("PRUN-20261001-001");
        saved.Lines.Select(l => (l.SupplierCode, l.Due, l.Included, l.Note)).Should().Equal(
            ("PA-1", 160m, true, null), ("PA-2", 40m, true, null), ("PA-3", 25m, true, null), ("PA-4", 0m, false, "Supplier on hold"));

        (await runs.SetLineAsync(companyId, saved.Number, "pa-1", "USD", true, 120m)).IsSuccess.Should().BeTrue();
        (await runs.SetLineAsync(companyId, saved.Number, "PA-2", "USD", true, 50m)).Error.Code.Should().Be("Ap.RunAmount");
        (await runs.SetLineAsync(companyId, saved.Number, "PA-4", "USD", true, null)).Error.Code.Should().Be("Ap.RunLineHeld");
        SavedPaymentRun edited = (await runs.SetLineAsync(companyId, saved.Number, "PA-3", "USD", false, null)).Value;
        edited.TotalByCurrency.Should().Equal(new Dictionary<string, decimal> { ["USD"] = 160m });

        (await runs.ApproveAsync(companyId, saved.Number, 71)).Error.Code.Should().Be("Ap.RunSelfApproval");
        PaymentRunResult paid = (await runs.ApproveAsync(companyId, saved.Number, 72)).Value;
        paid.Lines.Select(l => (l.SupplierCode, l.Amount)).Should().BeEquivalentTo(new[] { ("PA-1", 120m), ("PA-2", 40m) });

        Dictionary<string, decimal> outstanding = await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentNumber.StartsWith("PA-I"))
            .ToDictionaryAsync(i => i.DocumentNumber, i => i.OutstandingTransaction);
        outstanding.Should().Equal(new Dictionary<string, decimal> { ["PA-I1"] = 0m, ["PA-I2"] = 40m, ["PA-I3"] = 0m, ["PA-I4"] = 25m, ["PA-I5"] = 30m });

        SavedPaymentRun closed = (await runs.GetAsync(companyId, saved.Number)).Value;
        closed.Status.Should().Be(PaymentRunStatus.Paid);
        closed.Lines.Where(l => l.Included).Should().OnlyContain(l => !string.IsNullOrEmpty(l.PaymentNumber));
        (await runs.ApproveAsync(companyId, saved.Number, 72)).Error.Code.Should().Be("Ap.RunNotProposed");

        // A second proposal the same day is numbered on; a cancelled one cannot be approved.
        SavedPaymentRun second = (await runs.SaveProposalAsync(request, 71)).Value;
        second.Number.Should().Be("PRUN-20261001-002");
        (await runs.CancelAsync(companyId, second.Number)).IsSuccess.Should().BeTrue();
        (await runs.ApproveAsync(companyId, second.Number, 72)).Error.Code.Should().Be("Ap.RunNotProposed");
    }
}

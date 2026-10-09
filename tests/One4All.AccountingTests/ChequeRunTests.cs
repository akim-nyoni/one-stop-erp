using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Documents;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Banking;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers paying a supplier run by cheque: each supplier gets a cheque numbered on from the first, written in the cheque register and
/// printable with the amount in words; a cheque run makes no bank file.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ChequeRunTests
{
    private readonly AccountingFixture _fixture;

    public ChequeRunTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_cheque_run_writes_numbered_cheques_that_print_in_words()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier paper = new(companyId, "CR-P", "Paper Mill", Scenario.Usd), ink = new(companyId, "CR-I", "Ink Works", Scenario.Usd);
        db.AddRange(paper, ink, new BankAccount(companyId, "CR-BANK", "Main Bank", Scenario.Usd, "1100"));
        await db.SaveChangesAsync();
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        foreach ((Supplier supplier, string number, decimal amount) in new[] { (paper, "CR-1", 1_234.5m), (ink, "CR-2", 80m) })
        {
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 9, 1), Currency = Scenario.Usd, NetAmount = amount, ExpenseAccountCode = "5000",
            })).IsSuccess.Should().BeTrue();
        }

        IPaymentRunService runs = scope.ServiceProvider.GetRequiredService<IPaymentRunService>();
        PaymentRunRequest Run(string? first) => new()
        {
            CompanyId = companyId, PaymentDate = new DateOnly(2026, 10, 1), DueOnOrBefore = new DateOnly(2026, 10, 1), BankAccountCode = "1100", Method = PaymentRunMethod.Cheque, FirstChequeNumber = first,
        };
        (await runs.RunAsync(Run("A1"))).Error.Code.Should().Be("Ap.ChequeNumber");
        PaymentRunResult paid = (await runs.RunAsync(Run("000100"))).Value;
        paid.EftFile.Should().BeEmpty();
        paid.ChequeNumbers.Should().Equal("000100", "000101");
        List<Cheque> cheques = await db.Cheques.AsNoTracking().Where(c => c.CompanyId == companyId).OrderBy(c => c.ChequeNumber).ToListAsync();
        cheques.Select(c => c.ChequeNumber).Should().Equal("000100", "000101");
        cheques.Select(c => (c.Payee, c.Amount)).Should().BeEquivalentTo([("Ink Works", 80m), ("Paper Mill", 1_234.5m)]);

        string printed = (await scope.ServiceProvider.GetRequiredService<IDocumentLayoutService>().ChequeAsync(companyId, "cr-bank", cheques.Single(c => c.Payee == "Paper Mill").ChequeNumber)).Value;
        printed.Should().Contain("Pay   Paper Mill").And.Contain("1,234.50").And.Contain("One Thousand Two Hundred And Thirty-Four Dollars and Fifty Cents");
        (await scope.ServiceProvider.GetRequiredService<IDocumentLayoutService>().ChequeAsync(companyId, "CR-BANK", "999")).Error.Code.Should().Be("Doc.ChequeNotFound");
    }
}

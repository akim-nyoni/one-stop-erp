using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;
using IAllocationService = One4All.Application.Abstractions.Trading.IAllocationService;

namespace One4All.AccountingTests;

/// <summary>On cash accounting the VAT settlement pays what the return says, holding unpaid invoices' VAT back; a filed period is closed to VAT.</summary>
[Collection(AccountingCollection.Name)]
public sealed class VatBasisSettlementTests
{
    private readonly AccountingFixture _fixture;

    public VatBasisSettlementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cash_accounting_settles_vat_as_it_is_paid_and_filed_periods_are_closed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).TaxPointBasis = TaxPointBasis.Cash;
        db.Accounts.AddRange(new LedgerAccount(companyId, "2250", "VAT payable", AccountType.Liability), new LedgerAccount(companyId, "2260", "VAT not yet due", AccountType.Liability));
        Customer buyer = new(companyId, "VBS-C", "Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        long invoice = (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "VBS-INV", DocumentDate = new DateOnly(2026, 3, 5), Currency = Scenario.Usd, NetAmount = 100m, TaxAmount = 15m,
            SalesAccountCode = "4000", TaxAccountCode = "2200",
        })).Value.OpenItemId;

        // March: invoiced, not paid - nothing due; the 15 is held back as not yet due.
        IVatReturnService vat = scope.ServiceProvider.GetRequiredService<IVatReturnService>();
        SettleVatRequest March(string? notYetDue) => new()
        {
            CompanyId = companyId, FromDate = new DateOnly(2026, 3, 1), ToDate = new DateOnly(2026, 3, 31), VatPayableAccountCode = "2250", DocumentNumber = "VAT-0326", VatNotYetDueAccountCode = notYetDue,
        };
        (await vat.SettleAsync(March(null))).Error.Code.Should().Be("Vat.NotYetDueAccount");
        VatSettlementResult march = (await vat.SettleAsync(March("2260"))).Value;
        (march.OutputTax, march.NetPayable, march.Deferred).Should().Be((0m, 0m, 15m));
        (await vat.SettleAsync(March("2260"))).Error.Code.Should().Be("Vat.PeriodFiled");

        // March is filed: VAT cannot post on its days any more.
        (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 20), TransactionCurrency = Scenario.Usd,
            Lines = [new JournalLineRequest { AccountCode = "2200", Side = JournalSide.Debit, TransactionAmount = 1m }, new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 1m }],
        })).Error.Code.Should().Be("Tax.PeriodFiled");

        // Paid in April: the 15 falls due and comes out of the not-yet-due account into the payable.
        long receipt = (await ar.PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "VBS-RCT", ReceiptDate = new DateOnly(2026, 4, 10), Currency = Scenario.Usd, Amount = 115m, BankAccountCode = "1100",
        })).Value.OpenItemId;
        (await scope.ServiceProvider.GetRequiredService<IAllocationService>().AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoice, receipt, 115m, new DateOnly(2026, 4, 10)))
            .IsSuccess.Should().BeTrue();
        VatSettlementResult april = (await vat.SettleAsync(new SettleVatRequest
        {
            CompanyId = companyId, FromDate = new DateOnly(2026, 4, 1), ToDate = new DateOnly(2026, 4, 30), VatPayableAccountCode = "2250", DocumentNumber = "VAT-0426", VatNotYetDueAccountCode = "2260",
        })).Value;
        (april.OutputTax, april.NetPayable, april.Deferred).Should().Be((15m, 15m, -15m));
        decimal Balance(string account) => db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == account).Sum(l => l.BaseCredit - l.BaseDebit);
        (Balance("2250"), Balance("2260"), Balance("2200")).Should().Be((15m, 0m, 0m));
        (await db.VatReturnFilings.AsNoTracking().CountAsync(f => f.CompanyId == companyId)).Should().Be(2);
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Municipal;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers rates clearance: a stand billed 90 a month for three months owes 270 plus two months (180) in advance; the
/// certificate is refused while 450 and then 50 is due, and issued once the advance is paid, valid for 60 days; an
/// unknown property has no quote.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ClearanceCertificateTests
{
    private readonly AccountingFixture _fixture;

    public ClearanceCertificateTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Clearance_needs_arrears_and_advance_paid()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer seller = new(companyId, "CC-SELL", "Selling Owner", Scenario.Usd) { PaymentTermsDays = 30 };
        db.Customers.Add(seller);
        await db.SaveChangesAsync();
        Property stand = new(companyId, "301", "Ward 4", seller.Id);
        db.Properties.Add(stand);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach (int month in new[] { 1, 2, 3 })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = seller.Id, DocumentNumber = $"CC-I{month}", DocumentDate = new DateOnly(2026, month, 15), Currency = Scenario.Usd, NetAmount = 90m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        IClearanceService clearance = scope.ServiceProvider.GetRequiredService<IClearanceService>();
        DateOnly applied = new(2026, 4, 10);
        (await clearance.QuoteAsync(companyId, stand.Id, applied, 2)).Value.Should().Be(new ClearanceQuote(270m, 90m, 2, 180m));
        (await clearance.QuoteAsync(companyId, -1, applied, 2)).Error.Code.Should().Be("Mun.PropertyNotFound");
        (await clearance.IssueAsync(companyId, stand.Id, applied, 2, 60, 71)).Error.Code.Should().Be("Mun.ClearanceDue");

        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = seller.Id, DocumentNumber = "CC-R1", ReceiptDate = applied, Currency = Scenario.Usd, Amount = 400m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        (await clearance.QuoteAsync(companyId, stand.Id, applied, 2)).Value.AmountDue.Should().Be(50m);
        (await clearance.IssueAsync(companyId, stand.Id, applied, 2, 60, 71)).Error.Message.Should().StartWith("50.00 must be paid first");

        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = seller.Id, DocumentNumber = "CC-R2", ReceiptDate = applied, Currency = Scenario.Usd, Amount = 50m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        (await clearance.IssueAsync(companyId, stand.Id, applied, 2, 0, 71)).Error.Code.Should().Be("Mun.ClearanceValidity");
        IssuedClearance issued = (await clearance.IssueAsync(companyId, stand.Id, applied, 2, 60, 71)).Value;
        issued.CertificateNumber.Should().StartWith("CC-");
        issued.Should().Be(new IssuedClearance(issued.CertificateNumber, "301", applied, new DateOnly(2026, 6, 8), 180m));
        (await db.Set<ClearanceCertificate>().AsNoTracking().CountAsync(c => c.CompanyId == companyId && c.PropertyId == stand.Id)).Should().Be(1);
    }
}

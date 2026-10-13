using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers an EFT payment run written as an ISO 20022 pain.001 file: the layout code PAIN001 needs no stored layout and pays each supplier's account.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PaymentRunPain001Tests
{
    private readonly AccountingFixture _fixture;

    public PaymentRunPain001Tests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_run_with_the_pain001_layout_writes_a_credit_transfer_file()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier supplier = new(companyId, "P001-A", "Euro Parts", Scenario.Usd) { BankAccountName = "Euro Parts GmbH", BankAccountNumber = "DE89 3704 0044 0532 0130 00" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>().PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "P001-I1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 420m, ExpenseAccountCode = "6800",
        })).IsSuccess.Should().BeTrue();

        PaymentRunResult paid = (await scope.ServiceProvider.GetRequiredService<IPaymentRunService>().RunAsync(new PaymentRunRequest
        {
            CompanyId = companyId, PaymentDate = new DateOnly(2026, 3, 31), DueOnOrBefore = new DateOnly(2026, 6, 30), BankAccountCode = "1100", Currency = "USD",
            Method = PaymentRunMethod.Eft, EftLayoutCode = "pain001", SupplierIds = [supplier.Id],
        })).Value;

        string xml = System.Text.Encoding.UTF8.GetString(paid.EftFile);
        xml.Should().Contain("urn:iso:std:iso:20022:tech:xsd:pain.001.001.03").And.Contain("<IBAN>DE89370400440532013000</IBAN>").And.Contain("<Nm>Euro Parts GmbH</Nm>")
            .And.Contain("<InstdAmt Ccy=\"USD\">420.00</InstdAmt>").And.Contain("<ReqdExctnDt>2026-03-31</ReqdExctnDt>");
    }
}

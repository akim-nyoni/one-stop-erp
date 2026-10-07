using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Schools;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Municipal;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;
using IAllocationService = One4All.Application.Abstractions.Trading.IAllocationService;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the statement reports: AR42 dunning letters, AR46 commission statement, AP51 supplier statement (opening, running and closing
/// balance, ageing), AP56 cash requirement by week, M144 ratepayer statement and SC173 student fee statement (fees by term, allocated payment).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StatementReportsTests
{
    private readonly AccountingFixture _fixture;

    public StatementReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Statements_letters_and_forecasts_render()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();

        SalesRep rep = new(companyId, "ST-REP", "Statement Rep", CommissionBasis.Invoiced, 5m);
        db.Add(rep);
        await db.SaveChangesAsync();
        Customer buyer = new(companyId, "ST-CUST", "Late Payer Ltd", Scenario.Usd) { SalesRepId = rep.Id };
        Supplier supplier = new(companyId, "ST-SUPP", "Statement Supplier", Scenario.Usd) { PaymentTermsDays = 30 };
        Customer ratepayer = new(companyId, "ST-RATE", "Ratepayer One", Scenario.Usd);
        Customer guardian = new(companyId, "ST-GUAR", "Guardian One", Scenario.Usd);
        db.AddRange(buyer, supplier, ratepayer, guardian);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "ST-I1", DocumentDate = new DateOnly(2026, 3, 5), Currency = Scenario.Usd, NetAmount = 1_000m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        foreach ((string number, DateOnly date, decimal amount) in new[] { ("ST-P1", new DateOnly(2026, 2, 1), 300m), ("ST-P2", new DateOnly(2026, 6, 1), 500m) })
        {
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Usd, NetAmount = amount, ExpenseAccountCode = "5000" }))
                .IsSuccess.Should().BeTrue();
        }

        (await ap.PostPaymentAsync(new PaymentRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "ST-PAY", PaymentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Usd, Amount = 100m, BankAccountCode = "1100" }))
            .IsSuccess.Should().BeTrue();

        // Ratepayer: a property with refuse, a charge and a payment.
        Tariff refuse = new(companyId, "ST-REF", "Refuse", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 15m };
        db.Tariffs.Add(refuse);
        await db.SaveChangesAsync();
        Property property = new(companyId, "ST-77", "Ward 3", ratepayer.Id) { Address = "77 Main Road" };
        property.AddService(ServiceType.Refuse, refuse.Id, new DateOnly(2026, 1, 1));
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = ratepayer.Id, DocumentNumber = "ST-RATES-1", DocumentDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd, NetAmount = 80m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = ratepayer.Id, DocumentNumber = "ST-RR1", ReceiptDate = new DateOnly(2026, 4, 20), Currency = Scenario.Usd, Amount = 30m, BankAccountCode = "1100" }))
            .IsSuccess.Should().BeTrue();

        // Student: billed for a term, part paid and allocated.
        FeeStructure structure = new(companyId, "ST-T1", "Term 1", "G4", "Term 1", "4200");
        structure.AddItem("Tuition", 600m, true);
        db.FeeStructures.Add(structure);
        Student student = new(companyId, "ST-ADM-9", "Pupil Nine", "G4", guardian.Id);
        db.Students.Add(student);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ISchoolBillingService>().BillStudentAsync(companyId, student.Id, structure.Id, "ST-FEE-9", new DateOnly(2026, 1, 15))).IsSuccess.Should().BeTrue();
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = guardian.Id, DocumentNumber = "ST-FR1", ReceiptDate = new DateOnly(2026, 2, 10), Currency = Scenario.Usd, Amount = 250m, BankAccountCode = "1100" }))
            .IsSuccess.Should().BeTrue();
        long fee = await db.OpenItems.Where(i => i.CompanyId == companyId && i.DocumentNumber == "ST-FEE-9").Select(i => i.Id).SingleAsync();
        long paid = await db.OpenItems.Where(i => i.CompanyId == companyId && i.DocumentNumber == "ST-FR1").Select(i => i.Id).SingleAsync();
        (await scope.ServiceProvider.GetRequiredService<IAllocationService>().AllocateAsync(companyId, TradingLedger.AccountsReceivable, fee, paid, 250m, new DateOnly(2026, 2, 10)))
            .IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string report, Dictionary<string, object?> parameters)
        {
            var result = await runner.RenderAsync(companyId, report, parameters, ReportFormat.Csv);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            return Encoding.UTF8.GetString(result.Value.Content);
        }

        DateOnly june = new(2026, 6, 30);
        string dunning = await Render("AR42", new() { ["AsAtDate"] = june });
        dunning.Should().Contain("Late Payer Ltd").And.Contain("1000");

        string commission = await Render("AR46", new() { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31), ["RepCode"] = "st-rep" });
        commission.Should().Contain("ST-CUST").And.Contain("50").And.Contain("Total to accrue");

        string supplierStatement = await Render("AP51", new() { ["SupplierCode"] = "st-supp", ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = june });
        string[] lines = supplierStatement.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().Contain(l => l.Contains("Opening balance") && l.Contains("300"));
        lines.Should().Contain(l => l.Contains("ST-PAY") && l.Contains("200"));
        lines.Should().Contain(l => l.Contains("Closing balance") && l.Contains("700"));
        lines.Should().Contain(l => l.Contains("Over 90 days") && l.Contains("200"));

        string cash = await Render("AP56", new() { ["AsAtDate"] = new DateOnly(2026, 6, 15), ["Weeks"] = 4 });
        cash.Should().Contain("Overdue").And.Contain("Week 3").And.Contain("500");

        string rates = await Render("M144", new() { ["StandNumber"] = "st-77", ["FromDate"] = new DateOnly(2026, 4, 1), ["ToDate"] = june });
        rates.Should().Contain("Stand ST-77").And.Contain("Refuse").And.Contain("Charges").And.Contain("Payment").And.Contain("Closing balance");
        rates.Split('\n').Should().Contain(l => l.Contains("Closing balance") && l.Contains("50"));

        string fees = await Render("SC173", new() { ["AdmissionNumber"] = "st-adm-9", ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = june });
        fees.Should().Contain("Pupil Nine").And.Contain("Guardian One").And.Contain("Term 1 fees").And.Contain("ST-FR1");
        fees.Split('\n').Should().Contain(l => l.Contains("Closing balance") && l.Contains("350"));
    }
}

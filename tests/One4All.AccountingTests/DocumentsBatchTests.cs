using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Documents;
using One4All.Core.FixedAssets;
using One4All.Core.Inventory;
using One4All.Core.JobCosting;
using One4All.Core.Municipal;
using One4All.Core.Payroll;
using One4All.Core.Trading;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #19 (brief §S): stock transfer note, asset tag, leave forms, loan statement, gift voucher, EFT advice, subcontractor certificate and licence print as documents.</summary>
[Collection(AccountingCollection.Name)]
public sealed class DocumentsBatchTests
{
    private readonly AccountingFixture _fixture;

    public DocumentsBatchTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Transfer_note_asset_tag_leave_forms_and_loan_statement_render()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item cement = new(companyId, "DB-CEM", "Cement 50kg") { CostOfSalesAccountCode = "5000" };
        Warehouse main = new(companyId, "DB-MAIN", "Main store"), transit = new(companyId, "DB-TRN", "In transit"), site = new(companyId, "DB-SITE", "Site store");
        Employee employee = new(companyId, "DB-E1", "Tendai Moyo", 1000m, new DateOnly(2024, 1, 1));
        db.AddRange(cement, main, transit, site, employee);
        await db.SaveChangesAsync();
        db.StockTransfers.Add(new StockTransfer(companyId, "DB-ST1", cement.Id, main.Id, transit.Id, site.Id, new DateOnly(2026, 5, 2), 40m, 320m));
        db.FixedAssets.Add(new FixedAsset(companyId, "DB-FA1", "Toyota Hilux", "Vehicles", 30_000m, 5_000m, new DateOnly(2026, 1, 15), DepreciationMethod.StraightLine, 60, 0m, "1600", "1610", "6000"));
        db.LeaveApplications.Add(new LeaveApplication(companyId, employee.Id, "Annual", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 10), 8m));
        EmployeeLeaveBalance annual = new(companyId, employee.Id, "Annual");
        annual.Accrue(15m);
        db.EmployeeLeaveBalances.Add(annual);
        db.EmployeeLoans.Add(new EmployeeLoan(companyId, employee.Id, "DB-L1", 600m, 200m, new DateOnly(2026, 4, 1), "1350"));
        await db.SaveChangesAsync();

        ITextDocumentService documents = scope.ServiceProvider.GetRequiredService<ITextDocumentService>();
        async Task<string> Print(string kind, string number, DateOnly? date = null)
        {
            var printed = await documents.RenderAsync(companyId, kind, number, date);
            printed.IsSuccess.Should().BeTrue(printed.IsFailure ? $"{kind}: {printed.Error.Message}" : null);
            return printed.Value;
        }

        (await Print("StockTransferNote", "DB-ST1")).Should().Contain("STOCK TRANSFER NOTE").And.Contain("DB-MAIN Main store").And.Contain("DB-SITE Site store").And.Contain("DB-CEM Cement 50kg").And.Contain("320.00");
        (await Print("AssetTag", "DB-FA1")).Should().Contain("Asset: DB-FA1").And.Contain("Toyota Hilux").And.Contain("*DB-FA1*");
        (await Print("LeaveApplication", "DB-E1/2026-07-01")).Should().Contain("LEAVE APPLICATION").And.Contain("Tendai Moyo").And.Contain("Days: 8").And.Contain("15 day(s) of Annual leave available");
        (await Print("LeaveBalanceCertificate", "DB-E1", new DateOnly(2026, 6, 30))).Should().Contain("LEAVE BALANCE CERTIFICATE").And.Contain("ANNUAL");
        string loan = await Print("LoanStatement", "DB-L1");
        loan.Should().Contain("EMPLOYEE LOAN AGREEMENT AND STATEMENT").And.Contain("Outstanding: 600.00").And.Contain("   3");
        (await documents.RenderAsync(companyId, "LeaveApplication", "DB-E1")).Error.Code.Should().Be("Doc.LeaveKey");
    }

    [SkippableFact]
    public async Task Gift_voucher_eft_advice_subcontract_certificate_and_licence_render()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier builder = new(companyId, "DB-SUB", "Moyo Builders", Scenario.Usd) { BankName = "CBZ", BankAccountNumber = "0123456789" };
        Customer client = new(companyId, "DB-CLI", "Harare Traders", Scenario.Usd);
        db.AddRange(builder, client);
        await db.SaveChangesAsync();

        db.GiftCards.Add(new GiftCard(companyId, "gv-100", Scenario.Usd, 50m, new DateOnly(2026, 5, 1), new DateOnly(2027, 4, 30)));
        PaymentRunBatch run = new(companyId, "DB-PR1", new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 31), "1000", 1);
        run.AddLine(new PaymentRunBatchLine(builder.Id, builder.Code, "USD", 1_250m, true, null));
        run.AddLine(new PaymentRunBatchLine(builder.Id, builder.Code, "USD", 999m, false, "on hold"));
        db.Add(run);
        JobContract job = new(companyId, "DB-JOB", "Clinic block", client.Id, 100_000m, 80_000m, 5m, "4000");
        db.Add(job);
        await db.SaveChangesAsync();
        Subcontract subcontract = new(companyId, job.Id, builder.Id, "DB-SC1", "Roofing", 20_000m, 10m);
        subcontract.AddCertificate(new SubcontractCertificate("DB-C1", new DateOnly(2026, 4, 30), 6_000m, 6_000m, 600m, "INV-1"));
        subcontract.AddCertificate(new SubcontractCertificate("DB-C2", new DateOnly(2026, 5, 31), 10_000m, 4_000m, 400m, "INV-2"));
        db.Add(subcontract);
        db.Add(new CouncilLicence(companyId, "DB-LIC1", CouncilLicenceKind.Liquor, client.Id, null, "Bottle store", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 12, 300m, "4000"));
        await db.SaveChangesAsync();

        ITextDocumentService documents = scope.ServiceProvider.GetRequiredService<ITextDocumentService>();
        async Task<string> Print(string kind, string number)
        {
            var printed = await documents.RenderAsync(companyId, kind, number);
            printed.IsSuccess.Should().BeTrue(printed.IsFailure ? $"{kind}: {printed.Error.Message}" : null);
            return printed.Value;
        }

        (await Print("GiftVoucher", "GV-100")).Should().Contain("GIFT VOUCHER").And.Contain("Value:   USD 50.00").And.Contain("Valid until 2027-04-30").And.Contain("*GV-100*");
        string eft = await Print("EftAdvice", "DB-PR1");
        eft.Should().Contain("EFT PAYMENT ADVICE").And.Contain("CBZ").And.Contain("0123456789").And.Contain("1,250.00").And.Contain("Total USD (1 payment(s))").And.NotContain("999.00");
        string certificate = await Print("SubcontractCertificate", "DB-C2");
        certificate.Should().Contain("SUBCONTRACTOR PAYMENT CERTIFICATE").And.Contain("DB-JOB Clinic block").And.Contain("DB-SUB Moyo Builders");
        certificate.Should().MatchRegex(@"Less previously certified\s+6,000\.00").And.MatchRegex(@"Amount payable\s+3,600\.00").And.MatchRegex(@"Retention held to date: 1,000\.00");
        (await Print("LicenceCertificate", "DB-LIC1")).Should().Contain("LIQUOR LICENCE").And.Contain("Harare Traders (DB-CLI)").And.Contain("Bottle store").And.Contain("Expires: 2026-12-31");
        (await documents.RenderAsync(companyId, "EftAdvice", "NOPE")).Error.Code.Should().Be("Doc.NotFound");
    }
}

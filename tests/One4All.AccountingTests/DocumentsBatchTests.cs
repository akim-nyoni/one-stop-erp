using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Documents;
using One4All.Core.FixedAssets;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Budgeting;
using One4All.Core.Housing;
using One4All.Core.Inventory;
using One4All.Core.JobCosting;
using One4All.Core.Municipal;
using One4All.Core.Payroll;
using One4All.Core.SalesOrders;
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

    [SkippableFact]
    public async Task Proforma_housing_letters_and_employment_contract_render()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer client = new(companyId, "DB-PF", "Bulawayo Hardware", Scenario.Usd);
        Item bricks = new(companyId, "DB-BRK", "Bricks (1000)") { CostOfSalesAccountCode = "5000" };
        Employee manager = new(companyId, "DB-M1", "Rudo Ncube", 3000m, new DateOnly(2020, 1, 1));
        db.AddRange(client, bricks, manager);
        LandParcel parcel = new(companyId, $"LP-DB-{companyId}", "Documents parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-DB-{companyId}", "Documents Scheme", SchemeType.CouncilDelivered);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        HousingApplicant applicant = new(companyId, $"DB-ID-{companyId}", "Farai Dube", new DateOnly(2015, 1, 1));
        db.HousingApplicants.Add(applicant);
        await db.SaveChangesAsync();
        SalesOrder order = new(companyId, client.Id, "DB-SO1", new DateOnly(2026, 6, 1), Scenario.Usd);
        order.AddLine(bricks.Id, "Bricks (1000)", 3m, 250m, null, "4000");
        db.Add(order);
        db.Add(new Employee(companyId, "DB-E2", "Chipo Sibanda", 1500m, new DateOnly(2026, 2, 1))
        {
            JobTitle = "Storekeeper", Department = "Stores", ReportsToEmployeeId = manager.Id, ProbationEndDate = new DateOnly(2026, 4, 30), NoticePeriodDays = 30, NationalIdNumber = "63-123456A70",
        });
        db.HousingStands.Add(new HousingStand(companyId, parcel.Id, scheme.Id, "DB-S1", 300m, 1000m));
        applicant.SetDetails(HousingCategory.HighDensity, new DateOnly(1985, 1, 1), new DateOnly(2000, 1, 1), 2, false, 400m, CurrentTenure.Lodger);
        applicant.Register($"WL-DB-{companyId}", applicant.RegistrationDate);
        await db.SaveChangesAsync();

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationRunView run = (await allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Ballot, Seed = 7, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71,
        })).Value;
        string certificate = (await allocations.ApproveAsync(companyId, run.RunNumber, 72, new DateOnly(2026, 3, 1), 30)).Value.Lines[0].CertificateNumber!;

        ITextDocumentService documents = scope.ServiceProvider.GetRequiredService<ITextDocumentService>();
        async Task<string> Print(string kind, string number, DateOnly? date = null)
        {
            var printed = await documents.RenderAsync(companyId, kind, number, date);
            printed.IsSuccess.Should().BeTrue(printed.IsFailure ? $"{kind}: {printed.Error.Message}" : null);
            return printed.Value;
        }

        (await Print("ProformaInvoice", "DB-SO1")).Should().Contain("PROFORMA INVOICE").And.Contain("Bulawayo Hardware").And.Contain("750.00").And.Contain("AMOUNT DUE").And.Contain("not a tax invoice");
        (await Print("HousingOfferLetter", certificate, new DateOnly(2026, 3, 2))).Should().Contain("OFFER OF STAND DB-S1").And.Contain("Farai Dube").And.Contain("2026-03-31");
        (await documents.RenderAsync(companyId, "HousingAllocationLetter", certificate, new DateOnly(2026, 3, 2))).Error.Code.Should().Be("Doc.NotAccepted");
        (await allocations.AcceptAsync(companyId, certificate, new DateOnly(2026, 3, 10))).IsSuccess.Should().BeTrue();
        (await Print("HousingAllocationLetter", certificate, new DateOnly(2026, 3, 11))).Should().Contain("ALLOCATION OF STAND DB-S1").And.Contain("recorded on 2026-03-10");
        (await Print("EmploymentContract", "DB-E2")).Should().Contain("CONTRACT OF EMPLOYMENT").And.Contain("Chipo Sibanda, ID 63-123456A70").And.Contain("Storekeeper, Stores, reporting to Rudo Ncube")
            .And.Contain("until 2026-04-30").And.Contain("1,500.00").And.Contain("30 days");
    }

    [SkippableFact]
    public async Task Instalment_agreement_notice_budget_form_and_student_receipt_render()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer buyer = new(companyId, "DB-BUY", "Tafadzwa Moyo", Scenario.Usd), guardian = new(companyId, "DB-GRD", "Mr Banda", Scenario.Usd);
        LandParcel parcel = new(companyId, $"LP-DI-{companyId}", "Instalment parcel", 5000m, 50000m);
        db.AddRange(buyer, guardian, parcel);
        await db.SaveChangesAsync();
        HousingStand stand = new(companyId, parcel.Id, null, "DB-IS1", 300m, 1000m);
        db.HousingStands.Add(stand);
        await db.SaveChangesAsync();
        InstalmentAgreement agreement = new(companyId, "DB-IA1", stand.Id, buyer.Id, 3_000m, 600m, 0m, InterestMethod.Zero, 2, new DateOnly(2026, 1, 1), "4000");
        agreement.AddLine(new InstalmentScheduleLine(1, new DateOnly(2026, 2, 1), 1_200m, 0m, 1_200m, 1_200m));
        agreement.AddLine(new InstalmentScheduleLine(2, new DateOnly(2026, 3, 1), 1_200m, 0m, 1_200m, 0m));
        db.Add(agreement);
        await db.SaveChangesAsync();
        db.Add(new InstalmentNotice(companyId, agreement.Id, "DB-N1", DefaultStage.Demand, new DateOnly(2026, 4, 1), 1_200m, 31, "Pay the arrears within 14 days or the agreement may be cancelled."));
        await db.SaveChangesAsync();

        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).OrderBy(y => y.StartDate).Select(y => y.Id).FirstAsync();
        (await scope.ServiceProvider.GetRequiredService<IBudgetingService>().CreateBudgetAsync(companyId, yearId, "DB-BUD", BudgetType.OriginalApproved, null,
            [new NewBudgetLine("6800", 1, 100m, null), new NewBudgetLine("6800", 4, 200m, null), new NewBudgetLine("6000", 12, 50m, null)])).IsSuccess.Should().BeTrue();

        One4All.Application.Abstractions.Trading.ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Trading.ISalesLedgerService>();
        long invoice = (await ar.PostInvoiceAsync(new One4All.Application.Abstractions.Trading.SalesInvoiceRequest { CompanyId = companyId, CustomerId = guardian.Id, DocumentNumber = "DB-FEE1", DocumentDate = new DateOnly(2026, 1, 10),
            Currency = Scenario.Usd, NetAmount = 400m, SalesAccountCode = "4000", Narrative = "Term 1 fees - Tino Banda (S001)" })).Value.OpenItemId;
        long receipt = (await ar.PostReceiptAsync(new One4All.Application.Abstractions.Trading.ReceiptRequest { CompanyId = companyId, CustomerId = guardian.Id, DocumentNumber = "DB-RC1", ReceiptDate = new DateOnly(2026, 1, 20),
            Currency = Scenario.Usd, Amount = 500m, BankAccountCode = "1100" })).Value.OpenItemId;
        (await scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Trading.IAllocationService>()
            .AllocateAsync(companyId, One4All.Core.Trading.TradingLedger.AccountsReceivable, invoice, receipt, 300m, new DateOnly(2026, 1, 20))).IsSuccess.Should().BeTrue();

        ITextDocumentService documents = scope.ServiceProvider.GetRequiredService<ITextDocumentService>();
        async Task<string> Print(string kind, string number)
        {
            var printed = await documents.RenderAsync(companyId, kind, number);
            printed.IsSuccess.Should().BeTrue(printed.IsFailure ? $"{kind}: {printed.Error.Message}" : null);
            return printed.Value;
        }

        (await Print("InstalmentAgreement", "DB-IA1")).Should().Contain("INSTALMENT SALE AGREEMENT").And.Contain("Tafadzwa Moyo (DB-BUY)").And.Contain("Stand:      DB-IS1")
            .And.Contain("Financed: 2,400.00").And.Contain("2026-03-01");
        (await Print("InstalmentNotice", "DB-N1")).Should().Contain("INSTALMENT DEMAND NOTICE").And.Contain("Agreement:  DB-IA1").And.Contain("Days overdue: 31").And.Contain("within 14 days");
        (await Print("BudgetForm", "DB-BUD")).Should().Contain("BUDGET SUBMISSION FORM").And.MatchRegex(@"6800 .*100\.00\s+200\.00\s+0\.00\s+0\.00\s+300\.00").And.MatchRegex(@"Total\s+100\.00\s+200\.00\s+0\.00\s+50\.00\s+350\.00");
        (await Print("StudentReceipt", "DB-RC1")).Should().Contain("SCHOOL FEES RECEIPT").And.Contain("Mr Banda (DB-GRD)").And.Contain("Term 1: Tino Banda (S001)")
            .And.MatchRegex(@"300\.00\s+100\.00").And.MatchRegex(@"Held on account \(not yet allocated\)\s+200\.00");
    }

    [SkippableFact]
    public async Task A_customer_debit_note_prints_as_a_debit_note()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "DB-DN", "Gweru Motors", Scenario.Usd);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        One4All.Application.Abstractions.Trading.ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Trading.ISalesLedgerService>();
        (await ar.PostInvoiceAsync(new One4All.Application.Abstractions.Trading.SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "DB-INV9",
            DocumentDate = new DateOnly(2026, 5, 1), Currency = Scenario.Usd, NetAmount = 1_000m, SalesAccountCode = "4000" })).IsSuccess.Should().BeTrue();
        var note = await ar.PostDebitNoteAsync(new One4All.Application.Abstractions.Trading.SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "DB-DN1",
            DocumentDate = new DateOnly(2026, 5, 9), Currency = Scenario.Usd, NetAmount = 75m, SalesAccountCode = "4000", Narrative = "Freight not invoiced" }, "DB-INV9");
        note.IsSuccess.Should().BeTrue(note.IsFailure ? note.Error.Message : null);

        var printed = await scope.ServiceProvider.GetRequiredService<ITextDocumentService>().RenderAsync(companyId, "TaxInvoice", "DB-DN1");
        printed.IsSuccess.Should().BeTrue(printed.IsFailure ? printed.Error.Message : null);
        printed.Value.Should().Contain("DEBIT NOTE").And.Contain("Gweru Motors").And.Contain("75.00").And.NotContain("CREDIT NOTE");
    }
}

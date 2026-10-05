using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers housing fees, duty and arrangements (brief §14 H27, H47, H63): a registration fee taken to income once; a
/// listed applicant who has not paid it; transfer duty received on a transfer's consideration, held, then remitted, once
/// only; and a payment arrangement with an instalment buyer measured against its schedule.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingFeesTests
{
    private readonly AccountingFixture _fixture;

    public HousingFeesTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Fees_duty_and_arrangements_are_posted_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Accounts.Add(new LedgerAccount(companyId, "4965", "Housing Interest Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "4991", "Waiting List Fees", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "2481", "Transfer Duty Payable", AccountType.Liability));
        Customer buyer = new(companyId, "HF-BUY", "Fee Buyer", Scenario.Usd);
        Customer next = new(companyId, "HF-NXT", "Next Owner", Scenario.Usd);
        db.Customers.AddRange(buyer, next);
        foreach (string id in new[] { $"HF-A-{companyId}", $"HF-B-{companyId}" })
        {
            HousingApplicant applicant = new(companyId, id, id, new DateOnly(2026, 2, 1));
            applicant.Register($"WL-{id}", applicant.RegistrationDate);
            db.HousingApplicants.Add(applicant);
        }

        await db.SaveChangesAsync();

        IHousingFeeService fees = scope.ServiceProvider.GetRequiredService<IHousingFeeService>();
        ApplicantFeeReceipt Fee(string receipt) => new()
        {
            CompanyId = companyId, WaitingListNumber = $"WL-HF-A-{companyId}", FeeType = ApplicantFeeType.Registration, Amount = 25m, Date = new DateOnly(2026, 2, 1),
            ReceiptReference = receipt, BankAccountCode = "1100", IncomeAccountCode = "4991",
        };
        (await fees.RecordApplicantFeeAsync(Fee("RF-1"))).IsSuccess.Should().BeTrue();
        (await fees.RecordApplicantFeeAsync(Fee("RF-2"))).Error.Code.Should().Be("Hse.FeeAlreadyPaid");
        (await fees.RecordApplicantFeeAsync(Fee("RF-3") with { FeeType = ApplicantFeeType.Renewal, Amount = 5m, Date = new DateOnly(2026, 3, 1) })).IsSuccess.Should().BeTrue();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-HF-{companyId}", "Parcel", 500m, 5000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-HF")).Value.LandParcelId;
        long standId = (await housing.SubdivideAsync(companyId, parcelId, null, [new NewStand("HF-1", 500m)], ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds[0];
        (await scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>().SignAsync(new InstalmentSaleRequest
        {
            CompanyId = companyId, StandId = standId, BuyerCustomerId = buyer.Id, CashPrice = 13000m, Deposit = 1000m, AnnualRatePercent = 12m, Method = InterestMethod.ReducingBalance,
            TermMonths = 12, SignedOn = new DateOnly(2026, 1, 10), FirstDueDate = new DateOnly(2026, 2, 1), SalesAccountCode = "4100", StandsAvailableAccountCode = "1460",
            CostOfSalesAccountCode = "5100", InterestAccountCode = "4965", BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        string transfer = (await scope.ServiceProvider.GetRequiredService<ITransferApplicationService>().LodgeAsync(new TransferLodgement
        {
            CompanyId = companyId, StandId = standId, TransferType = TransferType.Sale, ToCustomerId = next.Id, Consideration = 20000m, Date = new DateOnly(2026, 3, 1),
        })).Value;

        TransferDutyReceipt Duty(decimal rate) => new()
        {
            CompanyId = companyId, ApplicationNumber = transfer, RatePercent = rate, Date = new DateOnly(2026, 3, 5), ReceiptReference = "TD-1", BankAccountCode = "1100", LiabilityAccountCode = "2481",
        };
        (await fees.WithholdTransferDutyAsync(Duty(0m))).Error.Code.Should().Be("Hse.DutyDetails");
        (await fees.WithholdTransferDutyAsync(Duty(2.5m))).Value.Should().Be(500m);
        (await fees.WithholdTransferDutyAsync(Duty(2.5m))).Error.Code.Should().Be("Hse.DutyAlreadyWithheld");
        (await fees.RemitTransferDutyAsync(companyId, transfer, new DateOnly(2026, 3, 1), "ZIMRA-9", "1100")).Error.Code.Should().Be("Hse.RemittanceDate");
        (await fees.RemitTransferDutyAsync(companyId, transfer, new DateOnly(2026, 4, 10), "ZIMRA-9", "1100")).IsSuccess.Should().BeTrue();
        (await fees.RemitTransferDutyAsync(companyId, transfer, new DateOnly(2026, 4, 11), "ZIMRA-9", "1100")).Error.Code.Should().Be("Hse.DutyRemitted");

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 12, 31))).Value;
        tb.Rows.Single(r => r.AccountCode == "4991").Credit.Should().Be(30m);
        tb.Rows.Where(r => r.AccountCode == "2481").Sum(r => r.Credit - r.Debit).Should().Be(0m);

        db.Set<PaymentArrangement>().Add(new PaymentArrangement(companyId, buyer.Id, 1200m, 600m, new DateOnly(2026, 4, 1)));
        await db.SaveChangesAsync();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 5, 15) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string feesReport = await Render("H27");
        feesReport.Should().Contain($"2026-02-01,WL-HF-A-{companyId},HF-A-{companyId},Registration,RF-1,25").And.Contain("Renewal,RF-3,5")
            .And.Contain($"WL-HF-B-{companyId},HF-B-{companyId},Registration fee not paid");
        (await Render("H63")).Should().Contain($"{transfer},HF-1,\"20,000.00\",2.5,2026-03-05,TD-1,2026-04-10,ZIMRA-9,500");
        (await Render("H47")).Should().Contain("(no scheme),Fee Buyer,2026-04-01,\"1,200.00\",600.00,Active,\"1,200.00\",0.00,2,2026-04-01,1200"); // next due is the earliest unpaid instalment;
    }
}

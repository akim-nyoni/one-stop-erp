using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using ISalesLedgerService = One4All.Application.Abstractions.Trading.ISalesLedgerService;
using ReceiptRequest = One4All.Application.Abstractions.Trading.ReceiptRequest;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers purchase deposits and cancellations (brief §14 H51, H53): a deposit against an accepted offer is held, refunded,
/// taken again and applied to the buyer's account; an instalment agreement cancelled at the buyer's request returns the
/// stand and refunds what was paid less occupation rental and the admin charge; the registers read it back.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingDepositsTests
{
    private readonly AccountingFixture _fixture;

    public HousingDepositsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Deposits_are_held_and_cleared_and_cancellations_refund_the_buyer()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Accounts.Add(new LedgerAccount(companyId, "4965", "Housing Interest Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "2482", "Purchase Deposits Held", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(companyId, "4986", "Forfeits & Cancellations", AccountType.Income));
        Customer payer = new(companyId, "HD-PAY", "Deposit Payer", Scenario.Usd);
        Customer buyer = new(companyId, "HD-CAN", "Cancelling Buyer", Scenario.Usd);
        db.Customers.AddRange(payer, buyer);
        HousingScheme scheme = new(companyId, $"SCH-HDP-{companyId}", "Deposit Scheme", SchemeType.CouncilDelivered);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-HDP-{companyId}", "Parcel", 600m, 6000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-HDP")).Value.LandParcelId;
        IReadOnlyList<long> stands = (await housing.SubdivideAsync(companyId, parcelId, scheme.Id, [new NewStand("HDP-1", 300m), new NewStand("HDP-2", 300m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds;

        // Stand 2 is sold on instalments first, so the allocation run can only offer stand 1.
        IInstalmentSaleService sales = scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>();
        string agreement = (await sales.SignAsync(new InstalmentSaleRequest
        {
            CompanyId = companyId, StandId = stands[1], BuyerCustomerId = buyer.Id, CashPrice = 13000m, Deposit = 1000m, AnnualRatePercent = 12m, Method = InterestMethod.ReducingBalance,
            TermMonths = 12, SignedOn = new DateOnly(2026, 1, 10), FirstDueDate = new DateOnly(2026, 2, 1), SalesAccountCode = "4100", StandsAvailableAccountCode = "1460",
            CostOfSalesAccountCode = "5100", InterestAccountCode = "4965", BankAccountCode = "1100",
        })).Value.AgreementNumber;

        HousingApplicant applicant = new(companyId, $"HDP-A-{companyId}", "Deposit Applicant", new DateOnly(2015, 1, 1));
        applicant.SetDetails(HousingCategory.HighDensity, new DateOnly(1985, 1, 1), new DateOnly(2000, 1, 1), 1, false, 300m, CurrentTenure.Lodger);
        applicant.Register($"WL-HDP-A-{companyId}", applicant.RegistrationDate);
        db.HousingApplicants.Add(applicant);
        await db.SaveChangesAsync();
        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationRunView run = (await allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71,
        })).Value;
        string certificate = (await allocations.ApproveAsync(companyId, run.RunNumber, 72, new DateOnly(2026, 3, 2), 30)).Value.Lines.Single().CertificateNumber!;

        IHousingDepositService deposits = scope.ServiceProvider.GetRequiredService<IHousingDepositService>();
        StandDepositReceipt Deposit(string receipt, decimal amount, DateOnly date) => new()
        {
            CompanyId = companyId, CertificateNumber = certificate, CustomerId = payer.Id, Amount = amount, Date = date, ReceiptReference = receipt, BankAccountCode = "1100", LiabilityAccountCode = "2482",
        };
        (await scope.ServiceProvider.GetRequiredService<IHousingRegisterDetailsService>().SetSchemeDetailsAsync(companyId, scheme.Code, new SchemeDetails(null, null, null, 500m)))
            .IsSuccess.Should().BeTrue();
        (await allocations.AcceptAsync(companyId, certificate, new DateOnly(2026, 3, 10))).Error.Code.Should().Be("Hse.DepositRequired");
        (await deposits.TakeDepositAsync(Deposit("DP-1", 500m, new DateOnly(2026, 3, 15)))).IsSuccess.Should().BeTrue();
        (await allocations.AcceptAsync(companyId, certificate, new DateOnly(2026, 3, 16))).IsSuccess.Should().BeTrue();
        (await deposits.TakeDepositAsync(Deposit("DP-X", 100m, new DateOnly(2026, 3, 16)))).Error.Code.Should().Be("Hse.DepositHeld");
        (await deposits.RefundDepositAsync(companyId, certificate, new DateOnly(2026, 4, 1), "1100")).IsSuccess.Should().BeTrue();
        (await deposits.TakeDepositAsync(Deposit("DP-2", 600m, new DateOnly(2026, 4, 5)))).IsSuccess.Should().BeTrue();
        (await deposits.ApplyDepositAsync(companyId, certificate, new DateOnly(2026, 4, 4))).Error.Code.Should().Be("Hse.DepositDate");
        (await deposits.ApplyDepositAsync(companyId, certificate, new DateOnly(2026, 4, 10))).IsSuccess.Should().BeTrue();
        (await deposits.ApplyDepositAsync(companyId, certificate, new DateOnly(2026, 4, 11))).Error.Code.Should().Be("Hse.NoDepositHeld");

        // The buyer pays the two raised instalments, then cancels: paid 3,132.38 less 4 months at 50 and 1% of 13,000.
        (await sales.RaiseDueAsync(companyId, new DateOnly(2026, 3, 31))).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "RC-HDP-1", ReceiptDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd, Amount = 2132.38m, BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        AgreementCancellationRequest Cancel(long approver) => new()
        {
            CompanyId = companyId, AgreementNumber = agreement, Date = new DateOnly(2026, 4, 15), Reason = "Relocating", MonthlyOccupationRental = 50m, AdminChargePercent = 1m,
            NetRealisableValue = 3000m, RequestedByUserId = 71, ApprovedByUserId = approver, StandsAvailableAccountCode = "1460", GainLossAccountCode = "4986", BankAccountCode = "1100",
        };
        (await deposits.CancelAgreementAsync(Cancel(71))).Error.Code.Should().Be("Hse.SelfApproval");
        AgreementCancellationResult cancelled = (await deposits.CancelAgreementAsync(Cancel(72))).Value;
        cancelled.Should().BeEquivalentTo(new { Paid = 3132.38m, MonthsOccupied = 4, OccupationRental = 200m, AdminCharge = 130m, Arrears = 0m, Refund = 2802.38m });

        // The allocatee gives the allocated stand back: the accepted allocation ends, so the stand no longer counts as promised.
        StandSurrenderEntry surrendered = (await scope.ServiceProvider.GetRequiredService<IRepossessionService>().SurrenderAsync(new StandSurrenderRequest
        {
            CompanyId = companyId, StandNumber = "HDP-1", Date = new DateOnly(2026, 4, 20), Reason = "Emigrating", RequestedByUserId = 71, ApprovedByUserId = 72, GainLossAccountCode = "4986",
        })).Value;
        (surrendered.Holder, surrendered.InventoryRecognised).Should().Be(("Deposit Applicant", 0m));
        (await db.Set<StandAllocation>().AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.CertificateNumber == certificate)).Status.Should().Be(AllocationStatus.Ended);
        (await db.HousingStands.AsNoTracking().SingleAsync(s => s.Id == stands[0])).Status.Should().Be(StandStatus.Surrendered);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 12, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Where(r => r.AccountCode == "2482").Sum(r => r.Credit - r.Debit).Should().Be(0m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, DateOnly asAt)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["AsAtDate"] = asAt }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("H51", new DateOnly(2026, 3, 20))).Should().Contain($"{scheme.Code},Deposit Payer,Purchase deposit,{certificate},2026-03-15,500");
        (await Render("H51", new DateOnly(2026, 4, 30))).Should().NotContain("Purchase deposit");
        (await Render("H53", new DateOnly(2026, 4, 30))).Should().Contain($"{agreement},Cancelling Buyer,2026-04-15,\"3,132.38\",4,200.00,130.00,0.00,").And.Contain("2802.38");
    }
}

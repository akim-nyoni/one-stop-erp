using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers instalment default: a buyer who pays nothing after the deposit falls into arrears; the reminder, demand and breach notices are issued in turn, each
/// only after the last is served; the cancellation notice waits for the breach notice to stand; penalty interest is charged once a month on the arrears.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class InstalmentDefaultTests
{
    private readonly AccountingFixture _fixture;

    public InstalmentDefaultTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Default_notices_follow_the_arrears_with_proof_of_service()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4965", "Housing Interest Income", AccountType.Income));
        Customer buyer = new(companyId, "IDF-B", "Default Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        await db.SaveChangesAsync();
        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-IDF-{companyId}", "Parcel", 1000m, 100000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-IDF")).Value.LandParcelId;
        long standId = (await housing.SubdivideAsync(companyId, parcelId, null, [new NewStand("IDF-1", 500m), new NewStand("IDF-2", 500m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds[0];
        IInstalmentSaleService sales = scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>();
        string agreement = (await sales.SignAsync(new InstalmentSaleRequest
        {
            CompanyId = companyId, StandId = standId, BuyerCustomerId = buyer.Id, CashPrice = 13000m, Deposit = 1000m, AnnualRatePercent = 0m, Method = InterestMethod.ReducingBalance,
            TermMonths = 12, SignedOn = new DateOnly(2026, 1, 10), FirstDueDate = new DateOnly(2026, 2, 1), SalesAccountCode = "4100", StandsAvailableAccountCode = "1460",
            CostOfSalesAccountCode = "5100", InterestAccountCode = "4965", BankAccountCode = "1100",
        })).Value.AgreementNumber;
        (await sales.RaiseDueAsync(companyId, new DateOnly(2026, 4, 30))).IsSuccess.Should().BeTrue();

        IInstalmentDefaultService defaults = scope.ServiceProvider.GetRequiredService<IInstalmentDefaultService>();
        DateOnly may = new(2026, 5, 10);
        DefaultCase arrears = (await defaults.ReviewAsync(companyId, may)).Single(c => c.AgreementNumber == agreement);
        (arrears.Arrears, arrears.DaysOverdue, arrears.NextStage).Should().Be((3000m, 98, DefaultStage.Reminder), "February, March and April unpaid; February 98 days overdue");

        (string reminder, string text) = (await defaults.IssueNextAsync(companyId, agreement, may)).Value;
        text.Should().StartWith("REMINDER").And.Contain("3,000.00").And.Contain("Default Buyer");
        (await defaults.IssueNextAsync(companyId, agreement, may)).Error.Message.Should().Contain("not been served");
        (await defaults.RecordServiceAsync(companyId, reminder, may, "", "Officer")).Error.Code.Should().Be("Hse.Service");
        (await defaults.RecordServiceAsync(companyId, reminder, may, "Hand", "Officer Moyo")).IsSuccess.Should().BeTrue();
        (await defaults.RecordServiceAsync(companyId, reminder, may, "Hand", "Officer Moyo")).Error.Code.Should().Be("Hse.NoticeServed");

        (string demand, _) = (await defaults.IssueNextAsync(companyId, agreement, may)).Value;
        (await defaults.RecordServiceAsync(companyId, demand, may, "Registered post", "Clerk")).IsSuccess.Should().BeTrue();
        (string breach, string breachText) = (await defaults.IssueNextAsync(companyId, agreement, may)).Value;
        breachText.Should().StartWith("NOTICE OF BREACH");
        (await defaults.RecordServiceAsync(companyId, breach, may, "Hand", "Officer Moyo")).IsSuccess.Should().BeTrue();
        (await defaults.IssueNextAsync(companyId, agreement, may)).Error.Message.Should().Contain("must stand 30 days");
        (await defaults.IssueNextAsync(companyId, agreement, new DateOnly(2026, 6, 15))).Value.Text.Should().StartWith("NOTICE OF CANCELLATION");
        (await db.Set<InstalmentNotice>().AsNoTracking().CountAsync(n => n.CompanyId == companyId)).Should().Be(4);

        (await defaults.SetPenaltyRateAsync(companyId, agreement, 12m)).IsSuccess.Should().BeTrue();
        (await defaults.ChargePenaltyInterestAsync(companyId, may)).Value.Should().Be(new PenaltyRun(1, 30m), "1% a month on 3,000 owed at the start of May");
        (await defaults.ChargePenaltyInterestAsync(companyId, may)).Value.Agreements.Should().Be(0, "once a month");
    }
}

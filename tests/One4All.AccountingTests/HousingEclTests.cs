using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers housing ECL: an instalment buyer in a scheme owes 12,000 (net of a 1,000 deposit) 50 days past due and a
/// tenant owes 300 rent 30 days past due, while an ordinary trade debtor's overdue 1,000 is left to the general
/// provision. On the instalment matrix (2% current, 20% over 30 days) the scheme needs 2,400; on the rental matrix (5%,
/// 50% from 30 days) rentals need 150. Posting raises 2,550 on the housing allowance; a repeat posts nothing.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingEclTests
{
    private readonly AccountingFixture _fixture;

    public HousingEclTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Housing_debtors_are_provided_for_by_scheme_and_for_rentals()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4965", "Housing Interest Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "4975", "Rental Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "1295", "Housing Debtors Allowance", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(companyId, "6970", "Housing Impairment", AccountType.Expense));
        Customer buyer = new(companyId, "HE-BUY", "Scheme Buyer", Scenario.Usd);
        Customer tenant = new(companyId, "HE-TEN", "Council Tenant", Scenario.Usd) { PaymentTermsDays = 0 };
        Customer trade = new(companyId, "HE-TRD", "Trade Debtor", Scenario.Usd) { PaymentTermsDays = 0 };
        db.Customers.AddRange(buyer, tenant, trade);
        HousingScheme scheme = new(companyId, $"SCH-E-{companyId}", "Eastview", SchemeType.CouncilDelivered);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-HE-{companyId}", "Parcel", 1000m, 100000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-HE")).Value.LandParcelId;
        long standId = (await housing.SubdivideAsync(companyId, parcelId, scheme.Id, [new NewStand("HE-1", 500m), new NewStand("HE-2", 500m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds[0];
        (await scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>().SignAsync(new InstalmentSaleRequest
        {
            CompanyId = companyId, StandId = standId, BuyerCustomerId = buyer.Id, CashPrice = 13000m, Deposit = 1000m, AnnualRatePercent = 0m, Method = InterestMethod.Zero,
            TermMonths = 12, SignedOn = new DateOnly(2026, 1, 10), FirstDueDate = new DateOnly(2026, 2, 1), SalesAccountCode = "4100", StandsAvailableAccountCode = "1460",
            CostOfSalesAccountCode = "5100", InterestAccountCode = "4965", BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        (await rentals.AddUnitAsync(companyId, "HE-F1", "Flat", RentalUnitType.Flat, 2, 300m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.LetAsync(new LetUnitRequest { CompanyId = companyId, UnitCode = "HE-F1", TenantCustomerId = tenant.Id, StartDate = new DateOnly(2026, 3, 1) })).IsSuccess.Should().BeTrue();
        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 3, 1))).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = trade.Id, DocumentNumber = "HE-TRADE", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 1000m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();

        IHousingEclService ecl = scope.ServiceProvider.GetRequiredService<IHousingEclService>();
        HousingEclRequest request = new()
        {
            CompanyId = companyId, AsAtDate = new DateOnly(2026, 3, 31), AllowanceAccountCode = "1295", ExpenseAccountCode = "6970",
            InstalmentMatrix = new EclMatrix([new EclBand(0, 2m, "Current"), new EclBand(31, 20m, "Over 30 days")]),
            RentalMatrix = new EclMatrix([new EclBand(0, 5m, "Current"), new EclBand(30, 50m, "30 days and over")]),
        };

        HousingEclReport assessed = (await ecl.AssessAsync(request)).Value;
        assessed.Portfolios.Select(p => (p.Portfolio, p.Exposure, p.Provision)).Should().Equal((scheme.Code, 12000m, 2400m), ("Council rentals", 300m, 150m));
        assessed.Adjustment.Should().Be(2550m);

        HousingEclReport posted = (await ecl.PostAsync(request)).Value;
        posted.JournalNumber.Should().NotBeNull();
        HousingEclReport again = (await ecl.PostAsync(request)).Value;
        again.Adjustment.Should().Be(0m);
        again.JournalNumber.Should().BeNull();

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "1295").Credit.Should().Be(2550m);
        tb.Rows.Single(r => r.AccountCode == "6970").Debit.Should().Be(2550m);
    }
}

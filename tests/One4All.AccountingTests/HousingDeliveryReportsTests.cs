using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the applicant, statement and scheme-delivery housing reports (H21–H96): a scheme of three stands, one sold on
/// instalments to a waiting-list applicant; two applicants who look like one person; a transfer missing its documents;
/// a tenant billed rent. Each report reads those registers back.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingDeliveryReportsTests
{
    private readonly AccountingFixture _fixture;

    public HousingDeliveryReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Applicant_statement_and_delivery_reports_reflect_the_registers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        string buyerId = $"HD-A-{companyId}";
        db.Accounts.Add(new LedgerAccount(companyId, "4965", "Housing Interest Income", AccountType.Income));
        Customer buyer = new(companyId, "HD-BUY", "John Moyo", Scenario.Usd) { NationalIdNumber = buyerId };
        Customer tenant = new(companyId, "HD-TEN", "Delivery Tenant", Scenario.Usd);
        Customer other = new(companyId, "HD-OTH", "Next Owner", Scenario.Usd);
        db.Customers.AddRange(buyer, tenant, other);
        HousingScheme scheme = new(companyId, $"SCH-HD-{companyId}", "Delivery Scheme", SchemeType.CouncilDelivered) { TargetBeneficiaries = 4 };
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-HD-{companyId}", "Parcel", 1500m, 15000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-HD")).Value.LandParcelId;
        IReadOnlyList<long> stands = (await housing.SubdivideAsync(companyId, parcelId, scheme.Id, [new NewStand("HD-1", 250m), new NewStand("HD-2", 250m), new NewStand("HD-3", 1000m)],
            ApportionMethod.ByArea, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds;
        InstalmentSaleResult sale = (await scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>().SignAsync(new InstalmentSaleRequest
        {
            CompanyId = companyId, StandId = stands[0], BuyerCustomerId = buyer.Id, CashPrice = 13000m, Deposit = 1000m, AnnualRatePercent = 12m, Method = InterestMethod.ReducingBalance,
            TermMonths = 12, SignedOn = new DateOnly(2026, 1, 10), FirstDueDate = new DateOnly(2026, 2, 1), SalesAccountCode = "4100", StandsAvailableAccountCode = "1460",
            CostOfSalesAccountCode = "5100", InterestAccountCode = "4965", BankAccountCode = "1100",
        })).Value;

        // The buyer applied as John Moyo; "Jon Moyo" with the same birthday applied under another ID; a third applicant never confirmed.
        foreach ((string id, string name, DateOnly born, HousingCategory category) in new[]
        {
            (buyerId, "John Moyo", new DateOnly(1985, 1, 1), HousingCategory.HighDensity),
            ($"HD-B-{companyId}", "Jon Moyo", new DateOnly(1985, 1, 1), HousingCategory.HighDensity),
            ($"HD-C-{companyId}", "Tendai Dube", new DateOnly(1990, 5, 5), HousingCategory.LowDensity),
        })
        {
            HousingApplicant applicant = new(companyId, id, name, new DateOnly(2026, 2, 1));
            applicant.SetDetails(category, born, new DateOnly(2000, 1, 1), 2, false, 400m, CurrentTenure.Lodger);
            applicant.Register($"WL-{id}", applicant.RegistrationDate);
            db.HousingApplicants.Add(applicant);
        }

        await db.SaveChangesAsync();

        string transfer = (await scope.ServiceProvider.GetRequiredService<ITransferApplicationService>().LodgeAsync(new TransferLodgement
        {
            CompanyId = companyId, StandId = stands[0], TransferType = TransferType.Sale, ToCustomerId = other.Id, Consideration = 15000m, Date = new DateOnly(2026, 3, 1),
        })).Value;

        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        (await rentals.AddUnitAsync(companyId, "HD-F1", "Flat", RentalUnitType.Flat, 2, 300m, "4400", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.LetAsync(new LetUnitRequest { CompanyId = companyId, UnitCode = "HD-F1", TenantCustomerId = tenant.Id, StartDate = new DateOnly(2026, 3, 1) })).IsSuccess.Should().BeTrue();
        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 3, 1))).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, params (string Name, object? Value)[] extra)
        {
            Dictionary<string, object?> parameters = new() { ["AsAtDate"] = new DateOnly(2026, 4, 30) };
            foreach ((string name, object? value) in extra)
            {
                parameters[name] = value;
            }

            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string sheet = await Render("H21", ("WaitingListNumber", $"WL-{buyerId}"));
        sheet.Should().Contain("John Moyo").And.Contain("1985-01-01").And.Contain("HighDensity").And.Contain("Score,Total");
        string duplicates = await Render("H23");
        duplicates.Should().Contain($"WL-HD-B-{companyId}").And.Contain("Similar name and same date of birth").And.NotContain("Tendai Dube");
        (await Render("H25")).Should().Contain("Tendai Dube").And.Contain("never (registered 2026-02-01)");
        string movement = await Render("H28");
        movement.Should().Contain("2026-02,0,3,0,0,3").And.Contain("2026-04,3,0,0,0,3");
        string gap = await Render("H29");
        gap.Should().Contain("HighDensity,2,1,1").And.Contain("LowDensity,1,1,0");
        string statement = await Render("H43", ("Beneficiary", "HD-1"));
        statement.Should().Contain("HD-BUY John Moyo").And.Contain("Next due").And.Contain("13000").And.Contain("-1000");
        (await Render("H60")).Should().Contain(transfer).And.Contain("MunicipalClearance");
        (await Render("H74", ("Tenant", "HD-TEN"))).Should().Contain("Delivery Tenant").And.Contain("300").And.NotContain("John Moyo");
        (await Render("H82")).Should().Contain($"SCH-HD-{companyId},4,3,0,1,1,0,0.0%");
        (await Render("H83")).Should().Contain($"SCH-HD-{companyId},3,1,\"15,000.00\",\"13,000.00\",86.7%,\"5,000.00\"");
        (await Render("H96")).Should().Contain("HighDensity,Instalment sale,HighDensity,1");
    }
}

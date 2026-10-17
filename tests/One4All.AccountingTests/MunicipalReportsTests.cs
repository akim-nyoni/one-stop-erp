using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the M-series municipal reports (brief §14 I): a reviewed refuse billing run (two stands billed, three
/// exceptions) is summarised with its approver, its exceptions listed, the ratepayer aged by ward and service, and the
/// property register shows each stand's services; every M report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class MunicipalReportsTests
{
    private readonly AccountingFixture _fixture;

    public MunicipalReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Municipal_reports_reflect_billing_and_registers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Tariff refuse = new(companyId, "REF-MR", "Refuse Removal", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 30m };
        Tariff free = new(companyId, "REF-MRF", "Refuse Exempt", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 0m };
        Tariff water = new(companyId, "WAT-MR", "Water Stepped", ServiceType.Water, TariffType.Stepped, new DateOnly(2026, 1, 1), "4000");
        water.AddBlock(0m, 10m, 1.5m);
        water.AddBlock(10m, null, 2.25m);
        db.Tariffs.AddRange(refuse, free, water);
        Customer owner = new(companyId, "MR-OWN", "Rates Payer", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();

        Property Stand(string number, Tariff tariff)
        {
            Property p = new(companyId, number, "Ward 1", owner.Id);
            p.AddService(ServiceType.Refuse, tariff.Id, new DateOnly(2026, 1, 1));
            return p;
        }

        Property billed = Stand("MR3", refuse);
        db.Properties.AddRange(Stand("MR1", refuse), Stand("MR2", refuse), billed, Stand("MR5", free));
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>().BillServiceAsync(companyId, billed.Id, ServiceType.Refuse, 0m, $"RB-{billed.Id}-202604", new DateOnly(2026, 4, 2)))
            .IsSuccess.Should().BeTrue();

        IBillingRunService runs = scope.ServiceProvider.GetRequiredService<IBillingRunService>();
        BillingRunSummary proposed = (await runs.ProposeAsync(new BillingRunProposal
        {
            CompanyId = companyId, ServiceType = ServiceType.Refuse, BillDate = new DateOnly(2026, 4, 30), Ward = "Ward 1", InvoicePrefix = "RB", UserId = 71,
        })).Value;
        (await runs.ApproveAsync(companyId, proposed.RunNumber, 72)).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 5, 31) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("M146")).Should().Contain($"{proposed.RunNumber},Refuse,2026-04,Ward 1,Posted,2,2,0.00,").And.Contain(",60"); // the approver column shows whichever user 72 is in this database
        (await Render("M150")).Should().Contain("2026-04,Ward 1,Refuse,60.00,0.00,0.0%,60", "the run billed 60 and nothing is collected yet");
        (await Render("M165")).Should().NotContain("MR1", "no deposits are held");
        string exceptions = await Render("M147");
        exceptions.Should().Contain($"{proposed.RunNumber},MR3,AlreadyBilled").And.Contain($"{proposed.RunNumber},MR5,NothingToBill");
        (await Render("M148")).Should().Contain("Ward 1,Domestic,Refuse,1,");
        (await Render("M152")).Should().Contain("MR1,Ward 1,—,—,Rates Payer,Refuse (REF-MR),—,0").And.Contain("Refuse (REF-MRF)");

        (await Render("M168")).Should().Contain("REF-MR,Refuse Removal,Refuse,Flat,Domestic,All,2026-01-01,fixed 30,—,Yes")
            .And.Contain("WAT-MR,Water Stepped,Water,Stepped,Domestic,All,2026-01-01,—,0–10 @ 1.5; 10–∞ @ 2.25,Yes");

        (await Render("M169")).Should().Contain("Billed: Refuse,4000,2,60");
        (await Render("M170")).Should().Contain("Ward Ward 1,Properties,4").And.Contain("Ward Ward 1,Billed 2026 to date,60.00");
        string statutory = await Render("M171");
        statutory.Should().Contain("Domestic,1,").And.Contain("Total,1,");

        foreach (string code in MunicipalReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}

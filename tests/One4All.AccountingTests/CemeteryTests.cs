using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the cemetery register: three plots at 300 are added to section B of Westpark (a repeat is refused), a single
/// plot and a family plot for two; an unsold plot cannot take an interment; the single plot is sold (300 invoiced) and
/// cannot be sold again; one burial with a 50 fee fills it; the family plot sold for 300 takes two burials and then
/// refuses a third; the available list and burial register reflect it all; plot and burial income is 650.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CemeteryTests
{
    private readonly AccountingFixture _fixture;

    public CemeteryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Plots_are_sold_and_interments_recorded_up_to_capacity()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4955", "Cemetery Income", AccountType.Income));
        Customer family = new(companyId, "CEM-FAM", "Moyo Family", Scenario.Usd);
        db.Customers.Add(family);
        await db.SaveChangesAsync();

        ICemeteryService cemeteries = scope.ServiceProvider.GetRequiredService<ICemeteryService>();
        (await cemeteries.AddPlotsAsync(companyId, "Westpark", "B", ["1", "2", "3"], 300m, 1)).Value.Should().Be(3);
        (await cemeteries.AddPlotsAsync(companyId, "Westpark", "B", ["3"], 300m, 1)).Error.Code.Should().Be("Mun.PlotExists");
        (await cemeteries.AddPlotsAsync(companyId, "Westpark", "F", ["1"], 300m, 2)).IsSuccess.Should().BeTrue();

        IntermentRequest Burial(string plot, string name, int day, decimal fee = 0m) => new()
        {
            CompanyId = companyId, PlotReference = plot, DeceasedName = name, DateOfDeath = new DateOnly(2026, 3, day), IntermentDate = new DateOnly(2026, 3, day + 4),
            IntermentFee = fee, FeeAccountCode = fee > 0m ? "4955" : null,
        };

        (await cemeteries.RecordIntermentAsync(Burial("westpark-b-1", "J. Moyo", 1))).Error.Code.Should().Be("Mun.PlotNotSold");
        (await cemeteries.SellAsync(companyId, "westpark-b-1", family.Id, new DateOnly(2026, 3, 2), "4955")).Value.Should().StartWith("CP-");
        (await cemeteries.SellAsync(companyId, "WESTPARK-B-1", family.Id, new DateOnly(2026, 3, 2), "4955")).Error.Code.Should().Be("Mun.PlotSold");
        (await cemeteries.RecordIntermentAsync(Burial("WESTPARK-B-1", "J. Moyo", 1, 50m))).Value.Should().StartWith("BUR-");
        (await cemeteries.RecordIntermentAsync(Burial("WESTPARK-B-1", "Another", 5))).Error.Code.Should().Be("Mun.PlotFull");

        (await cemeteries.SellAsync(companyId, "WESTPARK-F-1", family.Id, new DateOnly(2026, 3, 3), "4955")).IsSuccess.Should().BeTrue();
        (await cemeteries.RecordIntermentAsync(Burial("WESTPARK-F-1", "A. Moyo", 6))).IsSuccess.Should().BeTrue();
        (await cemeteries.RecordIntermentAsync(Burial("WESTPARK-F-1", "B. Moyo", 9))).IsSuccess.Should().BeTrue();
        (await cemeteries.RecordIntermentAsync(Burial("WESTPARK-F-1", "C. Moyo", 12))).Error.Code.Should().Be("Mun.PlotFull");

        (await cemeteries.PlotsAsync(companyId, "Westpark", CemeteryPlotStatus.Available)).Select(p => p.Reference).Should().Equal("WESTPARK-B-2", "WESTPARK-B-3");
        (await cemeteries.PlotsAsync(companyId, "Westpark", CemeteryPlotStatus.Full)).Should().Equal(
            new PlotEntry("WESTPARK-B-1", CemeteryPlotStatus.Full, 300m, 1, 1, "CEM-FAM"),
            new PlotEntry("WESTPARK-F-1", CemeteryPlotStatus.Full, 300m, 2, 2, "CEM-FAM"));
        (await cemeteries.BurialRegisterAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))).Select(b => b.DeceasedName).Should().Equal("J. Moyo", "A. Moyo", "B. Moyo");

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "4955").Credit.Should().Be(650m);
    }
}

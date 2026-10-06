using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Core.Accounting;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers asset components and cost-centre analysis: a component is registered under its parent and depreciated on its own; the asset's
/// journals carry its department and cost centre; a transfer moves cost and accumulated depreciation between analyses; a parent cannot
/// be disposed of while it has components in use.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AssetComponentTests
{
    private readonly AccountingFixture _fixture;

    public AssetComponentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Components_and_analysis_follow_the_asset()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Departments.AddRange(new Department(companyId, "OPS", "Operations"), new Department(companyId, "FIN", "Finance"));
        db.CostCentres.Add(new CostCentre(companyId, "HQ", "Head Office"));
        await db.SaveChangesAsync();
        IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
        RegisterAssetRequest Asset(string number, decimal cost, int months, string? parent = null, string? department = "OPS") => new()
        {
            CompanyId = companyId, AssetNumber = number, Description = number, AssetClass = "Buildings", Cost = cost, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 1, 1),
            Method = DepreciationMethod.StraightLine, UsefulLifeMonths = months, CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
            FundingAccountCode = "1100", DocumentNumber = $"ACQ-{number}", ParentAssetNumber = parent, CostCentre = "HQ", Department = department,
        };

        (await fa.RegisterAsync(Asset("AC-X", 1_000m, 10, department: "NOPE"))).Error.Code.Should().Be("Fa.UnknownDimension");
        (await fa.RegisterAsync(Asset("AC-ROOF", 1_000m, 10, parent: "AC-MISSING"))).Error.Code.Should().Be("Fa.ParentNotFound");
        long building = (await fa.RegisterAsync(Asset("AC-BLDG", 100_000m, 500))).Value;
        long roof = (await fa.RegisterAsync(Asset("AC-ROOF", 20_000m, 100, parent: "AC-BLDG"))).Value;
        (await fa.ComponentsAsync(companyId, building)).Should().ContainSingle(c => c.AssetNumber == "AC-ROOF" && c.Cost == 20_000m);

        // January depreciation: 200 on the building and 200 on the roof, both analysed to OPS / HQ.
        (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 1, 31), "DEP-AC-01")).Value.TotalDepreciation.Should().Be(400m);

        // Moving the building to Finance carries its cost and depreciation with it.
        (await fa.ReassignAsync(companyId, building, "HQ", "FIN", new DateOnly(2026, 2, 1), "TRF-AC-1")).IsSuccess.Should().BeTrue();
        var balances = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines)
            .Where(l => l.AccountCode == "1600" || l.AccountCode == "1650").GroupBy(l => new { l.AccountCode, l.Department })
            .Select(g => new { g.Key.AccountCode, g.Key.Department, Net = g.Sum(l => l.BaseDebit - l.BaseCredit) }).ToListAsync();
        balances.Should().Contain(b => b.AccountCode == "1600" && b.Department == "FIN" && b.Net == 100_000m)
            .And.Contain(b => b.AccountCode == "1600" && b.Department == "OPS" && b.Net == 20_000m)
            .And.Contain(b => b.AccountCode == "1650" && b.Department == "FIN" && b.Net == -200m)
            .And.Contain(b => b.AccountCode == "1650" && b.Department == "OPS" && b.Net == -200m);

        // The building goes only after its roof.
        (await fa.DisposeAsync(companyId, building, 0m, new DateOnly(2026, 3, 1), "1100", "4900", "6900", "DSP-BLDG")).Error.Code.Should().Be("Fa.HasComponents");
        (await fa.DisposeAsync(companyId, roof, 0m, new DateOnly(2026, 3, 1), "1100", "4900", "6900", "DSP-ROOF")).IsSuccess.Should().BeTrue();
        (await fa.DisposeAsync(companyId, building, 0m, new DateOnly(2026, 3, 1), "1100", "4900", "6900", "DSP-BLDG")).IsSuccess.Should().BeTrue();
        (await db.FixedAssets.AsNoTracking().CountAsync(a => a.CompanyId == companyId && a.Status == FixedAssetStatus.Disposed)).Should().Be(2);
    }
}

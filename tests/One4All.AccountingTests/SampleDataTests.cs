using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.MasterData;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>The sample data loads into a freshly set-up company across the modules, with balanced books, and refuses to load twice.</summary>
[Collection(AccountingCollection.Name)]
public sealed class SampleDataTests
{
    private readonly AccountingFixture _fixture;

    public SampleDataTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Sample_data_fills_a_new_company_and_loads_only_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await Scenario.SeedAsync(db); // the shared currencies

        IndustrySetupResult company = (await scope.ServiceProvider.GetRequiredService<IIndustrySetupService>().SetUpCompanyAsync(new IndustrySetupRequest
        {
            Code = "SMP" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Name = "Sample Co", BaseCurrency = CurrencyCode.Parse("USD"), Verticals = Enum.GetValues<Vertical>(),
            FiscalYearStart = new DateOnly(DateTime.Today.Year, 1, 1), VatRatePercent = 15m,
        })).Value;

        ISampleDataService samples = scope.ServiceProvider.GetRequiredService<ISampleDataService>();
        var loaded = await samples.LoadAsync(company.CompanyId);
        loaded.IsSuccess.Should().BeTrue(loaded.IsFailure ? loaded.Error.Message : null);
        loaded.Value.Loaded.Should().HaveCountGreaterThanOrEqualTo(8);

        long id = company.CompanyId;
        (await db.Items.CountAsync(i => i.CompanyId == id && i.Code.StartsWith("SMP-"))).Should().Be(8);
        (await db.Customers.CountAsync(c => c.CompanyId == id && c.Code.StartsWith("SMP-"))).Should().Be(10);
        (await db.Employees.CountAsync(e => e.CompanyId == id && e.EmployeeNumber.StartsWith("SMP-"))).Should().Be(8);
        (await db.Properties.CountAsync(p => p.CompanyId == id)).Should().Be(8);
        (await db.MeterReadings.CountAsync(r => r.CompanyId == id)).Should().Be(24);
        (await db.HousingStands.CountAsync(s => s.CompanyId == id)).Should().Be(12);
        (await db.HousingApplicants.CountAsync(a => a.CompanyId == id)).Should().Be(8);
        (await db.FixedAssets.CountAsync(a => a.CompanyId == id)).Should().Be(3);
        (await db.OpenItems.CountAsync(o => o.CompanyId == id && o.DocumentNumber.StartsWith("SMP-INV-"))).Should().Be(19);

        // The books balance.
        var lines = await db.Journals.Where(j => j.CompanyId == id).SelectMany(j => j.Lines).ToListAsync();
        lines.Sum(l => l.BaseDebit).Should().Be(lines.Sum(l => l.BaseCredit));
        lines.Should().NotBeEmpty();

        (await samples.LoadAsync(company.CompanyId)).Error.Code.Should().Be("Sample.AlreadyLoaded");
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the posting engine's dimension check (brief §3.2: accounts "permitted for the company/cost centre/department"): a line may only name a
/// cost centre or department the company has set up and still uses; a blank one is fine.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PostingDimensionTests
{
    private readonly AccountingFixture _fixture;

    public PostingDimensionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Lines_name_only_the_companys_active_cost_centres_and_departments()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long other = await Scenario.SeedAsync(db);
        await Scenario.DimensionsAsync(db, companyId, "OPS");
        await Scenario.DimensionsAsync(db, other, "ZZZ");
        db.CostCentres.Add(new CostCentre(companyId, "OLD", "Closed") { IsActive = false });
        await db.SaveChangesAsync();
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();

        Task<Core.Common.Result<PostingReceipt>> Post(string? costCentre, string? department) => engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false,
            Lines =
            [
                new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 10m, CostCentre = costCentre, Department = department },
                new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Credit, TransactionAmount = 10m },
            ],
        });

        (await Post("OPS", "ops")).IsSuccess.Should().BeTrue("codes are matched whatever their case");
        (await Post(null, null)).IsSuccess.Should().BeTrue("a line need not name a dimension");
        (await Post("XYZ", null)).Error.Code.Should().Be("Posting.CostCentreUnknown");
        (await Post("OLD", null)).Error.Code.Should().Be("Posting.CostCentreUnknown", "a closed cost centre takes no new postings");
        (await Post(null, "ZZZ")).Error.Code.Should().Be("Posting.DepartmentUnknown", "another company's department");
    }
}

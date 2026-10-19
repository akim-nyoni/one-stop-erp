using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>A prepayment is released to expense a month at a time over the months it covers, each month once, the last month taking the rounding.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PrepaymentScheduleTests
{
    private readonly AccountingFixture _fixture;

    public PrepaymentScheduleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Prepayments_release_month_by_month()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IAccrualService accruals = scope.ServiceProvider.GetRequiredService<IAccrualService>();
        (await accruals.CreatePrepaymentScheduleAsync(new NewPrepaymentSchedule(companyId, "ps-ins", "Insurance", "1350", "6800", 1_000m, new DateOnly(2026, 1, 15), 3))).IsSuccess.Should().BeTrue();
        (await accruals.CreatePrepaymentScheduleAsync(new NewPrepaymentSchedule(companyId, "PS-INS", "Again", "1350", "6800", 10m, new DateOnly(2026, 1, 1), 1))).Error.Code.Should().Be("GL.PrepaymentExists");
        (await accruals.CreatePrepaymentScheduleAsync(new NewPrepaymentSchedule(companyId, "PS-BAD", "Bad", "1350", "6800", 10m, new DateOnly(2026, 1, 1), 0))).Error.Code.Should().Be("GL.PrepaymentTerms");

        // By 28 February only January has ended: 333.33. Run again, nothing more. By the end of March the rest: 333.33 and the last 333.34.
        PrepaymentRelease february = (await accruals.ReleasePrepaymentsAsync(companyId, new DateOnly(2026, 2, 27))).Value;
        (february.Months, february.Amount).Should().Be((1, 333.33m));
        (await accruals.ReleasePrepaymentsAsync(companyId, new DateOnly(2026, 2, 27))).Value.Months.Should().Be(0);
        PrepaymentRelease march = (await accruals.ReleasePrepaymentsAsync(companyId, new DateOnly(2026, 3, 31))).Value;
        (march.Months, march.Amount).Should().Be((2, 666.67m));
        PrepaymentSchedule done = await db.PrepaymentSchedules.AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.Reference == "PS-INS");
        (done.IsFinished, done.Released).Should().Be((true, 1_000m));
        decimal charged = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.DocumentType == "GL-PREPREL").SelectMany(j => j.Lines).Where(l => l.AccountCode == "6800")
            .SumAsync(l => l.BaseDebit);
        charged.Should().Be(1_000m);
    }
}

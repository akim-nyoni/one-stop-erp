using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers an accrual whose automatic reversal cannot post (the next period is closed): the accrual itself is not left
/// on the ledger un-reversed; once the period reopens, the accrual and its reversal post together.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AccrualAtomicityTests
{
    private readonly AccountingFixture _fixture;

    public AccrualAtomicityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_accrual_without_its_reversal_is_not_posted()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long yearId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).SingleAsync();
            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 4, "GL", PeriodStatus.Closed)).IsSuccess.Should().BeTrue();
        }

        DateOnly march31 = new(2026, 3, 31);
        DateOnly april1 = new(2026, 4, 1);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAccrualService accruals = scope.ServiceProvider.GetRequiredService<IAccrualService>();
            (await accruals.PostAccrualAsync(companyId, march31, april1, "6700", "2470", 75m, "ACC-1", "Audit fee")).IsFailure.Should().BeTrue();
            await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Journals.CountAsync(j => j.CompanyId == companyId && j.DocumentType == "GL-ACCR")).Should().Be(0);

            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 4, "GL", PeriodStatus.Open)).IsSuccess.Should().BeTrue();
            var posted = await scope.ServiceProvider.GetRequiredService<IAccrualService>().PostAccrualAsync(companyId, march31, april1, "6700", "2470", 75m, "ACC-1", "Audit fee");
            posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.ToString() : null);
            posted.Value.ReversalJournalNumber.Should().NotBeNullOrEmpty();
            (await db.Journals.CountAsync(j => j.CompanyId == companyId && j.PostingDate == april1)).Should().Be(1);
        }
    }
}

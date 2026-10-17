using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers closing a period permanently while a deferred journal of that module is dated in it: refused (the journal
/// could never be released), though a plain close - which can be reopened - and another module's permanent close are
/// allowed; once the journal is released the permanent close goes through.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PeriodPermanentCloseTests
{
    private readonly AccountingFixture _fixture;

    public PeriodPermanentCloseTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Permanent_close_waits_for_the_periods_deferred_journals()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long yearId;
        long deferredId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).SingleAsync();

            deferredId = (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 15), TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = false, Deferred = true,
                Lines =
                [
                    new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 20m },
                    new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 20m },
                ],
            })).Value.JournalId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPeriodService periods = scope.ServiceProvider.GetRequiredService<IPeriodService>();

            Result refused = await periods.SetStatusAsync(companyId, yearId, 3, "GL", PeriodStatus.PermanentlyClosed);
            refused.Error.Code.Should().Be("Period.UnpostedJournals");

            // A plain close can be undone, and another module's permanent close is unaffected.
            (await periods.SetStatusAsync(companyId, yearId, 3, "GL", PeriodStatus.Closed)).IsSuccess.Should().BeTrue();
            (await periods.SetStatusAsync(companyId, yearId, 3, "AR", PeriodStatus.PermanentlyClosed)).IsSuccess.Should().BeTrue();

            // Reopen, release, then close for good.
            (await periods.SetStatusAsync(companyId, yearId, 3, "GL", PeriodStatus.Open)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().ReleaseDeferredAsync(companyId, deferredId)).IsSuccess.Should().BeTrue();
            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 3, "GL", PeriodStatus.PermanentlyClosed)).IsSuccess.Should().BeTrue();
        }
    }
}

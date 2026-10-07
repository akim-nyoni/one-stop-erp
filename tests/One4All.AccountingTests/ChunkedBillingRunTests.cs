using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers chunked, resumable billing: a 30-stand run approved with a cap posts in chunks reporting progress and stays Posting; a bill
/// posted outside the run in the meantime is recognised, not posted twice; a ratepayer on hold fails alone while the rest post; the
/// resumed run finishes Posted with its counts.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ChunkedBillingRunTests
{
    private readonly AccountingFixture _fixture;

    public ChunkedBillingRunTests(AccountingFixture fixture) => _fixture = fixture;

    private sealed class Recorder : IProgress<BillingRunProgress>
    {
        public List<BillingRunProgress> Reports { get; } = [];

        public void Report(BillingRunProgress value) => Reports.Add(value);
    }

    [SkippableFact]
    public async Task A_large_run_posts_in_resumable_chunks()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff refuse = new(companyId, "REF-CH", "Refuse Removal", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 20m };
        db.Tariffs.Add(refuse);
        List<Customer> owners = [.. Enumerable.Range(1, 30).Select(n => new Customer(companyId, $"CH-{n:D3}", $"Owner {n}", Scenario.Usd) { OnHold = n == 25 })];
        db.Customers.AddRange(owners);
        await db.SaveChangesAsync();
        foreach (Customer owner in owners)
        {
            Property stand = new(companyId, owner.Code.Replace("CH-", "S", StringComparison.Ordinal), "Ward 9", owner.Id);
            stand.AddService(ServiceType.Refuse, refuse.Id, new DateOnly(2026, 1, 1));
            db.Properties.Add(stand);
        }

        await db.SaveChangesAsync();
        IBillingRunService runs = scope.ServiceProvider.GetRequiredService<IBillingRunService>();
        BillingRunSummary proposed = (await runs.ProposeAsync(new BillingRunProposal
        {
            CompanyId = companyId, ServiceType = ServiceType.Refuse, BillDate = new DateOnly(2026, 5, 31), Ward = "Ward 9", InvoicePrefix = "CH", UserId = 81,
        })).Value;
        proposed.Included.Should().Be(30);

        // Approved with a cap of 12 in chunks of 8: two chunks, then it stops part-way.
        Recorder progress = new();
        BillingRunSummary first = (await runs.ApproveAsync(companyId, proposed.RunNumber, 82, new BillingPostOptions { ChunkSize = 8, MaxBills = 12, Progress = progress })).Value;
        (first.Status, first.Posted, first.Failed, first.Remaining).Should().Be((BillingRunStatus.Posting, 12, 0, 18));
        progress.Reports.Should().Equal(new BillingRunProgress(8, 0, 30), new BillingRunProgress(12, 0, 30));
        (await runs.ApproveAsync(companyId, proposed.RunNumber, 82)).IsFailure.Should().BeTrue("it is no longer proposed");

        // Meanwhile one of the remaining bills reaches the account another way (as after an interruption).
        List<BillingRunLineView> lines = (await runs.LinesAsync(companyId, proposed.RunNumber, exceptionsOnly: false)).Value.ToList();
        BillingRunLineView later = lines.OrderBy(l => l.StandNumber).Last();
        Customer laterOwner = owners.Single(o => o.Code == later.StandNumber.Replace("S", "CH-", StringComparison.Ordinal));
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = laterOwner.Id, DocumentNumber = later.InvoiceNumber, DocumentDate = new DateOnly(2026, 5, 31), Currency = Scenario.Usd, NetAmount = 20m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();

        // Resumed: the rest post, the held ratepayer fails alone, the bill already raised is not raised twice.
        BillingRunSummary done = (await runs.ResumeAsync(companyId, proposed.RunNumber, new BillingPostOptions { ChunkSize = 10 })).Value;
        (done.Status, done.Posted, done.Failed, done.Remaining).Should().Be((BillingRunStatus.Posted, 29, 1, 0));
        (await db.OpenItems.AsNoTracking().CountAsync(i => i.CompanyId == companyId && i.DocumentNumber == later.InvoiceNumber)).Should().Be(1);
        (await db.OpenItems.AsNoTracking().CountAsync(i => i.CompanyId == companyId && i.DocumentNumber.StartsWith("CH-"))).Should().Be(29);
        BillingRunLineView failed = (await runs.LinesAsync(companyId, proposed.RunNumber, exceptionsOnly: true)).Value.Single(l => l.StandNumber == "S025");
        failed.Message.Should().Contain("on hold");
        (await runs.ResumeAsync(companyId, proposed.RunNumber, new BillingPostOptions())).Error.Code.Should().Be("Mun.NotPosting");
    }
}

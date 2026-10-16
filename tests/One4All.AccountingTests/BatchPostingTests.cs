using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the batch posting paths a billing run uses: several journals post as one unit with consecutive numbers, one period-balance update per
/// account and one audit entry; a journal the batch cannot take is refused as a whole (the caller posts singly); several invoices post as one batch
/// with an open item each, and a held customer or a credit-limit policy sends the batch to the single path.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BatchPostingTests
{
    private readonly AccountingFixture _fixture;

    public BatchPostingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Journals_post_as_one_batch_with_consecutive_numbers_and_summed_balances()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        DateOnly date = new(2026, 5, 31);
        static JournalLineRequest L(string account, JournalSide side, decimal amount) => new() { AccountCode = account, Side = side, TransactionAmount = amount };
        JournalRequest One(int n) => new()
        {
            CompanyId = companyId, SourceModule = "AR", PostingDate = date, TransactionCurrency = Scenario.Usd, DocumentType = "SalesInvoice", DocumentNumber = $"BATCH-{n}",
            Lines = [L("1200", JournalSide.Debit, 10m * n), L("4000", JournalSide.Credit, 10m * n)],
        };

        decimal before = await db.PeriodBalances.AsNoTracking().Where(b => b.CompanyId == companyId && b.AccountCode == "4000").SumAsync(b => b.BaseCredit);
        Result<IReadOnlyList<PostingReceipt>> posted = await engine.PostBatchAsync([One(1), One(2), One(3)]);
        posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.Message : null);
        posted.Value.Should().HaveCount(3);
        posted.Value.Select(r => r.JournalId).Should().OnlyHaveUniqueItems().And.NotContain(0);
        int[] numbers = [.. posted.Value.Select(r => int.Parse(r.JournalNumber[^6..], System.Globalization.CultureInfo.InvariantCulture))];
        numbers.Should().Equal(numbers[0], numbers[0] + 1, numbers[0] + 2);
        (await db.Journals.AsNoTracking().CountAsync(j => j.CompanyId == companyId && j.DocumentNumber!.StartsWith("BATCH-") && j.Status == JournalStatus.Posted)).Should().Be(3);
        (await db.PeriodBalances.AsNoTracking().Where(b => b.CompanyId == companyId && b.AccountCode == "4000").SumAsync(b => b.BaseCredit)).Should().Be(before + 60m);

        // What the batch cannot take is refused as a whole, and nothing of it is posted.
        Result<IReadOnlyList<PostingReceipt>> refused = await engine.PostBatchAsync([One(4), new JournalRequest
        {
            CompanyId = companyId, SourceModule = "AR", PostingDate = date, TransactionCurrency = Scenario.Usd, DocumentNumber = "BATCH-5", Deferred = true,
            Lines = [L("1200", JournalSide.Debit, 1m), L("4000", JournalSide.Credit, 1m)],
        }]);
        refused.Error.Code.Should().Be("Posting.BatchUnsupported");
        (await db.Journals.AsNoTracking().AnyAsync(j => j.CompanyId == companyId && j.DocumentNumber == "BATCH-4")).Should().BeFalse();

        // A bad line fails the batch as a whole too.
        (await engine.PostBatchAsync([One(6), new JournalRequest
        {
            CompanyId = companyId, SourceModule = "AR", PostingDate = date, TransactionCurrency = Scenario.Usd, DocumentNumber = "BATCH-7",
            Lines = [L("1200", JournalSide.Debit, 1m), L("NOPE", JournalSide.Credit, 1m)],
        }])).Error.Code.Should().Be("Posting.AccountNotFound");
        (await db.Journals.AsNoTracking().AnyAsync(j => j.CompanyId == companyId && j.DocumentNumber == "BATCH-6")).Should().BeFalse();
    }

    [SkippableFact]
    public async Task Invoices_post_as_one_batch_and_fall_back_when_a_customer_needs_the_single_path()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer a = new(companyId, "BI-A", "Batch A", Scenario.Usd);
        Customer b = new(companyId, "BI-B", "Batch B", Scenario.Usd);
        Customer held = new(companyId, "BI-H", "Batch Held", Scenario.Usd) { OnHold = true };
        Customer limited = new(companyId, "BI-L", "Batch Limited", Scenario.Usd) { CreditLimit = 50m, CreditLimitPolicy = CreditLimitPolicy.Block };
        db.Customers.AddRange(a, b, held, limited);
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        SalesInvoiceRequest Invoice(Customer c, string number, decimal net) => new()
        {
            CompanyId = companyId, CustomerId = c.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 5, 31), Currency = Scenario.Usd, NetAmount = net, SalesAccountCode = "4000",
        };

        Result<IReadOnlyList<TradingPostingResult>> posted = await ar.PostInvoicesAsync([Invoice(a, "BI-1", 20m), Invoice(b, "BI-2", 30m)]);
        posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.Message : null);
        posted.Value.Select(r => r.BaseAmount).Should().Equal(20m, 30m);
        List<OpenItem> items = await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentNumber.StartsWith("BI-")).OrderBy(i => i.DocumentNumber).ToListAsync();
        items.Select(i => (i.TraderId, i.OutstandingBase, i.JournalEntryId != null)).Should().Equal((a.Id, 20m, true), (b.Id, 30m, true));

        (await ar.PostInvoicesAsync([Invoice(a, "BI-3", 1m), Invoice(held, "BI-4", 1m)])).Error.Code.Should().Be("Ar.CustomerOnHold");
        (await ar.PostInvoicesAsync([Invoice(a, "BI-5", 1m), Invoice(limited, "BI-6", 1m)])).Error.Code.Should().Be("Ar.BatchUnsupported");
        (await db.OpenItems.AsNoTracking().CountAsync(i => i.CompanyId == companyId && i.DocumentNumber.StartsWith("BI-"))).Should().Be(2);
    }
}

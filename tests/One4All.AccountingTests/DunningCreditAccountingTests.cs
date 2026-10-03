using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers dunning with unallocated credits: a customer whose unallocated receipt covers the overdue invoice is not
/// chased (and a letter is refused), and one whose credit note covers the oldest invoice drops from a final notice
/// to a first reminder for the remainder, with the deduction shown on the letter.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DunningCreditAccountingTests
{
    private readonly AccountingFixture _fixture;

    public DunningCreditAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Unallocated_credits_reduce_or_remove_reminders()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long paidUp;
        long partly;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer a = new(companyId, "DUN-PAID", "Paid Up Co", Scenario.Usd) { PaymentTermsDays = 0 };
            Customer b = new(companyId, "DUN-PART", "Part Credit Co", Scenario.Usd) { PaymentTermsDays = 0 };
            db.Customers.AddRange(a, b);
            await db.SaveChangesAsync();
            (paidUp, partly) = (a.Id, b.Id);

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();

            // A: 1,000 due 1 January, paid in March but the receipt never allocated.
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = a.Id, DocumentNumber = "DA-1", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 1_000m, SalesAccountCode = "4000" });
            await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = a.Id, DocumentNumber = "DA-R", ReceiptDate = new DateOnly(2026, 3, 20), Currency = Scenario.Usd, Amount = 1_000m, BankAccountCode = "1100" });

            // B: 300 due 1 January (credited in full, unallocated) and 500 due 15 March.
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = b.Id, DocumentNumber = "DB-1", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 300m, SalesAccountCode = "4000" });
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = b.Id, DocumentNumber = "DB-2", DocumentDate = new DateOnly(2026, 3, 15), Currency = Scenario.Usd, NetAmount = 500m, SalesAccountCode = "4000" });
            (await ar.PostCreditNoteAsync(new SalesCreditNoteRequest { CompanyId = companyId, CustomerId = b.Id, DocumentNumber = "DB-CN", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 300m, SalesAccountCode = "4000" })).IsSuccess.Should().BeTrue();
        }

        DateOnly asAt = new(2026, 4, 1);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IDunningService dunning = scope.ServiceProvider.GetRequiredService<IDunningService>();
            IReadOnlyList<DunningNotice> notices = (await dunning.AssessAsync(companyId, asAt, DunningStages.Default)).Value;

            notices.Should().NotContain(n => n.CustomerId == paidUp);
            notices.Should().ContainSingle(n => n.CustomerId == partly).Which.Should().BeEquivalentTo(new { Level = 1, OldestDaysOverdue = 17, OverdueAmount = 500m });

            (await dunning.RenderReminderTextAsync(companyId, paidUp, asAt, DunningStages.Default)).Error.Code.Should().Be("Dunning.CoveredByCredits");

            string letter = (await dunning.RenderReminderTextAsync(companyId, partly, asAt, DunningStages.Default)).Value;
            letter.Should().Contain("FIRST REMINDER").And.Contain("Less payments / credits not yet allocated").And.Contain("(300.00)");
            letter.Split('\n').Single(l => l.StartsWith("TOTAL OVERDUE", StringComparison.Ordinal)).Should().Contain("500.00");
        }
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// End-to-end reminder letter: a customer's overdue invoices are listed at the dunning level the oldest
/// warrants, and customers with nothing overdue are refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DunningLetterTests
{
    private readonly AccountingFixture _fixture;

    public DunningLetterTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reminder_letter_lists_overdue_invoices_at_the_warranted_level()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long overdueId;
        long cleanId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer overdue = new(companyId, "C-OVER", "Overdue Co", Scenario.Usd) { PaymentTermsDays = 0 };
            Customer clean = new(companyId, "C-CLEAN", "Clean Co", Scenario.Usd) { PaymentTermsDays = 0 };
            db.Customers.AddRange(overdue, clean);
            await db.SaveChangesAsync();
            overdueId = overdue.Id;
            cleanId = clean.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            // Due 2026-01-01 (90 days by 2026-04-01 → final notice) and 2026-03-01 (31 days).
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = overdueId, DocumentNumber = "INV-OLD", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 1000m, TaxAmount = 0m, SalesAccountCode = "4000" });
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = overdueId, DocumentNumber = "INV-NEW", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 500m, TaxAmount = 0m, SalesAccountCode = "4000" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IDunningService dunning = scope.ServiceProvider.GetRequiredService<IDunningService>();
            DateOnly asAt = new(2026, 4, 1);

            var letter = await dunning.RenderReminderTextAsync(companyId, overdueId, asAt, DunningStages.Default);
            letter.IsSuccess.Should().BeTrue(letter.IsFailure ? letter.Error.ToString() : null);
            string text = letter.Value;
            text.Should().Contain("FINAL NOTICE");          // oldest item is 90 days overdue
            text.Should().Contain("To: Overdue Co");
            text.Should().Contain("INV-OLD").And.Contain("INV-NEW");
            text.Should().Contain("1,500.00");              // 1000 + 500 total overdue

            (await dunning.RenderReminderTextAsync(companyId, cleanId, asAt, DunningStages.Default)).Error.Code.Should().Be("Dunning.NothingOverdue");
            (await dunning.RenderReminderTextAsync(companyId, 999_999, asAt, DunningStages.Default)).Error.Code.Should().Be("Dunning.CustomerNotFound");
        }
    }
}

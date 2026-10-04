using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Scheduling;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Communications;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers bulk statements by email: customers owing at least the minimum with an email address get their statement merged
/// into the template and queued in one batch; those without email, below the minimum or paid up are left out and counted;
/// a missing template is refused; and the monthly scheduled task queues last month's statements.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StatementDespatchTests
{
    private readonly AccountingFixture _fixture;

    public StatementDespatchTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Statements_are_emailed_to_customers_who_owe()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer emailed = new(companyId, "ST-A", "Alpha Traders", Scenario.Usd) { Email = "accounts@alpha.example" };
        Customer noEmail = new(companyId, "ST-B", "Beta Stores", Scenario.Usd);
        Customer tiny = new(companyId, "ST-C", "Gamma", Scenario.Usd) { Email = "gamma@example.com" };
        Customer paid = new(companyId, "ST-D", "Delta", Scenario.Usd) { Email = "delta@example.com" };
        db.Customers.AddRange(emailed, noEmail, tiny, paid);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        async Task Invoice(Customer customer, decimal amount) => (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = $"ST-{customer.Code}-{companyId}", DocumentDate = new DateOnly(2026, 4, 10), Currency = Scenario.Usd, NetAmount = amount, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();
        await Invoice(emailed, 500m);
        await Invoice(noEmail, 300m);
        await Invoice(tiny, 0.5m);
        await Invoice(paid, 200m);
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = paid.Id, DocumentNumber = $"ST-R-{companyId}", ReceiptDate = new DateOnly(2026, 4, 20), Currency = Scenario.Usd, Amount = 200m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();

        IStatementDespatchService statements = scope.ServiceProvider.GetRequiredService<IStatementDespatchService>();
        DateOnly monthEnd = new(2026, 4, 30);
        (await statements.QueueAsync(companyId, monthEnd, "STATEMENT", "en", 1m)).Error.Code.Should().Be("Msg.TemplateNotFound");
        INotificationService messages = scope.ServiceProvider.GetRequiredService<INotificationService>();
        (await messages.SaveTemplateAsync(companyId, "STATEMENT", "Statement", "en", MessageChannel.Email, "Statement {{Account}} at {{AsAt}}",
            "Dear {{Name}},\nYour balance is {{Currency}} {{Balance}}.\n\n{{Statement}}")).IsSuccess.Should().BeTrue();

        StatementDespatch queued = (await statements.QueueAsync(companyId, monthEnd, "statement", "en", 1m)).Value;
        (queued.Queued, queued.NoEmail, queued.BelowMinimum).Should().Be((1, 1, 1));
        OutboundMessage message = await db.Set<OutboundMessage>().AsNoTracking().SingleAsync(m => m.BatchId == queued.BatchId);
        (message.Recipient, message.Subject, message.Status).Should().Be(("accounts@alpha.example", "Statement ST-A at 30 April 2026", MessageStatus.Queued));
        message.Body.Should().StartWith("Dear Alpha Traders,\nYour balance is USD 500.00.").And.Contain("ST-ST-A-");

        // The monthly task on 1 May queues April's statements again (a new batch).
        IJobScheduler jobs = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        (await jobs.DefineAsync(new JobDefinition { CompanyId = companyId, Code = "MONTH-STMT", TaskKey = "statements.email", Frequency = JobFrequency.Monthly, DayOfMonth = 1, TimeOfDay = new TimeOnly(6, 0), From = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero) }))
            .IsSuccess.Should().BeTrue();
        JobRunEntry run = (await jobs.RunNowAsync(companyId, "MONTH-STMT", new DateTimeOffset(2026, 5, 1, 6, 0, 0, TimeSpan.Zero))).Value;
        (run.Succeeded, run.Message).Should().Be((true, "1 statements to 2026-04-30 queued; 1 customers without email."));
        (await db.Set<DespatchBatch>().CountAsync(b => b.CompanyId == companyId)).Should().Be(2);
    }
}

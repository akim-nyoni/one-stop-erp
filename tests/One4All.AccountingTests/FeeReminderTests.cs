using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Schools;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Communications;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers bulk fee reminders: the payers of enrolled students who owe are emailed their statements; others are counted.</summary>
[Collection(AccountingCollection.Name)]
public sealed class FeeReminderTests
{
    private readonly AccountingFixture _fixture;

    public FeeReminderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payers_who_owe_fees_are_sent_reminders()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer mother = new(companyId, "FR-M", "Mother", Scenario.Usd) { Email = "mother@example.com" }, uncle = new(companyId, "FR-U", "Uncle", Scenario.Usd),
            outsider = new(companyId, "FR-O", "Outsider", Scenario.Usd) { Email = "outsider@example.com" };
        db.Customers.AddRange(mother, uncle, outsider);
        await db.SaveChangesAsync();
        db.Students.AddRange(new Student(companyId, "FR-1", "First Child", "F1", mother.Id), new Student(companyId, "FR-2", "Second Child", "F2", uncle.Id));
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach (Customer payer in new[] { mother, uncle, outsider })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = payer.Id, DocumentNumber = $"FR-{payer.Code}", DocumentDate = new DateOnly(2026, 1, 10), Currency = Scenario.Usd, NetAmount = 300m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        (await scope.ServiceProvider.GetRequiredService<INotificationService>().SaveTemplateAsync(companyId, "FEES", "Fees", "en", MessageChannel.Email, "Fees for {{Account}}",
            "Dear {{Name}},\nFees of {{Currency}} {{Balance}} are outstanding.\n\n{{Statement}}")).IsSuccess.Should().BeTrue();
        StatementDespatch sent = (await scope.ServiceProvider.GetRequiredService<ISchoolBillingService>().SendFeeRemindersAsync(companyId, new DateOnly(2026, 2, 1), "FEES", "en", 1m)).Value;
        (sent.Queued, sent.NoEmail).Should().Be((1, 1), "the mother is emailed, the uncle has no address, and the outsider pays for no student");
    }
}

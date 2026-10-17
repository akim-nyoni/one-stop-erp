using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Communications;
using One4All.Core.Communications;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// The standard message templates (statement, fee statement, reminder, bill by email and SMS) are seeded for every company, left alone
/// once reworded, render with the fields the bulk senders supply, and a batch queued on one reports its status.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class MessageTemplateSeederTests
{
    private readonly AccountingFixture _fixture;

    public MessageTemplateSeederTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Standard_templates_are_seeded_once_and_render_for_the_bulk_senders()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            MessageTemplateSeeder seeder = scope.ServiceProvider.GetRequiredService<MessageTemplateSeeder>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            int before = await db.Set<MessageTemplate>().CountAsync(t => t.CompanyId == companyId);
            (await seeder.SeedAsync(companyId)).Should().Be(MessageTemplateSeeder.Standard.Count - before);
            (await seeder.SeedAsync(companyId)).Should().Be(0, "a second run adds nothing");
            (await db.Set<MessageTemplate>().CountAsync(t => t.CompanyId == companyId && t.Language == "en")).Should().BeGreaterThanOrEqualTo(MessageTemplateSeeder.Standard.Count);

            // A reworded template is kept.
            INotificationService messages = scope.ServiceProvider.GetRequiredService<INotificationService>();
            (await messages.SaveTemplateAsync(companyId, "REMINDER", "Dunning", "en", MessageChannel.Email, "Friendly reminder for {{Account}}", "Hi {{Name}}, {{Overdue}} is overdue. {{Letter}}")).IsSuccess.Should().BeTrue();
            (await seeder.SeedAsync(companyId)).Should().Be(0);
            (await db.Set<MessageTemplate>().AsNoTracking().SingleAsync(t => t.CompanyId == companyId && t.Code == "REMINDER" && t.Channel == MessageChannel.Email && t.Language == "en")).Subject.Should().StartWith("Friendly reminder");

            // The statement template renders with the fields the statement despatch supplies; a bill SMS has no subject.
            RenderedMessage statement = (await messages.PreviewAsync(companyId, "STATEMENT", "en", MessageChannel.Email, new Dictionary<string, string>
            {
                ["Name"] = "Thandi Moyo", ["Account"] = "C001", ["Balance"] = "1,250.00", ["Currency"] = "USD", ["AsAt"] = "30 June 2026", ["Statement"] = "(lines)",
            })).Value;
            statement.Subject.Should().Be("Statement of account C001 as at 30 June 2026");
            statement.Body.Should().Contain("Dear Thandi Moyo").And.Contain("USD 1,250.00");
            statement.MissingFields.Should().BeEmpty();
            RenderedMessage sms = (await messages.PreviewAsync(companyId, "BILL", "en", MessageChannel.Sms, new Dictionary<string, string> { ["Name"] = "T Moyo", ["BillDate"] = "2026-06", ["Account"] = "R-9" })).Value;
            sms.Subject.Should().BeEmpty();
            sms.MissingFields.Should().Contain("Amount");

            // A batch queued on the FEES template reports its status.
            var batch = await messages.QueueAsync(companyId, "Fee statements June", "FEES", "en", MessageChannel.Email,
                [new DespatchRecipient("parent@example.test", new Dictionary<string, string> { ["Name"] = "Learner One", ["Account"] = "S-1", ["Balance"] = "300.00", ["Currency"] = "USD", ["AsAt"] = "30 June 2026", ["Statement"] = "(lines)" })]);
            batch.IsSuccess.Should().BeTrue(batch.IsFailure ? batch.Error.ToString() : null);
            BatchStatus status = (await messages.BatchStatusAsync(companyId, batch.Value)).Value;
            status.Name.Should().Be("Fee statements June");
            (status.Queued + status.Sent + status.Failed).Should().Be(1);
        }
    }
}

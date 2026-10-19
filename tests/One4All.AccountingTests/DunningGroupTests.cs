using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Communications;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers dunning groups and sending: a government group chased later, an exempt group not at all, letters emailed to those due one (and
/// not again at the same level within the resend interval), and those without email counted for posting.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DunningGroupTests
{
    private readonly AccountingFixture _fixture;

    public DunningGroupTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Groups_set_the_timetable_and_reminders_are_emailed_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer shop = new(companyId, "DG-SHOP", "Corner Shop", Scenario.Usd) { Email = "shop@example.com" };
        Customer ministry = new(companyId, "DG-GOV", "Ministry", Scenario.Usd) { Email = "gov@example.com" };
        Customer church = new(companyId, "DG-CH", "Church", Scenario.Usd);
        Customer kiosk = new(companyId, "DG-K", "Kiosk", Scenario.Usd);
        db.Customers.AddRange(shop, ministry, church, kiosk);
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach (Customer customer in new[] { shop, ministry, church, kiosk })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = $"DG-{customer.Code}", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 500m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        IDunningService dunning = scope.ServiceProvider.GetRequiredService<IDunningService>();
        (await dunning.SaveGroupAsync(companyId, new DunningGroupInput("GOV", "Government", new DunningStages(60, 90, 120)))).IsSuccess.Should().BeTrue();
        (await dunning.SaveGroupAsync(companyId, new DunningGroupInput("NFP", "Not for profit", default, IsExempt: true))).IsSuccess.Should().BeTrue();
        (await dunning.SaveGroupAsync(companyId, new DunningGroupInput("BAD", "Bad", new DunningStages(30, 10, 60)))).Error.Code.Should().Be("Ar.DunningGroup");
        (await dunning.SetCustomerAsync(companyId, "DG-GOV", "GOV", "Government")).IsSuccess.Should().BeTrue();
        (await dunning.SetCustomerAsync(companyId, "DG-CH", "NFP", null)).IsSuccess.Should().BeTrue();
        (await dunning.SetCustomerAsync(companyId, "DG-CH", "NOPE", null)).Error.Code.Should().Be("Ar.DunningGroupNotFound");

        DateOnly asAt = new(2026, 3, 1);
        IReadOnlyList<DunningNotice> due = (await dunning.AssessAsync(companyId, asAt, DunningStages.Default)).Value;
        due.Select(n => n.CustomerName).Should().BeEquivalentTo(["Corner Shop", "Kiosk"], "the ministry is not yet 60 days overdue and the church is exempt");

        (await scope.ServiceProvider.GetRequiredService<INotificationService>().SaveTemplateAsync(companyId, "REMINDER", "Reminder", "en", MessageChannel.Email, "{{Level}}: {{Account}}",
            "Dear {{Name}},\n{{Overdue}} is overdue.\n\n{{Letter}}")).IsSuccess.Should().BeTrue();
        DunningSend first = (await dunning.SendAsync(companyId, asAt, DunningStages.Default, "REMINDER", "en")).Value;
        (first.Queued, first.NoEmail, first.AlreadySent).Should().Be((1, 1, 0), "the shop is emailed, the kiosk has no address");
        DunningSend again = (await dunning.SendAsync(companyId, asAt, DunningStages.Default, "REMINDER", "en")).Value;
        (again.Queued, again.AlreadySent).Should().Be((0, 1), "the shop was sent this level today");
        again.ToPost.Should().Equal("DG-K");

        // The kiosk has a mobile number and there is an SMS reminder: it is texted. A new debtor gets the first level's own wording (REMINDER1).
        INotificationService notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        (await notifications.SaveTemplateAsync(companyId, "REMINDER", "Reminder", "en", MessageChannel.Sms, string.Empty, "{{Name}}: {{Overdue}} overdue on {{Account}}.")).IsSuccess.Should().BeTrue();
        (await notifications.SaveTemplateAsync(companyId, "REMINDER1", "Reminder", "en", MessageChannel.Email, "FIRST {{Account}}", "Dear {{Name}},\n{{Letter}}")).IsSuccess.Should().BeTrue();
        (await db.Customers.SingleAsync(c => c.Id == kiosk.Id)).MobileNumber = "+263771000111";
        Customer cafe = new(companyId, "DG-CAFE", "Cafe", Scenario.Usd) { Email = "cafe@example.com" };
        db.Customers.Add(cafe);
        await db.SaveChangesAsync();
        (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = cafe.Id, DocumentNumber = "DG-CAFE", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 80m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();
        DunningSend third = (await dunning.SendAsync(companyId, asAt, DunningStages.Default, "REMINDER", "en")).Value;
        (third.Queued, third.BySms, third.NoEmail).Should().Be((2, 1, 0));
        List<OutboundMessage> messages = await db.Set<OutboundMessage>().AsNoTracking().Where(m => m.CompanyId == companyId && (m.Recipient == "+263771000111" || m.Recipient == "cafe@example.com")).ToListAsync();
        messages.Should().Contain(m => m.Recipient == "+263771000111" && m.Channel == MessageChannel.Sms && m.Body.Contains("overdue on DG-K"));
        messages.Should().Contain(m => m.Recipient == "cafe@example.com" && m.Subject == "FIRST DG-CAFE");
    }
}

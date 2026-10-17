using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Communications;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// A posted billing run's bills go out in bulk: an SMS to each ratepayer with a mobile number, and one printed page per ratepayer - all of them, or only
/// those without an email address.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BulkBillsTests
{
    private readonly AccountingFixture _fixture;

    public BulkBillsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_posted_run_is_texted_and_printed_in_bulk()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff refuse = new(companyId, "REF-BB", "Refuse Removal", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 25m };
        db.Tariffs.Add(refuse);
        Customer both = new(companyId, "BB-1", "Both Ways", Scenario.Usd) { MobileNumber = "+263771000001", Email = "both@example.com" };
        Customer phone = new(companyId, "BB-2", "Phone Only", Scenario.Usd) { MobileNumber = "+263771000002" };
        Customer neither = new(companyId, "BB-3", "Post Only", Scenario.Usd);
        db.Customers.AddRange(both, phone, neither);
        await db.SaveChangesAsync();
        foreach ((Customer owner, string stand) in new[] { (both, "B03"), (phone, "B01"), (neither, "B02") })
        {
            Property property = new(companyId, stand, "Ward 12", owner.Id);
            property.AddService(ServiceType.Refuse, refuse.Id, new DateOnly(2026, 1, 1));
            db.Properties.Add(property);
        }

        await db.SaveChangesAsync();
        IBillingRunService runs = scope.ServiceProvider.GetRequiredService<IBillingRunService>();
        BillingRunSummary proposed = (await runs.ProposeAsync(new BillingRunProposal
        {
            CompanyId = companyId, ServiceType = ServiceType.Refuse, BillDate = new DateOnly(2026, 6, 30), Ward = "Ward 12", InvoicePrefix = "BB", UserId = 81,
        })).Value;

        (await runs.SmsBillsAsync(companyId, proposed.RunNumber, "BILLSMS", "en")).Error.Code.Should().Be("Mun.RunNotPosted");
        (await runs.ApproveAsync(companyId, proposed.RunNumber, 82)).Value.Posted.Should().Be(3);

        (await scope.ServiceProvider.GetRequiredService<INotificationService>().SaveTemplateAsync(companyId, "BILLSMS", "Bill", "en", MessageChannel.Sms, "Bill",
            "{{Name}}: stand {{Stands}} billed {{Amount}}, you owe {{Balance}} {{Currency}}.")).IsSuccess.Should().BeTrue();
        BillSmsDespatch texted = (await runs.SmsBillsAsync(companyId, proposed.RunNumber, "BILLSMS", "en")).Value;
        (texted.Queued, texted.NoMobile).Should().Be((2, 1));
        texted.BatchId.Should().NotBeNull();

        PrintedBills all = (await runs.PrintBillsAsync(companyId, proposed.RunNumber, withoutEmailOnly: false)).Value;
        all.Bills.Should().Be(3);
        string[] pages = all.Text.Split('\f');
        pages.Should().HaveCount(3);
        pages[0].Should().Contain("stand(s) B01").And.Contain("Phone Only"); // stand order
        pages[2].Should().Contain("stand(s) B03");

        PrintedBills notEmailed = (await runs.PrintBillsAsync(companyId, proposed.RunNumber, withoutEmailOnly: true)).Value;
        notEmailed.Bills.Should().Be(2);
        notEmailed.Text.Should().NotContain("Both Ways");
    }
}

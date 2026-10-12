using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Municipal;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using ReceiptRequest = One4All.Application.Abstractions.Trading.ReceiptRequest;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the enquiry desk: a stand with a registered owner, metered water read at 120 and a refuse service ended in
/// March; 100 billed in February and 50 in late March with 30 received leave a balance of 120, of which 70 is overdue;
/// the latest transactions come newest first; the indigent subsidy, payment arrangement, open disconnection order and
/// a hawker permit all show; there is no valid clearance.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PropertyEnquiryTests
{
    private readonly AccountingFixture _fixture;

    public PropertyEnquiryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_enquiry_desk_shows_the_whole_property()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff water = new(companyId, "WAT-ENQ", "Water", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { PerUnitRate = 1m };
        Tariff refuse = new(companyId, "REF-ENQ", "Refuse", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 20m };
        db.Tariffs.AddRange(water, refuse);
        Customer owner = new(companyId, "EQ-OWN", "Enquiry Owner", Scenario.Usd) { PaymentTermsDays = 30, Email = $"eq{companyId}@example.com" };
        db.Customers.Add(owner);
        await db.SaveChangesAsync();

        IMeterReadingService meters = scope.ServiceProvider.GetRequiredService<IMeterReadingService>();
        string serial = $"EQ-{companyId}";
        long meterId = (await meters.InstallAsync(companyId, serial, 5, 1m, 100m, new DateOnly(2026, 1, 1))).Value;
        Property stand = new(companyId, "E1", "Ward 12", owner.Id) { Valuation = 80_000m };
        stand.AddService(ServiceType.Water, water.Id, new DateOnly(2026, 1, 1)).MeterId = meterId;
        PropertyServiceLink ended = stand.AddService(ServiceType.Refuse, refuse.Id, new DateOnly(2026, 1, 1));
        ended.EndDate = new DateOnly(2026, 3, 15);
        ended.IsActive = false;
        db.Properties.Add(stand);
        await db.SaveChangesAsync();
        (await meters.CaptureAsync(companyId, meterId, new DateOnly(2026, 3, 1), 120m, false)).IsSuccess.Should().BeTrue();

        (await scope.ServiceProvider.GetRequiredService<IOwnershipService>().RegisterOwnersAsync(companyId, stand.Id, [new OwnerShare(owner.Id, 100m, true)], new DateOnly(2019, 6, 1), null)).IsSuccess.Should().BeTrue();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach ((string number, DateOnly date, decimal amount) in new[] { ("EQ-FEB", new DateOnly(2026, 2, 1), 100m), ("EQ-MAR", new DateOnly(2026, 3, 25), 50m) })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = owner.Id, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Usd, NetAmount = amount, SalesAccountCode = "4000" })).IsSuccess.Should().BeTrue();
        }

        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = owner.Id, DocumentNumber = "EQ-R1", ReceiptDate = new DateOnly(2026, 3, 28), Currency = Scenario.Usd, Amount = 30m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        db.Set<IndigentRegistration>().Add(new IndigentRegistration(companyId, stand.Id, 40m, 50m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 71));
        db.Set<ServiceOrder>().Add(new ServiceOrder(companyId, $"DO-EQ-{companyId}", stand.Id, ServiceType.Water, ServiceOrderKind.Disconnect, new DateOnly(2026, 3, 30), 70m));
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IReceiptingService>().CaptureArrangementAsync(companyId, owner.Id, 70m, 35m, new DateOnly(2026, 4, 10))).IsSuccess.Should().BeTrue();
        string permit = (await scope.ServiceProvider.GetRequiredService<ICouncilLicensingService>().IssueAsync(new IssueLicenceRequest
        {
            CompanyId = companyId, Kind = CouncilLicenceKind.Hawker, HolderCustomerId = owner.Id, Description = "Fruit stall", IssuedOn = new DateOnly(2026, 1, 1), CycleMonths = 12, Fee = 0m,
        })).Value.LicenceNumber;

        TicketCategory leak = new(companyId, $"LEAK-{companyId}", "Water leak", ServiceType.Water, 4m, 48m);
        db.Set<TicketCategory>().Add(leak);
        await db.SaveChangesAsync();
        DateTimeOffset reported = new(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);
        db.Set<ServiceTicket>().Add(new ServiceTicket(companyId, $"TK-{companyId}", leak.Id, stand.Id, TicketPriority.High, "Enquiry Owner", "0771", "Burst pipe at the gate", reported,
            reported.AddHours(4), reported.AddHours(48)));
        db.Set<One4All.Core.Communications.OutboundMessage>().Add(new One4All.Core.Communications.OutboundMessage(companyId, null, One4All.Core.Communications.MessageChannel.Email,
            owner.Email!, "Your March statement", "Attached.", new DateTimeOffset(2026, 3, 26, 8, 0, 0, TimeSpan.Zero)));
        db.Set<One4All.Core.Communications.InboundMessage>().Add(new One4All.Core.Communications.InboundMessage(companyId, One4All.Core.Communications.MessageChannel.Email, owner.Email!,
            "Enquiry Owner", "Please read my meter again", new DateTimeOffset(2026, 3, 27, 8, 0, 0, TimeSpan.Zero), $"in-{companyId}"));
        await db.SaveChangesAsync();

        PropertyEnquiry view = (await scope.ServiceProvider.GetRequiredService<IPropertyEnquiryService>().EnquireAsync(companyId, stand.Id, new DateOnly(2026, 4, 1), 2)).Value;
        view.Tickets.Should().ContainSingle().Which.Should().Match<EnquiryTicket>(t => t.Category == "Water leak" && t.Description == "Burst pipe at the gate");
        view.Readings.Should().NotBeEmpty();
        view.Readings[0].Should().Match<EnquiryReading>(r => r.MeterSerial == serial && r.Date == new DateOnly(2026, 3, 1) && r.Reading == 120m);
        view.Correspondence.Select(m => (m.Outgoing, m.Subject)).Should().Equal((false, "Please read my meter again"), (true, "Your March statement"));
        view.Should().Match<PropertyEnquiry>(v => v.StandNumber == "E1" && v.Ward == "Ward 12" && v.Valuation == 80_000m && v.RatepayerCode == "EQ-OWN" && v.RatepayerName == "Enquiry Owner");
        view.Owners.Select(o => o.CustomerCode).Should().Equal("EQ-OWN");
        view.Services.Should().Equal(
            new EnquiryService(ServiceType.Water, "WAT-ENQ", true, new DateOnly(2026, 1, 1), null, serial, 120m, new DateOnly(2026, 3, 1)),
            new EnquiryService(ServiceType.Refuse, "REF-ENQ", false, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 15), null, null, null));
        view.Balance.Should().Be(120m);
        view.Overdue.Should().Be(70m);
        view.RecentTransactions.Select(t => (t.DocumentNumber, t.Amount)).Should().Equal(("EQ-R1", -30m), ("EQ-MAR", 50m));
        view.IndigentSubsidyPercent.Should().Be(50m);
        view.Arrangement.Should().Be(new EnquiryArrangement(70m, 35m, new DateOnly(2026, 4, 10)));
        view.OpenOrders.Select(o => o.Kind).Should().Equal(ServiceOrderKind.Disconnect);
        view.Licences.Should().Equal(new EnquiryLicence(permit, CouncilLicenceKind.Hawker, new DateOnly(2026, 12, 31), CouncilLicenceStatus.Active));
        view.ClearanceValidUntil.Should().BeNull();
        (await scope.ServiceProvider.GetRequiredService<IPropertyEnquiryService>().EnquireAsync(companyId, -1, new DateOnly(2026, 4, 1))).Error.Code.Should().Be("Mun.PropertyNotFound");
    }
}

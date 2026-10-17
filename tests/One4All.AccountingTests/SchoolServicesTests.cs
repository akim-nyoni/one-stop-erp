using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Accounting;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;
using ISalesLedgerService = One4All.Application.Abstractions.Trading.ISalesLedgerService;
using ReceiptRequest = One4All.Application.Abstractions.Trading.ReceiptRequest;

namespace One4All.AccountingTests;

/// <summary>
/// Covers school sponsors, transport and boarding (brief §14 J 179, 181, 183, 184): a sponsored student's fees go to the sponsor's
/// account; a boarder who rides the bus gets separate transport and boarding invoices; the deferred fee schedule, sponsor statement,
/// route utilisation and hostel occupancy read them back.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SchoolServicesTests
{
    private readonly AccountingFixture _fixture;

    public SchoolServicesTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Sponsors_transport_and_boarding_are_billed_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Accounts.AddRange(new LedgerAccount(companyId, "2490", "Fees Received in Advance", AccountType.Liability), new LedgerAccount(companyId, "4211", "Transport Income", AccountType.Income),
            new LedgerAccount(companyId, "4212", "Boarding Income", AccountType.Income));
        Customer parent = new(companyId, "SS-G1", "Parent One", Scenario.Usd), other = new(companyId, "SS-G2", "Parent Two", Scenario.Usd), trust = new(companyId, "SS-TR", "Trust Fund", Scenario.Usd);
        db.Customers.AddRange(parent, other, trust);
        FeeStructure structure = new(companyId, "SS-G1T1", "Grade 1 Term 1", "G1", "Term 1", "4200");
        structure.AddItem("Tuition", 1_000m, true);
        structure.DeferOverTerm(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), "2490");
        db.FeeStructures.Add(structure);
        TransportRoute route = new(companyId, "r1", "Route One", 20, 150m, "4211");
        Hostel hostel = new(companyId, "h1", "North House", 10, 400m, "4212");
        db.AddRange(route, hostel);
        await db.SaveChangesAsync();
        Student one = new(companyId, "SS-001", "S One", "G1", parent.Id) { Stream = "A", TransportRouteId = route.Id, IsBoarder = true, HostelId = hostel.Id };
        Student two = new(companyId, "SS-002", "S Two", "G1", other.Id) { SponsorCustomerId = trust.Id };
        db.Students.AddRange(one, two);
        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<ISchoolBillingService>().BillTermAsync(companyId, structure.Id, "SS-T1-", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = trust.Id, DocumentNumber = "SS-RC-1", ReceiptDate = new DateOnly(2026, 1, 20), Currency = Scenario.Usd, Amount = 600m, BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        List<string> parentInvoices = await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == parent.Id).Select(i => i.DocumentNumber).ToListAsync();
        parentInvoices.Should().BeEquivalentTo(["SS-T1-SS-001", "SS-T1-SS-001-TR", "SS-T1-SS-001-HS"]);
        (await db.OpenItems.AsNoTracking().CountAsync(i => i.CompanyId == companyId && i.TraderId == other.Id)).Should().Be(0); // the sponsor pays

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 1, 31), ["AsAtDate"] = new DateOnly(2026, 3, 31) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("SC179")).Should().Contain("Trust Fund,S Two (SS-002),\"1,000.00\",600.00,400");
        (await Render("SC181")).Should().Contain("SS-T1-SS-001,Term 1 fees - S One,2026-01-01 to 2026-03-31,\"1,000.00\",0.00,1000").And.Contain("SS-T1-SS-002,");
        (await Render("SC183")).Should().Contain("R1,Route One,20,1,5.0%,150.00,150");
        (await Render("SC184")).Should().Contain("H1,North House,10,1,10.0%,400.00,400");
        (await Render("SC176")).Should().Contain("G1 A,S One (SS-001),Parent One,3,");
    }
}

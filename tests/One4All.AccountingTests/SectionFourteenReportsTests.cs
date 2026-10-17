using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Thirteenth audit #18 (brief §14): the reports the catalogue lacked - GL3 nominal activity, AR43-ECL provision matrix, AP57 payment run proposal and
/// SYS237-U permission matrix by user - render from the ledgers.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SectionFourteenReportsTests
{
    private readonly AccountingFixture _fixture;

    public SectionFourteenReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Nominal_activity_ecl_matrix_payment_proposal_and_user_permissions_render()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer customer = new(companyId, "S14-C", "Slow Payer", Scenario.Usd) { PaymentTermsDays = 30 };
        Supplier supplier = new(companyId, "S14-S", "Paper Mill", Scenario.Usd);
        db.AddRange(customer, supplier);
        string tag = Guid.NewGuid().ToString("N")[..8];
        Role clerk = new($"S14 clerk {tag}");
        clerk.Grant("Purchasing.PaymentRun", OperationFlags.View | OperationFlags.Create);
        db.Roles.Add(clerk);
        User user = new($"s14.{tag}", "Section Fourteen");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        user.AssignRole(clerk.Id);
        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "S14-I1", DocumentDate = new DateOnly(2026, 1, 10), Currency = Scenario.Usd, NetAmount = 800m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>().PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "S14-P1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 400m, ExpenseAccountCode = "6000",
        })).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, Dictionary<string, object?> parameters)
        {
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string gl3 = await Render("GL3", new() { ["AccountCode"] = "4000", ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 6, 30) });
        gl3.Should().Contain("Opening balance 4000").And.Contain("Closing balance").And.Contain("800.00");
        (await Render("GL3", new() { ["AccountCode"] = "NOPE" })).Should().NotContain("Opening balance", "an unknown account gives no rows");

        // Due 9 February; at 30 June it is over 90 days past due: 50% of 800.
        string ecl = await Render("AR43-ECL", new() { ["AsAtDate"] = new DateOnly(2026, 6, 30) });
        ecl.Should().Contain("Over 90 days,91,50,800,400").And.Contain("Total provision required").And.Contain("Adjustment to post,,,,400");

        string ap57 = await Render("AP57", new() { ["PaymentDate"] = new DateOnly(2026, 6, 30), ["DueOnOrBefore"] = new DateOnly(2026, 6, 30) });
        ap57.Should().Contain("S14-S,Paper Mill,USD,5,1,400,0,0,Pay,0,400");

        string sys = await Render("SYS237-U", new());
        sys.Should().Contain($"s14.{tag},Section Fourteen,").And.Contain("Purchasing.PaymentRun");
    }
}

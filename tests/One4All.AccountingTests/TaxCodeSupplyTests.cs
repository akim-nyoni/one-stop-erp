using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #23: a tax code's goods / services scope is enforced on order and quote lines, and its jurisdiction and return box drive TX226.</summary>
[Collection(AccountingCollection.Name)]
public sealed class TaxCodeSupplyTests
{
    private readonly AccountingFixture _fixture;

    public TaxCodeSupplyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Goods_and_services_codes_are_enforced_and_the_return_groups_by_box()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        IMasterDataService master = scope.ServiceProvider.GetRequiredService<IMasterDataService>();
        DateOnly from = new(2026, 1, 1);
        (await master.CreateTaxCodeAsync(new NewTaxCode(companyId, "GDS", "Goods 15%", TaxType.Standard, 15m, from, "2300", "2300", TaxAppliesTo.Goods, "zw", "B1"))).IsSuccess.Should().BeTrue();
        (await master.CreateTaxCodeAsync(new NewTaxCode(companyId, "SVC", "Services 15%", TaxType.Standard, 15m, from, "2300", "2300", TaxAppliesTo.Services, "ZW", "B2"))).IsSuccess.Should().BeTrue();
        TaxCode saved = db.TaxCodes.Single(t => t.CompanyId == companyId && t.Code == "GDS");
        (saved.AppliesTo, saved.Jurisdiction, saved.ReturnBox).Should().Be((TaxAppliesTo.Goods, "ZW", "B1"));

        Customer customer = new(companyId, "TCS-C", "Kwekwe Supplies", Scenario.Usd);
        Item cement = new(companyId, "TCS-CEM", "Cement"), install = new(companyId, "TCS-INS", "Installation", ItemType.Service);
        db.AddRange(customer, cement, install);
        await db.SaveChangesAsync();

        IQuotationService quotes = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        var wrong = await quotes.CreateAsync(companyId, customer.Id, "TCS-Q1", from, from.AddDays(30), Scenario.Usd,
            [new QuotationLineRequest(cement.Id, "Cement", 10m, 9m, "GDS", "4000"), new QuotationLineRequest(install.Id, "Installation", 1m, 50m, "GDS", "4000")]);
        wrong.Error.Code.Should().Be("Tax.WrongSupply");
        wrong.Error.Message.Should().Contain("TCS-INS");
        (await quotes.CreateAsync(companyId, customer.Id, "TCS-Q2", from, from.AddDays(30), Scenario.Usd,
            [new QuotationLineRequest(cement.Id, "Cement", 10m, 9m, "GDS", "4000"), new QuotationLineRequest(install.Id, "Installation", 1m, 50m, "SVC", "4000")])).IsSuccess.Should().BeTrue();

        // TX226: goods and services sales each in their box, with the jurisdiction's total.
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach ((string number, string code, decimal net) in new[] { ("TCS-I1", "GDS", 1_000m), ("TCS-I2", "SVC", 200m) })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 5), Currency = Scenario.Usd,
                NetAmount = net, SalesAccountCode = "4000", TaxCode = code, TaxAmount = net * 0.15m, TaxAccountCode = "2300" })).IsSuccess.Should().BeTrue();
        }

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "TX226",
            new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        string csv = Encoding.UTF8.GetString(rendered.Value.Content);
        csv.Should().Contain("ZW,B1,GDS,Goods 15%,Goods,1000").And.Contain("ZW,B2,SVC,Services 15%,Services,200").And.Contain("ZW,Total,,Payable (refundable when negative),,1200");
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers dedicated receivables control accounts: a customer's own control, the tenure mapping, and the reconciliation per control.</summary>
[Collection(AccountingCollection.Name)]
public sealed class TenureControlAccountTests
{
    private readonly AccountingFixture _fixture;

    public TenureControlAccountTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Instalment_debtors_post_to_their_own_control()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        LedgerAccount instalmentControl = new(companyId, "1270", "Instalment Debtors Control", AccountType.Asset);
        instalmentControl.MakeControlAccount(ControlAccountType.AccountsReceivable);
        db.Accounts.Add(instalmentControl);
        Customer buyer = new(companyId, "TC-BUY", "Stand Buyer", Scenario.Usd), trader = new(companyId, "TC-TRD", "Ordinary Customer", Scenario.Usd);
        db.Customers.AddRange(buyer, trader);
        await db.SaveChangesAsync();

        IReceivableControlService controls = scope.ServiceProvider.GetRequiredService<IReceivableControlService>();
        (await controls.SetTenureControlAsync(companyId, InstalmentTenure.InstalmentSale, "4000")).Error.Code.Should().Be("Ar.NotReceivableControl");
        (await controls.SetTenureControlAsync(companyId, InstalmentTenure.InstalmentSale, "1270")).IsSuccess.Should().BeTrue();
        (await controls.SetCustomerControlAsync(companyId, "TC-BUY", "1270")).IsSuccess.Should().BeTrue();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        SalesInvoiceRequest Invoice(Customer c, string number, decimal net) => new()
        {
            CompanyId = companyId, CustomerId = c.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = net, SalesAccountCode = "4000",
        };
        (await ar.PostInvoiceAsync(Invoice(buyer, "TC-1", 700m))).IsSuccess.Should().BeTrue();
        (await ar.PostInvoiceAsync(Invoice(trader, "TC-2", 300m))).IsSuccess.Should().BeTrue();
        (await controls.SetCustomerControlAsync(companyId, "TC-BUY", null)).Error.Code.Should().Be("Ar.ControlHasItems");

        ControlReconciliationReport reconciliation = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().ControlReconciliationAsync(companyId, new DateOnly(2026, 3, 31));
        ControlReconciliationRow instalments = reconciliation.Rows.Single(r => r.AccountCode == "1270");
        ControlReconciliationRow main = reconciliation.Rows.Single(r => r.AccountCode == "1200");
        (instalments.GlBalance, instalments.SubLedgerBalance, instalments.Difference).Should().Be((700m, 700m, 0m));
        (main.GlBalance, main.SubLedgerBalance, main.Difference).Should().Be((300m, 300m, 0m));
        (await controls.TenureControlsAsync(companyId)).Should().Equal((InstalmentTenure.InstalmentSale, "1270"));
    }
}

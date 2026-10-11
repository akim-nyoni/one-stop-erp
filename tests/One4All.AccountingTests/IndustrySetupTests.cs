using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.MasterData;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the Industry Setup Wizard: a retail and construction company gets the core and industry charts, VAT at the chosen rate,
/// withholding tax, warehouses, an open year, starter roles and demo data, posts at once, and later adds schooling without duplicates.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class IndustrySetupTests
{
    private readonly AccountingFixture _fixture;

    public IndustrySetupTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_company_is_set_up_for_its_industries_and_can_add_more()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await Scenario.SeedAsync(db); // currencies
        IIndustrySetupService wizard = scope.ServiceProvider.GetRequiredService<IIndustrySetupService>();

        IndustrySetupResult setup = (await wizard.SetUpCompanyAsync(new IndustrySetupRequest
        {
            Code = "IW" + Guid.NewGuid().ToString("N")[..8], Name = "Builders & Shop", BaseCurrency = Scenario.Usd, Verticals = [Vertical.Construction, Vertical.Retail],
            FiscalYearStart = new DateOnly(2026, 1, 1), VatRatePercent = 14.5m, WithDemoData = true,
        })).Value;
        long companyId = setup.CompanyId;
        int expectedAccounts = IndustryTemplates.Plan([Vertical.Retail, Vertical.Construction]).Sum(t => t.Accounts.Count);
        setup.Accounts.Should().Be(expectedAccounts);
        setup.TaxCodes.Should().Be(4);
        setup.Warehouses.Should().Be(4);
        setup.DemoRecords.Should().Be(IndustryTemplates.Plan([Vertical.Retail, Vertical.Construction]).Sum(t => t.DemoCustomers.Count + t.DemoSuppliers.Count + t.DemoItems.Count));
        db.Items.Single(i => i.CompanyId == companyId && i.Code == "CEMENT").Category.Should().Be("Materials");
        db.TaxCodes.AsNoTracking().Include(t => t.Rates).Single(t => t.CompanyId == companyId && t.Code == "VAT").Rates.Single().RatePercent.Should().Be(14.5m);
        db.Accounts.Single(a => a.CompanyId == companyId && a.AccountCode == "2350").ControlAccountType.Should().Be(ControlAccountType.Retention);
        db.Items.Single(i => i.CompanyId == companyId && i.Code == "MILK").Category.Should().Be("Dairy");
        db.Roles.AsNoTracking().Include(r => r.Permissions).Single(r => r.NormalizedName == "RETAIL CASHIER").Permissions.Should().Contain(p => p.PermissionCode == "Pos.Sale");

        // Item categories, a till at head office and the industries' report set.
        setup.ItemCategories.Should().Be(IndustryExtras.For(Vertical.Core).ItemCategories.Count + IndustryExtras.For(Vertical.Retail).ItemCategories.Count
            + IndustryExtras.For(Vertical.Construction).ItemCategories.Count);
        db.ItemCategories.AsNoTracking().Where(c => c.CompanyId == companyId).Select(c => c.Code).Should().Contain(["GROC", "PLANT", "GEN"]);
        setup.Terminals.Should().Be(1);
        db.Set<One4All.Core.Pos.PosTerminal>().AsNoTracking().Single(t => t.CompanyId == companyId).Code.Should().Be("TILL01");
        db.FeaturedReports.AsNoTracking().Where(f => f.CompanyId == companyId).Select(f => f.ReportCode).Should().Contain(["GL-TB", "POS-SUM", "JC186"]);


        // It posts straight away.
        (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 5, 4), TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false,
            Lines = [new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 80m }, new JournalLineRequest { AccountCode = "4400", Side = JournalSide.Credit, TransactionAmount = 80m }],
        })).IsSuccess.Should().BeTrue();

        // Adding schooling later adds only what is new; adding it again adds nothing.
        IndustrySetupResult school = (await wizard.AddVerticalAsync(companyId, Vertical.School)).Value;
        (school.Accounts, school.TaxCodes, school.Warehouses).Should().Be((IndustryTemplates.For(Vertical.School).Accounts.Count, 0, 1));
        IndustrySetupResult again = (await wizard.AddVerticalAsync(companyId, Vertical.School)).Value;
        (again.Accounts, again.TaxCodes, again.Warehouses, again.Roles, again.ItemCategories, again.Terminals, again.FeaturedReports).Should().Be((0, 0, 0, 0, 0, 0, 0));
        school.Terminals.Should().Be(1, "the tuck shop till");
    }
}

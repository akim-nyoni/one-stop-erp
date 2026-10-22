using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the configurable qualification rules at registration: a category income ceiling, and a rates clearance refused while the applicant
/// owes an overdue charge and granted once it is paid off. The rules round-trip.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class QualificationRulesDbTests
{
    private readonly AccountingFixture _fixture;

    public QualificationRulesDbTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Registration_applies_the_configured_rules()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string id = $"63-{companyId % 1000000:000000}Q42";
        Customer ratepayer = new(companyId, "QR-RATE", "Rate Payer", Scenario.Usd) { NationalIdNumber = id };
        db.Customers.Add(ratepayer);
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = ratepayer.Id, DocumentNumber = "QR-RATES-1", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 40m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();

        IWaitingListService list = scope.ServiceProvider.GetRequiredService<IWaitingListService>();
        (await list.SetQualificationRulesAsync(companyId, new QualificationRules(false, true, true, new Dictionary<HousingCategory, decimal> { [HousingCategory.HighDensity] = 800m })))
            .IsSuccess.Should().BeTrue();
        QualificationRules saved = await list.QualificationRulesAsync(companyId);
        (saved.FirstTimeBuyersOnly, saved.RefuseForfeited, saved.RequireRatesClearance).Should().Be((false, true, true));
        saved.CategoryIncomeCeilings.Should().Equal(new Dictionary<HousingCategory, decimal> { [HousingCategory.HighDensity] = 800m });

        WaitingListApplication Application(decimal income) => new()
        {
            CompanyId = companyId, IdNumber = id, FullName = "Rate Payer", Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1985, 1, 1), ResidentSince = new DateOnly(2000, 1, 1),
            MonthlyIncome = income, Tenure = CurrentTenure.Lodger, RegistrationDate = new DateOnly(2026, 6, 1),
        };
        var refused = await TestClock.RegisterCapturedAsync(scope.ServiceProvider, Application(900m));
        refused.Error.Code.Should().Be("Hse.NotQualified");
        refused.Error.Message.Should().Contain("limit for HighDensity").And.Contain("Owes 40.00");

        (await ar.PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = ratepayer.Id, DocumentNumber = "QR-RCPT-1", ReceiptDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, Amount = 40m, BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        (await TestClock.RegisterCapturedAsync(scope.ServiceProvider, Application(700m))).IsSuccess.Should().BeTrue("the charge is paid and the income is under the ceiling");
    }
}

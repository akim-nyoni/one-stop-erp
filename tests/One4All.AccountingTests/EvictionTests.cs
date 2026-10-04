using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the eviction workflow: a tenant owing March and April rent gets a case opened with a reminder (a paid-up
/// tenancy and a second case are refused); demand and notice follow; legal handover waits out the 30-day notice; the
/// court order needs its reference; eviction on 1 August ends the tenancy — rent billed to that day, the deposit applied
/// — and returns the unit as void. A second tenant's case is withdrawn once the arrears are paid.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class EvictionTests
{
    private readonly AccountingFixture _fixture;

    public EvictionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Evictions_follow_the_stages_and_end_the_tenancy()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4975", "Rental Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "2260", "Rental Deposits Held", AccountType.Liability));
        Customer defaulter = new(companyId, "EV-DEF", "Defaulting Tenant", Scenario.Usd) { PaymentTermsDays = 7 };
        Customer payer = new(companyId, "EV-PAY", "Late Payer", Scenario.Usd) { PaymentTermsDays = 7 };
        db.Customers.AddRange(defaulter, payer);
        await db.SaveChangesAsync();

        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        foreach (string code in new[] { "EV-1", "EV-2" })
        {
            (await rentals.AddUnitAsync(companyId, code, $"Unit {code}", RentalUnitType.Flat, 2, 300m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        }

        LetUnitRequest Let(string unit, Customer tenant) => new()
        {
            CompanyId = companyId, UnitCode = unit, TenantCustomerId = tenant.Id, StartDate = new DateOnly(2026, 3, 1), Deposit = 300m, BankAccountCode = "1100", DepositAccountCode = "2260",
        };

        string defaulting = (await rentals.LetAsync(Let("EV-1", defaulter))).Value;
        string paying = (await rentals.LetAsync(Let("EV-2", payer))).Value;

        IEvictionService evictions = scope.ServiceProvider.GetRequiredService<IEvictionService>();
        (await evictions.OpenAsync(companyId, defaulting, new DateOnly(2026, 2, 20), 30, 71)).Error.Code.Should().Be("Hse.NoArrears");
        foreach (int month in new[] { 3, 4 })
        {
            (await rentals.RunRentAsync(companyId, new DateOnly(2026, month, 1))).IsSuccess.Should().BeTrue();
        }

        string evictionCase = (await evictions.OpenAsync(companyId, defaulting, new DateOnly(2026, 5, 10), 30, 71)).Value;
        (await evictions.OpenAsync(companyId, defaulting, new DateOnly(2026, 5, 11), 30, 71)).Error.Code.Should().Be("Hse.EvictionOpen");

        Task<Core.Common.Result> Advance(EvictionStage stage, DateOnly date, string? reference = null)
            => evictions.AdvanceAsync(companyId, evictionCase, stage, date, reference, "1100", 71);

        (await Advance(EvictionStage.NoticeToVacate, new DateOnly(2026, 5, 17))).Error.Code.Should().Be("Hse.EvictionStage");
        (await Advance(EvictionStage.Demand, new DateOnly(2026, 5, 17))).IsSuccess.Should().BeTrue();
        (await Advance(EvictionStage.NoticeToVacate, new DateOnly(2026, 5, 24), "NTV-14")).IsSuccess.Should().BeTrue();
        (await Advance(EvictionStage.LegalHandover, new DateOnly(2026, 6, 10))).Error.Message.Should().Contain("2026-06-23");
        (await Advance(EvictionStage.LegalHandover, new DateOnly(2026, 6, 23), "Moyo & Partners")).IsSuccess.Should().BeTrue();
        (await Advance(EvictionStage.CourtOrder, new DateOnly(2026, 7, 15))).Error.Message.Should().Contain("reference");
        (await Advance(EvictionStage.CourtOrder, new DateOnly(2026, 7, 15), "CO-2026-77")).IsSuccess.Should().BeTrue();
        (await Advance(EvictionStage.Evicted, new DateOnly(2026, 8, 1))).IsSuccess.Should().BeTrue();

        Tenancy ended = await db.Set<Tenancy>().AsNoTracking().SingleAsync(t => t.CompanyId == companyId && t.TenancyNumber == defaulting);
        ended.Status.Should().Be(TenancyStatus.Ended);
        ended.EndDate.Should().Be(new DateOnly(2026, 8, 1));
        (await rentals.VoidsAsync(companyId, new DateOnly(2026, 8, 2))).Single(v => v.UnitCode == "EV-1").VacantSince.Should().Be(new DateOnly(2026, 8, 2));
        (await Advance(EvictionStage.Evicted, new DateOnly(2026, 8, 2))).Error.Code.Should().Be("Hse.EvictionClosed");

        // The other tenant pays up and the case is withdrawn.
        string withdrawn = (await evictions.OpenAsync(companyId, paying, new DateOnly(2026, 5, 10), 30, 71)).Value;
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = payer.Id, DocumentNumber = "EV-PAID", ReceiptDate = new DateOnly(2026, 5, 12), Currency = Scenario.Usd, Amount = 600m, BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        (await evictions.WithdrawAsync(companyId, withdrawn, " ")).Error.Code.Should().Be("Hse.WithdrawReason");
        (await evictions.WithdrawAsync(companyId, withdrawn, "Arrears paid in full")).IsSuccess.Should().BeTrue();

        (await evictions.RegisterAsync(companyId, openOnly: false)).Select(c => (c.UnitCode, c.Stage, c.Status)).Should().Equal(
            ("EV-1", EvictionStage.Evicted, EvictionCaseStatus.Evicted),
            ("EV-2", EvictionStage.Reminder, EvictionCaseStatus.Withdrawn));
        (await evictions.RegisterAsync(companyId, openOnly: true)).Should().BeEmpty();
    }
}

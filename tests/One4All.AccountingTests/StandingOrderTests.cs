using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Banking;
using One4All.Core.Trading;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers standing orders (GL and supplier, month-end dates, never run twice), direct debits collecting what is owed up to a cap, and
/// gift card stock ranges activated at sale.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StandingOrderTests
{
    private readonly AccountingFixture _fixture;

    public StandingOrderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Standing_orders_pay_and_direct_debits_collect_on_schedule()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier landlord = new(companyId, "SO-LAND", "Landlord", Scenario.Usd);
        Customer member = new(companyId, "SO-MEM", "Member", Scenario.Usd);
        db.AddRange(landlord, member);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = member.Id, DocumentNumber = "SO-INV1", DocumentDate = new DateOnly(2026, 1, 10), Currency = Scenario.Usd, NetAmount = 500m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();

        IStandingOrderService service = scope.ServiceProvider.GetRequiredService<IStandingOrderService>();
        (await service.CreateStandingOrderAsync(new StandingOrderRequest
        {
            CompanyId = companyId, Reference = "SO-X", BankAccountCode = "1100", Amount = 10m, StartDate = new DateOnly(2026, 1, 1), SupplierId = landlord.Id, DebitAccountCode = "6000",
        })).Error.Code.Should().Be("Cb.StandingOrderInvalid");
        (await service.CreateStandingOrderAsync(new StandingOrderRequest
        {
            CompanyId = companyId, Reference = "so-rent", BankAccountCode = "1100", Amount = 500m, Frequency = RecurrenceFrequency.Monthly, StartDate = new DateOnly(2026, 1, 31), DebitAccountCode = "6000",
            Narrative = "Office rent",
        })).IsSuccess.Should().BeTrue();
        (await service.CreateStandingOrderAsync(new StandingOrderRequest
        {
            CompanyId = companyId, Reference = "SO-SUP", BankAccountCode = "1100", Amount = 200m, Frequency = RecurrenceFrequency.Quarterly, StartDate = new DateOnly(2026, 1, 15),
            EndDate = new DateOnly(2026, 6, 30), SupplierId = landlord.Id,
        })).IsSuccess.Should().BeTrue();
        (await service.CreateMandateAsync(new DirectDebitMandateRequest
        {
            CompanyId = companyId, Reference = "SO-RENT", CustomerId = member.Id, CustomerBankAccount = "12345", BankAccountCode = "1100", StartDate = new DateOnly(2026, 2, 1),
        })).Error.Code.Should().Be("Cb.ReferenceTaken");
        (await service.CreateMandateAsync(new DirectDebitMandateRequest
        {
            CompanyId = companyId, Reference = "DD-MEM", CustomerId = member.Id, CustomerBankAccount = "12345", BankAccountCode = "1100", StartDate = new DateOnly(2026, 2, 1), MaximumAmount = 300m,
        })).IsSuccess.Should().BeTrue();

        // To the end of March: rent on 31 Jan, 28 Feb and 31 Mar; the landlord on 15 Jan; the member's 500 collected 300 then 200.
        RecurringBankRun march = (await service.RunDueAsync(companyId, new DateOnly(2026, 3, 31))).Value;
        march.Lines.Select(l => (l.DocumentNumber, l.Amount)).Should().Equal(
            ("SO-RENT-20260131", 500m), ("SO-RENT-20260228", 500m), ("SO-RENT-20260331", 500m), ("SO-SUP-20260115", 200m), ("DD-MEM-20260201", 300m), ("DD-MEM-20260301", 200m));
        (await service.RunDueAsync(companyId, new DateOnly(2026, 3, 31))).Value.Lines.Should().BeEmpty();

        // The member owes nothing more; the landlord's order ends in June; the rent is stopped after April.
        RecurringBankRun april = (await service.RunDueAsync(companyId, new DateOnly(2026, 4, 30))).Value;
        april.Lines.Select(l => l.DocumentNumber).Should().Equal("SO-RENT-20260430", "SO-SUP-20260415");
        april.NothingOwed.Should().Be(1);
        (await service.CancelAsync(companyId, "so-rent")).IsSuccess.Should().BeTrue();
        (await service.RunDueAsync(companyId, new DateOnly(2026, 12, 31))).Value.Lines.Should().BeEmpty();

        decimal bank = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == "1100").SumAsync(l => l.BaseDebit - l.BaseCredit);
        bank.Should().Be(500m - 2_000m - 400m);
    }

    [SkippableFact]
    public async Task Gift_card_stock_is_activated_at_sale()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IGiftCardServicing cards = scope.ServiceProvider.GetRequiredService<IGiftCardServicing>();
        IVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IVoucherService>();

        (await cards.CreateRangeAsync(companyId, "gcx", 1, 5, 4, Scenario.Usd, new DateOnly(2026, 5, 1))).Value.Should().Be(5);
        (await cards.CreateRangeAsync(companyId, "GCX", 5, 6, 4, Scenario.Usd, new DateOnly(2026, 5, 1))).Error.Code.Should().Be("Voucher.RangeOverlaps");
        (await cards.CreateRangeAsync(companyId, "GCY", 1, 10_000, 3, Scenario.Usd, new DateOnly(2026, 5, 1))).Error.Code.Should().Be("Voucher.RangeInvalid");
        (await vouchers.RedeemAsync(companyId, "GCX0001", 5m)).IsFailure.Should().BeTrue();
        (await cards.ActivateAsync(companyId, "GCX0001", 25m, new DateOnly(2026, 5, 2), new DateOnly(2027, 5, 2), "1000", "2300")).Value.Status.Should().Be(GiftCardStatus.Active);
        (await cards.ActivateAsync(companyId, "GCX0001", 25m, new DateOnly(2026, 5, 2), null, "1000", "2300")).Error.Code.Should().Be("Voucher.CannotActivate");
        (await vouchers.RedeemAsync(companyId, "GCX0001", 10m)).Value.Balance.Should().Be(15m);
        (await cards.HistoryAsync(companyId, "GCX0001")).Value.First().Event.Should().Be(GiftCardEvent.Activated);
        decimal liability = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == "2300").SumAsync(l => l.BaseCredit - l.BaseDebit);
        liability.Should().Be(25m);
    }
}

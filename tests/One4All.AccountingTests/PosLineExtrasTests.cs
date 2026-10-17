using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Till extras from the twelfth audit: a serial-tracked item needs its serial number, lines carry a note and gift wrap, and a
/// cash-up variance over the till's limit needs another user's sign-off.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosLineExtrasTests
{
    private readonly AccountingFixture _fixture;

    public PosLineExtrasTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Serial_numbers_notes_and_gift_wrap_are_taken_and_the_cash_up_is_signed_off()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long terminalId;
        long warehouseId;
        long phoneId;
        long sodaId;
        long supervisorId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Branch branch = new(companyId, "HO", "Head Office");
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
            PosTerminal terminal = new(companyId, branch.Id, "TILLX", "Serial Till") { CashUpSignOffVariance = 2m };
            db.PosTerminals.Add(terminal);
            Item phone = new(companyId, "PHONE", "Phone") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000", Tracking = ItemTracking.Serial };
            Item soda = new(companyId, "SODA-X", "Soda") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.AddRange(phone, soda);
            Warehouse wh = new(companyId, "WHX", "Shop");
            db.Warehouses.Add(wh);
            Role tillSupervisors = new($"Till supervisors {companyId}");
            tillSupervisors.Grant("Pos.TillSession", OperationFlags.View | OperationFlags.AuthoriseLevel1);
            db.Roles.Add(tillSupervisors);
            await db.SaveChangesAsync();
            User supervisor = new($"till-super-{companyId}", "Till Supervisor");
            supervisor.AssignRole(tillSupervisors.Id);
            db.Users.Add(supervisor);
            await db.SaveChangesAsync();
            (terminalId, warehouseId, phoneId, sodaId, supervisorId) = (terminal.Id, wh.Id, phone.Id, soda.Id, supervisor.Id);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            long sessionId = (await pos.OpenTillAsync(companyId, terminalId, cashierUserId: 777, openingFloat: 100m)).Value;

            CompleteSaleRequest Sale(string number, params PosSaleLineRequest[] lines) => new()
            {
                CompanyId = companyId, TerminalId = terminalId, TillSessionId = sessionId, CashierUserId = 777, SaleNumber = number, SaleDate = new DateOnly(2026, 7, 1), WarehouseId = warehouseId,
                TaxAccountCode = "2200", Lines = lines, Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, lines.Sum(l => l.LineNet + l.TaxAmount), "1000", IsChange: false)],
            };

            // The phone is serial-tracked: no serial, or two on one line, is refused; one with its serial sells, with a note and gift wrap on the line.
            (await pos.CompleteSaleAsync(Sale("SX-1", new PosSaleLineRequest(phoneId, "Phone", 1m, 100m, 100m, 15m, "4000")))).Error.Code.Should().Be("Pos.SerialRequired");
            (await pos.CompleteSaleAsync(Sale("SX-1", new PosSaleLineRequest(phoneId, "Phone", 2m, 100m, 200m, 30m, "4000", SerialNumber: "sn-1")))).Error.Code.Should().Be("Pos.SerialOnePerLine");
            var sold = await pos.CompleteSaleAsync(Sale("SX-1", new PosSaleLineRequest(phoneId, "Phone", 1m, 100m, 100m, 15m, "4000", SerialNumber: "sn-001", Note: "Engrave: Thandi", GiftWrap: true),
                new PosSaleLineRequest(sodaId, "Soda", 1m, 5m, 5m, 0m, "4000")));
            sold.IsSuccess.Should().BeTrue(sold.IsFailure ? sold.Error.ToString() : null);

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            Sale sale = await db.Sales.AsNoTracking().Include(s => s.Lines).SingleAsync(s => s.CompanyId == companyId && s.SaleNumber == "SX-1");
            SaleLine phoneLine = sale.Lines.Single(l => l.ItemId == phoneId);
            phoneLine.SerialNumber.Should().Be("SN-001");
            phoneLine.Note.Should().Be("Engrave: Thandi");
            phoneLine.GiftWrap.Should().BeTrue();
            sale.Lines.Single(l => l.ItemId == sodaId).GiftWrap.Should().BeFalse();

            // Drawer: 100 float + 120 takings = 220; counted 215 is 5 short, over the till's 2 limit: needs a supervisor who is not the cashier.
            CashUpRequest CashUp(long? supervisor) => new() { TillSessionId = sessionId, CountedCash = 215m, Date = new DateOnly(2026, 7, 1), CashAccountCode = "1000", OverShortAccountCode = "7500", CountedByUserId = 777, SupervisorUserId = supervisor };
            (await pos.CashUpAndCloseAsync(CashUp(null))).Error.Code.Should().Be("Pos.CashUp.SignOffRequired");
            (await pos.CashUpAndCloseAsync(CashUp(777))).Error.Code.Should().Be("Pos.CashUp.SelfSignOff");
            (await pos.CashUpAndCloseAsync(CashUp(999_999))).Error.Code.Should().Be("Pos.CashUp.SignOffNotAllowed");
            var closed = await pos.CashUpAndCloseAsync(CashUp(supervisorId));
            closed.IsSuccess.Should().BeTrue($"supervisor {supervisorId}, cashier 777: " + (closed.IsFailure ? closed.Error.ToString() : string.Empty));
            (await db.Set<TillSession>().AsNoTracking().SingleAsync(s => s.Id == sessionId)).SignedOffByUserId.Should().Be(supervisorId);
            (await db.Set<PosEvent>().AsNoTracking().AnyAsync(e => e.TillSessionId == sessionId && e.Kind == PosEventKind.CashUpSignOff)).Should().BeTrue();
        }
    }
}

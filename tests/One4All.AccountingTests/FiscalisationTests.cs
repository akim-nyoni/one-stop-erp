using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers fiscalisation at the till: opening a till opens the fiscal day; a sale is signed by the device and its receipt carries the
/// fiscal code and verification; with the device unreachable the sale still completes but waits in the queue (its receipt says so),
/// the retry signs it once the device is back; closing the last till closes the day, and the next till opens the next day.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FiscalisationTests
{
    private readonly AccountingFixture _fixture;

    public FiscalisationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Sales_are_fiscalised_or_queued_and_retried()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        SimulatedFiscalDeviceProvider device = new();
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton<IFiscalDeviceProvider>(device));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "FS", "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "FSTILL", "Till");
        Warehouse shop = new(companyId, "FS-WH", "Shop floor");
        Item soap = new(companyId, "FS-SOAP", "Soap") { RevenueAccountCode = "4000" };
        db.AddRange(terminal, shop, soap);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        DateOnly day = new(2026, 6, 2);
        Task<Result<CompleteSaleResult>> Sell(string number) => pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = day, WarehouseId = shop.Id, TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(soap.Id, "Soap", 2m, 5m, 10m, 1.5m, "4000")],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 11.5m, "1000", IsChange: false)],
        });
        async Task<Sale> Load(long id) => await db.Set<Sale>().AsNoTracking().SingleAsync(s => s.Id == id);
        IPosReceiptService receipts = scope.ServiceProvider.GetRequiredService<IPosReceiptService>();

        CompleteSaleResult signed = (await Sell("FS-1")).Value;
        Sale first = await Load(signed.SaleId);
        (first.FiscalStatus, first.FiscalCode, first.FiscalDayNumber).Should().Be((FiscalStatus.Fiscalised, "SIM-0001-000001", 1));
        string receipt = (await receipts.RenderReceiptTextAsync(companyId, signed.SaleId)).Value;
        receipt.Should().Contain("Fiscal code").And.Contain("SIM-0001-000001").And.Contain("Verify:");

        // The device goes down: the sale stands and waits.
        device.Offline = true;
        CompleteSaleResult waiting = (await Sell("FS-2")).Value;
        Sale second = await Load(waiting.SaleId);
        (second.FiscalStatus, second.FiscalAttempts).Should().Be((FiscalStatus.Queued, 1));
        (await receipts.RenderReceiptTextAsync(companyId, waiting.SaleId)).Value.Should().Contain("FISCAL SIGNATURE PENDING");
        (await pos.RetryFiscalAsync(companyId)).Should().Be((0, 1));

        // Back up: the retry signs it.
        device.Offline = false;
        (await pos.RetryFiscalAsync(companyId)).Should().Be((1, 0));
        Sale retried = await Load(waiting.SaleId);
        (retried.FiscalStatus, retried.FiscalCode, retried.FiscalAttempts, retried.FiscalError).Should().Be((FiscalStatus.Fiscalised, "SIM-0001-000002", 3, (string?)null));

        // Closing the last till closes the day; the next till opens day 2.
        (await pos.CashUpAndCloseAsync(new CashUpRequest { TillSessionId = session, CountedCash = 23m, Date = day, CashAccountCode = "1000", OverShortAccountCode = "6900" })).IsSuccess.Should().BeTrue();
        _ = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        List<string> events = await db.Set<PosEvent>().AsNoTracking().Where(e => e.CompanyId == companyId && e.Kind == PosEventKind.FiscalSubmission).OrderBy(e => e.Id)
            .Select(e => e.Description).ToListAsync();
        events.Should().ContainInOrder("Fiscal day 1 open", "Sale FS-1 fiscalised SIM-0001-000001", "Fiscal day closed", "Fiscal day 2 open");
        events.Should().Contain(e => e.StartsWith("Sale FS-2 queued for the fiscal device", StringComparison.Ordinal));
    }
}

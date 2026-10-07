using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Banking;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Licensing.Core;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers catalogue reports that were missing: CB69 bank reconciliation statement (unpresented payment, uncleared receipt, statement
/// charge not yet booked, reconciled to nil), CB71 bank balances, POS102 Z/X-read per session with tenders per currency, SYS235 audit
/// trail and SYS246 data health.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class MissingCatalogueReportsTests
{
    private readonly AccountingFixture _fixture;

    public MissingCatalogueReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Bank_pos_audit_and_health_reports_render()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<One4All.Infrastructure.Persistence.Seeding.ReportCatalogueSeeder>().SeedAsync();
        string code = $"REC{Guid.NewGuid():N}"[..10];
        BankAccount bank = new(companyId, code, "Statement Current Account", Scenario.Usd, "1100");
        db.Set<BankAccount>().Add(bank);
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        Task<One4All.Core.Common.Result<PostingReceipt>> Post(DateOnly date, decimal amount) => engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "CB", PostingDate = date, TransactionCurrency = Scenario.Usd,
            Lines =
            [
                new JournalLineRequest { AccountCode = "1100", Side = amount > 0 ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(amount) },
                new JournalLineRequest { AccountCode = "4000", Side = amount > 0 ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(amount) },
            ],
        });
        (await Post(new DateOnly(2026, 5, 3), 500m)).IsSuccess.Should().BeTrue();
        (await Post(new DateOnly(2026, 5, 20), -120m)).IsSuccess.Should().BeTrue();
        (await Post(new DateOnly(2026, 5, 30), 75m)).IsSuccess.Should().BeTrue();
        IBankReconciliationService banking = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        (await banking.ImportStatementLinesAsync(companyId, bank.Id,
            [new StatementLineImport(new DateOnly(2026, 5, 3), "Deposit", 500m, null), new StatementLineImport(new DateOnly(2026, 5, 31), "Bank charges", -8m, null)])).IsSuccess.Should().BeTrue();
        (await banking.AutoMatchAsync(companyId, bank.Id)).Value.Should().Be(1);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string report, Dictionary<string, object?> parameters)
        {
            var result = await runner.RenderAsync(companyId, report, parameters, ReportFormat.Csv);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            return Encoding.UTF8.GetString(result.Value.Content);
        }

        DateOnly asAt = new(2026, 5, 31);
        string statement = await Render("CB69", new() { ["BankCode"] = code.ToLowerInvariant(), ["AsAtDate"] = asAt });
        statement.Should().Contain("Balance per bank statement").And.Contain("492").And.Contain("payments not yet presented").And.Contain("-120")
            .And.Contain("receipts not yet on the statement").And.Contain("75").And.Contain("statement items not yet in the cash book").And.Contain("Balance per cash book")
            .And.Contain("455").And.Contain("Reconciled — no difference");

        string balances = await Render("CB71", new() { ["AsAtDate"] = asAt });
        balances.Should().Contain(code.ToUpperInvariant()).And.Contain("455").And.Contain("never");

        // Till: one session with a USD cash sale.
        Branch branch = new(companyId, $"ZR{Guid.NewGuid():N}"[..8], "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        string till = $"Z{Guid.NewGuid():N}"[..8];
        PosTerminal terminal = new(companyId, branch.Id, till, "Till");
        Item item = new(companyId, $"ZR-{Guid.NewGuid():N}"[..10], "Soap") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse shop = new(companyId, $"ZW{Guid.NewGuid():N}"[..8], "Shop floor");
        db.AddRange(terminal, item, shop);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = item.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 6, 1), Quantity = 10m, UnitCost = 1m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 20m)).Value;
        (await pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = $"ZR-{Guid.NewGuid():N}"[..12], SaleDate = new DateOnly(2026, 6, 2),
            WarehouseId = shop.Id, Lines = [new PosSaleLineRequest(item.Id, "Soap", 3m, 2.5m, 7.5m, 0m, "4000")],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 7.5m, "1000", IsChange: false)],
        })).IsSuccess.Should().BeTrue();
        DateOnly today = DateOnly.FromDateTime(scope.ServiceProvider.GetRequiredService<IClock>().UtcNow.UtcDateTime);
        string zread = await Render("POS102", new() { ["FromDate"] = today.AddDays(-1), ["ToDate"] = today.AddDays(1) });
        zread.Should().Contain(till.ToUpperInvariant()).And.Contain("X (open)").And.Contain("Cash").And.Contain("7.5");

        string auditTrail = await Render("SYS235", new() { ["FromDate"] = today.AddDays(-1), ["ToDate"] = today.AddDays(1), ["EntityType"] = nameof(BankAccount) });
        auditTrail.Should().Contain($"BankAccount #{bank.Id}").And.Contain("Insert");

        string health = await Render("SYS246", new() { ["AsAtDate"] = asAt });
        health.Should().Contain("Check").And.Contain("Status");
        health.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.Should().BeGreaterThan(2);
    }
}

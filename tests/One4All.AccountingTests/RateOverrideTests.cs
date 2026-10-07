using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers rate-override control: with it on, a rate keyed on a document needs the exchange-rate override permission and is recorded against
/// the table's rate; documents at the table's rate need nothing; with it off, keyed rates pass as before.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RateOverrideTests
{
    private readonly AccountingFixture _fixture;

    public RateOverrideTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Keyed_rates_need_the_override_permission_and_are_recorded()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Role treasury = new($"Treasury {companyId}");
        treasury.Grant("System.ExchangeRate", OperationFlags.View | OperationFlags.OverridePrice);
        db.Roles.Add(treasury);
        User clerk = new($"ro-clerk-{companyId}", "Clerk"), dealer = new($"ro-dealer-{companyId}", "Dealer");
        db.Users.AddRange(clerk, dealer);
        Supplier supplier = new(companyId, "RO-SUP", "Euro Supplier", Scenario.Eur);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        dealer.AssignRole(treasury.Id);
        await db.SaveChangesAsync();

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        PurchaseInvoiceRequest Invoice(string number, decimal? rate, long? by) => new()
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Eur, NetAmount = 100m, ExpenseAccountCode = "5000",
            ExchangeRate = rate, RateOverrideByUserId = by,
        };
        (await ap.PostInvoiceAsync(Invoice("RO-0", 1.3m, clerk.Id))).IsSuccess.Should().BeTrue("control off: as before");

        (await db.Companies.SingleAsync(c => c.Id == companyId)).RateOverrideControl = true;
        await db.SaveChangesAsync();
        (await ap.PostInvoiceAsync(Invoice("RO-1", 1.3m, clerk.Id))).Error.Code.Should().Be("Trading.RateOverrideNotAllowed");
        (await ap.PostInvoiceAsync(Invoice("RO-1", 1.3m, null))).Error.Code.Should().Be("Trading.RateOverrideNotAllowed");
        (await ap.PostInvoiceAsync(Invoice("RO-1", 1.3m, dealer.Id))).Value.BaseAmount.Should().Be(130m);
        (await ap.PostInvoiceAsync(Invoice("RO-2", null, clerk.Id))).Value.BaseAmount.Should().Be(110m, "the table's daily rate needs nothing");

        ExchangeRateOverride kept = await db.Set<ExchangeRateOverride>().AsNoTracking().SingleAsync(o => o.CompanyId == companyId);
        (kept.DocumentNumber, kept.Currency, kept.KeyedRate, kept.TableRate, kept.UserId).Should().Be(("RO-1", "EUR", 1.3m, (decimal?)1.10m, dealer.Id));
    }
}

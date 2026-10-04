using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers service deposits held as a liability: a 150 water deposit is taken (Dr bank / Cr consumer deposits) and a
/// second for the same service is refused; one stand's deposit is refunded (bank and liability back to zero); another's
/// is applied to the account, leaving a credit that clears a 150 final bill. The consumer-deposits liability nets to
/// zero; the refunded deposit leaves the bank flat while the applied one stays as cash matched by the final bill.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ServiceDepositTests
{
    private readonly AccountingFixture _fixture;

    public ServiceDepositTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Deposits_are_held_then_refunded_or_applied_to_the_account()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2250", "Consumer Deposits", AccountType.Liability));
        Customer refundOwner = new(companyId, "SD-REF", "Refund Owner", Scenario.Usd) { PaymentTermsDays = 0 };
        Customer applyOwner = new(companyId, "SD-APP", "Apply Owner", Scenario.Usd) { PaymentTermsDays = 0 };
        db.Customers.AddRange(refundOwner, applyOwner);
        await db.SaveChangesAsync();
        Property leaving = new(companyId, "D1", "Ward 14", refundOwner.Id);
        Property closing = new(companyId, "D2", "Ward 14", applyOwner.Id);
        db.Properties.AddRange(leaving, closing);
        await db.SaveChangesAsync();

        IServiceDepositService deposits = scope.ServiceProvider.GetRequiredService<IServiceDepositService>();
        TakeDepositRequest Take(long propertyId, decimal amount) => new()
        {
            CompanyId = companyId, PropertyId = propertyId, ServiceType = ServiceType.Water, Amount = amount, Date = new DateOnly(2026, 1, 10), BankAccountCode = "1100", LiabilityAccountCode = "2250",
        };

        DepositTaken first = (await deposits.TakeAsync(Take(leaving.Id, 150m))).Value;
        first.Reference.Should().StartWith("DEP-");
        (await deposits.TakeAsync(Take(leaving.Id, 150m))).Error.Code.Should().Be("Mun.DepositHeld");
        string second = (await deposits.TakeAsync(Take(closing.Id, 150m))).Value.Reference;
        (await deposits.HeldAsync(companyId, leaving.Id)).Should().ContainSingle().Which.Amount.Should().Be(150m);

        (await deposits.RefundAsync(companyId, first.Reference, new DateOnly(2026, 6, 1), "1100")).IsSuccess.Should().BeTrue();
        (await deposits.RefundAsync(companyId, first.Reference, new DateOnly(2026, 6, 1), "1100")).Error.Code.Should().Be("Mun.DepositReleased");
        (await deposits.HeldAsync(companyId, leaving.Id)).Should().BeEmpty();

        // The closing account gets a final 150 water bill, then the deposit is applied to clear it.
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = applyOwner.Id, DocumentNumber = "SD-FINAL", DocumentDate = new DateOnly(2026, 6, 2), Currency = Scenario.Usd, NetAmount = 150m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();
        string receipt = (await deposits.ApplyToArrearsAsync(companyId, second, new DateOnly(2026, 6, 3))).Value;
        receipt.Should().Be(second + "-F");

        decimal Balance(string code)
        {
            ConsolidatedTrialBalance tb = scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 6, 30)).GetAwaiter().GetResult().Value;
            ConsolidatedTrialBalanceRow? row = tb.Rows.SingleOrDefault(r => r.AccountCode == code);
            return row is null ? 0m : row.Debit - row.Credit;
        }

        Balance("2250").Should().Be(0m); // the deposit liability is fully released
        Balance("1100").Should().Be(150m); // the applied deposit is cash the council keeps against the final bill
        Balance("4000").Should().Be(-150m); // earned as revenue on the final bill
        (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == applyOwner.Id).SumAsync(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase)).Should().Be(0m);
    }
}

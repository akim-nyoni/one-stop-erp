using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ReceiptAllocationTests
{
    private const long Cashier = 6001;

    private readonly AccountingFixture _fixture;

    public ReceiptAllocationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Allocation_strategies_and_user_directed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer resident = new(companyId, "RES-A", "Multi-charge Resident", Scenario.Usd);
            db.Customers.Add(resident);
            CashierStation station = new(companyId, "TILL-A", "Counter A", "RA");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            customerId = resident.Id;
            stationId = station.Id;
        }

        // Three charges: Jan 100, Feb 200, Mar 300.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "JAN", DocumentDate = new DateOnly(2026, 1, 31), Currency = Scenario.Usd, NetAmount = 100m, SalesAccountCode = "4000" });
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "FEB", DocumentDate = new DateOnly(2026, 2, 28), Currency = Scenario.Usd, NetAmount = 200m, SalesAccountCode = "4000" });
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "MAR", DocumentDate = new DateOnly(2026, 3, 31), Currency = Scenario.Usd, NetAmount = 300m, SalesAccountCode = "4000" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();

            // Oldest-first preview of 250 → JAN 100 + FEB 150.
            var oldest = await rcp.PreviewAllocationAsync(companyId, customerId, 250m, ReceiptAllocationStrategy.OldestFirst);
            oldest.IsSuccess.Should().BeTrue(oldest.IsFailure ? oldest.Error.ToString() : null);
            oldest.Value.Lines.Should().SatisfyRespectively(
                l => { l.DocumentNumber.Should().Be("JAN"); l.Applied.Should().Be(100m); },
                l => { l.DocumentNumber.Should().Be("FEB"); l.Applied.Should().Be(150m); });
            oldest.Value.CreditOnAccount.Should().Be(0m);

            // Newest-first preview of 250 → MAR 250.
            var newest = await rcp.PreviewAllocationAsync(companyId, customerId, 250m, ReceiptAllocationStrategy.NewestFirst);
            newest.Value.Lines.Should().ContainSingle().Which.DocumentNumber.Should().Be("MAR");
            newest.Value.Lines[0].Applied.Should().Be(250m);
        }

        long febItemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            febItemId = await db.Set<OpenItem>().Where(i => i.CompanyId == companyId && i.TraderId == customerId && i.DocumentNumber == "FEB").Select(i => i.Id).FirstAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            var open = await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 4, 1), [new FloatDeclaration(Scenario.Usd, 0m)]);
            long sessionId = open.Value;

            // User-directs 150 specifically to the FEB charge.
            var receipt = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.ArCustomer, PayerAccountId = customerId,
                Date = new DateOnly(2026, 4, 1), CashAccountCode = "1000",
                AllocationStrategy = ReceiptAllocationStrategy.UserDirected,
                DirectedAllocations = [new DirectedAllocation(febItemId, 150m)],
                Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, 150m)],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 150m)],
            });
            receipt.IsSuccess.Should().BeTrue(receipt.IsFailure ? receipt.Error.ToString() : null);
            receipt.Value.AllocatedToDebt.Should().Be(150m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            // FEB reduced 200 → 50; JAN and MAR untouched.
            decimal feb = await db.Set<OpenItem>().Where(i => i.CompanyId == companyId && i.TraderId == customerId && i.DocumentNumber == "FEB").Select(i => i.OutstandingTransaction).FirstAsync();
            decimal jan = await db.Set<OpenItem>().Where(i => i.CompanyId == companyId && i.TraderId == customerId && i.DocumentNumber == "JAN").Select(i => i.OutstandingTransaction).FirstAsync();
            feb.Should().Be(50m);
            jan.Should().Be(100m);
        }
    }
}

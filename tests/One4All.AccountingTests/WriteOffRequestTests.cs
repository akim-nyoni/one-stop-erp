using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Fourteenth audit #5: a bad-debt write-off is asked for by one user and written off only when another approves it.</summary>
[Collection(AccountingCollection.Name)]
public sealed class WriteOffRequestTests
{
    private const long Clerk = 95;
    private const long Controller = 96;

    private readonly AccountingFixture _fixture;

    public WriteOffRequestTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_write_off_waits_for_a_second_user()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "WOR-C", "Gone Away Ltd", Scenario.Usd);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        long invoice = (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "WOR-I1", DocumentDate = new DateOnly(2026, 1, 10), Currency = Scenario.Usd, NetAmount = 300m, SalesAccountCode = "4000",
        })).Value.OpenItemId;

        IWriteOffRequestService writeOffs = scope.ServiceProvider.GetRequiredService<IWriteOffRequestService>();
        long request = (await writeOffs.RequestAsync(companyId, invoice, "6800", "Customer liquidated", 0, Clerk)).Value;
        (await writeOffs.RequestAsync(companyId, invoice, "6800", "Again", 0, Clerk)).Error.Code.Should().Be("Ar.WriteOffPending");
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == invoice)).OutstandingTransaction.Should().Be(300m, "nothing is written off before approval");
        (await writeOffs.PendingAsync(companyId)).Should().ContainSingle(p => p.RequestId == request && p.CustomerCode == "WOR-C" && p.Amount == 300m);

        (await writeOffs.DecideAsync(companyId, request, true, Clerk, new DateOnly(2026, 9, 1), null)).Error.Code.Should().Be("Ar.WriteOffSelfApproval");
        (await writeOffs.DecideAsync(companyId, request, false, Controller, new DateOnly(2026, 9, 1), " ")).Error.Code.Should().Be("Ar.WriteOffReason");
        VatReliefWriteOff? written = (await writeOffs.DecideAsync(companyId, request, true, Controller, new DateOnly(2026, 9, 1), null)).Value;
        written!.WrittenOffToBadDebt.Should().BeGreaterThan(0m);
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == invoice)).OutstandingTransaction.Should().Be(0m);
        (await db.Set<WriteOffRequest>().AsNoTracking().SingleAsync(r => r.Id == request)).Status.Should().Be(WriteOffRequestStatus.Approved);
    }
}

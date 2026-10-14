using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;

namespace One4All.AccountingTests;

/// <summary>Covers the rule that a cashier may not receipt against their own or a related account: refused, logged, and shown on RC23.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CashierRelatedAccountTests
{
    private const long Cashier = 9902;
    private readonly AccountingFixture _fixture;

    public CashierRelatedAccountTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_cashier_cannot_receipt_against_a_related_account()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        CashierStation station = new(companyId, "TILL-RA", "Counter RA", "RA");
        Customer own = new(companyId, "RA-OWN", "The Cashier Herself", Scenario.Usd), other = new(companyId, "RA-OTH", "A Ratepayer", Scenario.Usd);
        db.AddRange(station, own, other);
        await db.SaveChangesAsync();
        db.Add(new CashierRelatedAccount(companyId, Cashier, own.Id, "Own"));
        await db.SaveChangesAsync();

        IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
        long session = (await rcp.OpenSessionAsync(companyId, station.Id, Cashier, new DateOnly(2026, 3, 2), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
        ReceiptRequest Pay(Customer customer) => new()
        {
            CompanyId = companyId, SessionId = session, PayerKind = PayerKind.ArCustomer, PayerAccountId = customer.Id, Date = new DateOnly(2026, 3, 2), CashAccountCode = "1000",
            Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, 40m, null, "Payment")],
            Tenders = [new ReceiptTenderRequest(TenderType.Cash, 40m)],
        };

        (await rcp.PostReceiptAsync(Pay(own))).Error.Code.Should().Be("Rcp.RelatedAccount");
        (await rcp.PostReceiptAsync(Pay(other))).IsSuccess.Should().BeTrue();

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "RC23",
            new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2020, 1, 1), ["ToDate"] = new DateOnly(2100, 12, 31) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        Encoding.UTF8.GetString(rendered.Value.Content).Should().Contain("RA-OWN The Cashier Herself").And.Contain("Refused (Own)").And.Contain("40");
    }
}

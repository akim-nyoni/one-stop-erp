using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Receipting;
using One4All.Infrastructure.Persistence;
using Xunit;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ReceiptCurrencyTests
{
    private const long Cashier = 7001;

    private readonly AccountingFixture _fixture;

    public ReceiptCurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Foreign_currency_sundry_receipt_converts_to_base()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            CashierStation station = new(companyId, "TILL-F", "FX Counter", "RF");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            stationId = station.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            var open = await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 3, 1), [new FloatDeclaration(Scenario.Eur, 0m)]);
            long sessionId = open.Value;

            // Sundry income of EUR 100 at the daily rate 1.10 → USD 110 base.
            var receipt = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.Anonymous,
                Date = new DateOnly(2026, 3, 1), Currency = Scenario.Eur, CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Sundry, 100m, "4400", "Tender document (EUR)")],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 100m)],
            });
            receipt.IsSuccess.Should().BeTrue(receipt.IsFailure ? receipt.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4400" && r.Credit == 110m); // EUR 100 × 1.10
            tb.Rows.Should().Contain(r => r.AccountCode == "1000" && r.Debit == 110m);
        }
    }
}

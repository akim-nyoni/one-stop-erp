using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>VAT inside money paid straight from the bank: vouchers and bank statement lines split by a tax code, the VAT to its account and tagged.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CashBookVatTests
{
    private readonly AccountingFixture _fixture;

    public CashBookVatTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Direct_bank_payments_claim_their_vat()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await scope.ServiceProvider.GetRequiredService<IMasterDataService>().CreateTaxCodeAsync(new NewTaxCode(companyId, "CBV-STD", "Standard", TaxType.Standard, 15m,
            new DateOnly(2026, 1, 1), "2200", "2210"))).IsSuccess.Should().BeTrue();
        BankAccount bank = new(companyId, "CBV-B", "Current", Scenario.Usd, "1100");
        db.Add(bank);
        await db.SaveChangesAsync();
        async Task<Dictionary<string, (decimal Net, string? Code)>> Lines(string journal) => (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.JournalNumber == journal)
                .SelectMany(j => j.Lines).ToListAsync())
            .GroupBy(l => l.AccountCode).ToDictionary(g => g.Key, g => (g.Sum(l => l.BaseDebit - l.BaseCredit), g.Select(l => l.TaxCode).FirstOrDefault()));

        // A payment voucher of 230 including VAT: 200 to the expense, 30 input VAT, both tagged CBV-STD.
        IAccountingVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IAccountingVoucherService>();
        PreparedVoucher voucher = (await vouchers.PrepareAsync(companyId, new VoucherRequest(AccountingVoucherType.Payment, new DateOnly(2026, 3, 10), "Utility", "1100", "Electricity",
            [new VoucherLineRequest("6800", 230m, "March power", TaxCode: "cbv-std")], 61))).Value;
        voucher.Total.Should().Be(230m);
        (await vouchers.CheckAsync(companyId, voucher.VoucherId, 62)).IsSuccess.Should().BeTrue();
        Result<string> approved = await vouchers.ApproveAsync(companyId, voucher.VoucherId, 63);
        approved.IsSuccess.Should().BeTrue(approved.IsFailure ? approved.Error.ToString() : null);
        Dictionary<string, (decimal Net, string? Code)> paid = await Lines(approved.Value);
        (paid["6800"], paid["2210"], paid["1100"].Net).Should().Be(((200m, "CBV-STD"), (30m, "CBV-STD"), -230m));

        // A bank charge of 11.50 off the statement, VAT inclusive: 10 charges, 1.50 input VAT.
        IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        (await recon.ImportStatementLinesAsync(companyId, bank.Id, [new StatementLineImport(new DateOnly(2026, 3, 31), "Service fee", -11.50m, null)])).IsSuccess.Should().BeTrue();
        long fee = await db.Set<BankStatementLine>().Where(l => l.BankAccountId == bank.Id).Select(l => l.Id).SingleAsync();
        StatementLinePosting posted = (await scope.ServiceProvider.GetRequiredService<IBankStatementPostingService>().PostToAccountAsync(companyId, bank.Id, fee, "6800", null, default, "CBV-STD")).Value;
        Dictionary<string, (decimal Net, string? Code)> charge = await Lines(posted.JournalNumber);
        (charge["6800"].Net, charge["2210"].Net, charge["1100"].Net).Should().Be((10m, 1.50m, -11.50m));
    }
}

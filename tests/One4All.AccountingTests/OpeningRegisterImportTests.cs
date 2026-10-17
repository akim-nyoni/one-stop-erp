using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Onboarding;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Take-on of housing instalment agreements (stand sold, schedule rebuilt, instalments to date raised, arrears as an open item) and of
/// municipal arrears by property and service (an unpaid bill each on an opening billing run), both against the migration suspense account.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class OpeningRegisterImportTests
{
    private readonly AccountingFixture _fixture;

    public OpeningRegisterImportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Instalment_agreements_and_municipal_arrears_are_taken_on()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long buyerId;
        long ratepayerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            foreach ((string code, string name, AccountType type) in new[] { ("2900", "Migration Suspense", AccountType.Liability), ("4950", "Instalment Interest", AccountType.Income) })
            {
                if (!await db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == code))
                {
                    db.Accounts.Add(new LedgerAccount(companyId, code, name, type));
                }
            }
            Customer buyer = new(companyId, "OB-BUY", "Opening Buyer", Scenario.Usd);
            Customer ratepayer = new(companyId, "OB-RATE", "Opening Ratepayer", Scenario.Usd);
            db.Customers.AddRange(buyer, ratepayer);
            LandParcel parcel = new(companyId, $"LP-OB-{companyId}", "Take-on parcel", 5000m, 50000m);
            db.LandParcels.Add(parcel);
            await db.SaveChangesAsync();
            db.HousingStands.Add(new HousingStand(companyId, parcel.Id, null, "OB-ST-1", 300m, 1000m));
            db.Properties.Add(new Property(companyId, "OB-STD-7", "Ward 2", ratepayer.Id));
            await db.SaveChangesAsync();
            (buyerId, ratepayerId) = (buyer.Id, ratepayer.Id);
        }

        DateOnly asAt = new(2026, 6, 30);
        string agreements = "agreement,stand,buyer,cash_price,deposit,rate,method,term_months,signed,first_due,interest_account,tenure,escalation,arrears\n"
            + "IA-OB-1,OB-ST-1,OB-BUY,12000,1200,12,ReducingBalance,60,2025-03-01,2025-04-01,4950,InstalmentSale,0,350.00\n"
            + "IA-OB-2,OB-ST-9,OB-BUY,5000,0,0,Zero,10,2026-01-01,2026-02-01,4950,,,0\n";
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IVerticalImportService imports = scope.ServiceProvider.GetRequiredService<IVerticalImportService>();

            // The second row names a stand not on the register: the file imports nothing until it is fixed; a dry run writes nothing either.
            ImportReport bad = await imports.ImportInstalmentAgreementsAsync(companyId, agreements, asAt, "2900", commit: true);
            bad.Committed.Should().BeFalse();
            bad.Errors.Should().ContainSingle(e => e.Message.Contains("OB-ST-9"));

            string good = string.Join('\n', agreements.Split('\n').Take(2)) + "\n";
            ImportReport dry = await imports.ImportInstalmentAgreementsAsync(companyId, good, asAt, "2900", commit: false);
            dry.Should().BeEquivalentTo(new { Rows = 1, Created = 0, Skipped = 0, Committed = false });
            ImportReport done = await imports.ImportInstalmentAgreementsAsync(companyId, good, asAt, "2900", commit: true);
            done.Errors.Should().BeEmpty();
            done.Should().BeEquivalentTo(new { Rows = 1, Created = 1, Skipped = 0, Committed = true });
            (await imports.ImportInstalmentAgreementsAsync(companyId, good, asAt, "2900", commit: true)).Skipped.Should().Be(1, "an agreement already on file is skipped");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            InstalmentAgreement agreement = await db.Set<InstalmentAgreement>().AsNoTracking().Include(a => a.Lines).SingleAsync(a => a.CompanyId == companyId && a.AgreementNumber == "IA-OB-1");
            agreement.Lines.Should().HaveCount(60);
            agreement.Lines.Count(l => l.Raised).Should().Be(15, "April 2025 to June 2026 fell due before the opening date");
            agreement.Lines.Where(l => l.Raised).Should().OnlyContain(l => l.DueDate <= asAt);
            HousingStand stand = await db.HousingStands.AsNoTracking().Include(s => s.Ownership).SingleAsync(s => s.CompanyId == companyId && s.StandNumber == "OB-ST-1");
            stand.CurrentHolderCustomerId.Should().Be(buyerId);
            stand.Status.Should().Be(StandStatus.Sold);
            OpenItem arrears = await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == "IA-OB-1-OB");
            arrears.TraderId.Should().Be(buyerId);
            arrears.OutstandingTransaction.Should().Be(350m);
        }

        string municipal = "stand,service,invoice,amount,date\nOB-STD-7,Water,OB-W-7,420.00,2026-05-31\nOB-STD-7,Refuse,OB-R-7,80.00,2026-06-30\nOB-STD-8,Water,OB-W-8,10.00,\n";
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IVerticalImportService imports = scope.ServiceProvider.GetRequiredService<IVerticalImportService>();
            (await imports.ImportMunicipalArrearsAsync(companyId, municipal, asAt, "2900", commit: true)).Errors.Should().ContainSingle(e => e.Message.Contains("OB-STD-8"));
            string good = string.Join('\n', municipal.Split('\n').Take(3)) + "\n";
            ImportReport done = await imports.ImportMunicipalArrearsAsync(companyId, good, asAt, "2900", commit: true);
            done.Errors.Should().BeEmpty();
            done.Created.Should().Be(2);
            (await imports.ImportMunicipalArrearsAsync(companyId, good, asAt, "2900", commit: true)).Skipped.Should().Be(2);

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<BillingRun> runs = await db.Set<BillingRun>().AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == companyId && r.InvoicePrefix == "OB").ToListAsync();
            runs.Should().HaveCount(2);
            runs.Should().OnlyContain(r => r.Status == BillingRunStatus.Posted);
            runs.Single(r => r.ServiceType == ServiceType.Water).Lines.Should().ContainSingle(l => l.InvoiceNumber == "OB-W-7" && l.Charge == 420m && l.StandNumber == "OB-STD-7");
            (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == ratepayerId && i.Ledger == TradingLedger.AccountsReceivable).SumAsync(i => i.OutstandingTransaction)).Should().Be(500m);
            (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == "OB-W-7")).DocumentDate.Should().Be(new DateOnly(2026, 5, 31));
        }
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Accounting;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers optional fee items and pro-rata billing: a student taking lunches is billed them (to their own revenue account, undiscounted);
/// a joiner half-way through a pro-rata term pays half; a leaver is credited the unused part of the term invoice; with deferred fees the
/// credit comes out of deferred income and the recognition schedule ends on the leaving date.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SchoolOptionsProRataTests
{
    private readonly AccountingFixture _fixture;

    public SchoolOptionsProRataTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Optional_items_and_pro_rata_joiners_and_leavers_are_billed_fairly()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4210", "Lunch Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "2490", "Fees Received in Advance", AccountType.Liability));
        Customer guardian = new(companyId, "SOP-G", "Guardian", Scenario.Usd), other = new(companyId, "SOP-H", "Other Guardian", Scenario.Usd);
        db.Customers.AddRange(guardian, other);
        FeeStructure g2 = new(companyId, "SOP-G2", "Grade 2 Term 1", "SOP2", "Term 1", "4200");
        g2.AddItem("Tuition", 900m, true);
        FeeItem lunches = g2.AddItem("Lunches", 90m, false, "4210");
        FeeStructure g3 = new(companyId, "SOP-G3", "Grade 3 Term 1", "SOP3", "Term 1", "4200");
        g3.AddItem("Tuition", 900m, true);
        g3.DeferOverTerm(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), "2490");
        db.FeeStructures.AddRange(g2, g3);
        await db.SaveChangesAsync();
        Student full = new(companyId, "SOP-001", "Full Term", "SOP2", guardian.Id);
        Student joiner = new(companyId, "SOP-002", "Joiner", "SOP2", other.Id) { AdmittedOn = new DateOnly(2026, 2, 15) };
        Student boarder = new(companyId, "SOP-003", "Deferred Leaver", "SOP3", other.Id);
        db.Students.AddRange(full, joiner, boarder);
        await db.SaveChangesAsync();

        ISchoolBillingService billing = scope.ServiceProvider.GetRequiredService<ISchoolBillingService>();
        long tuition = g2.Items.Single(i => i.IsCompulsory).Id;
        (await billing.SetOptionAsync(companyId, full.Id, tuition, true)).Error.Code.Should().Be("School.OptionNotFound");
        (await billing.SetOptionAsync(companyId, full.Id, lunches.Id, true)).IsSuccess.Should().BeTrue();
        (await billing.SetTermAsync(companyId, g2.Id, new DateOnly(2026, 3, 31), new DateOnly(2026, 1, 1), true)).Error.Code.Should().Be("School.TermDates");
        (await billing.SetTermAsync(companyId, g2.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), true)).IsSuccess.Should().BeTrue();
        (await billing.SetTermAsync(companyId, g3.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), true)).IsSuccess.Should().BeTrue();

        // 90-day term: the joiner from 15 February is there 45 days (half); the full-term student also takes lunches.
        TermBillResult run = (await billing.BillTermAsync(companyId, g2.Id, "SOP-T1-", new DateOnly(2026, 1, 5))).Value;
        run.Bills.Select(b => (b.NetFees, b.OptionalFees, b.ShareOfTerm)).Should().Equal((990m, 90m, 1m), (450m, 0m, 0.5m));
        (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.DocumentNumber == "SOP-T1-SOP-001").SelectMany(j => j.Lines)
            .Where(l => l.AccountCode == "4210").SumAsync(l => l.BaseCredit)).Should().Be(90m);

        // Leaving on 1 March (60 of 90 days): the unused third of 990 is credited.
        var leaving = await billing.WithdrawAsync(companyId, full.Id, new DateOnly(2026, 3, 1), g2.Id, "SOP-T1-");
        leaving.IsSuccess.Should().BeTrue(leaving.IsFailure ? leaving.Error.ToString() : null);
        WithdrawalResult left = leaving.Value;
        left.Should().Be(new WithdrawalResult("SOP-T1-SOP-001-CR", 329.97m));
        (await billing.WithdrawAsync(companyId, full.Id, new DateOnly(2026, 3, 1), g2.Id, "SOP-T1-")).Error.Code.Should().Be("School.NotEnrolled");
        (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == guardian.Id).SumAsync(i => i.OutstandingBase)).Should().Be(660.03m);

        // Deferred fees: the credit comes out of deferred income and the schedule now ends on the leaving date.
        (await billing.BillTermAsync(companyId, g3.Id, "SOP-T1-", new DateOnly(2026, 1, 5))).Value.TotalNetBilled.Should().Be(900m);
        (await billing.WithdrawAsync(companyId, boarder.Id, new DateOnly(2026, 3, 1), g3.Id, "SOP-T1-")).Value.Credited.Should().Be(299.97m);
        DeferredIncomeSchedule schedule = await db.Set<DeferredIncomeSchedule>().AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.Reference == "SOP-T1-SOP-003");
        (schedule.Amount, schedule.EndDate, schedule.EarnedBy(new DateOnly(2026, 3, 1))).Should().Be((600.03m, new DateOnly(2026, 3, 1), 600.03m));
        (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.DocumentNumber == "SOP-T1-SOP-003-CR").SelectMany(j => j.Lines)
            .Where(l => l.AccountCode == "2490").SumAsync(l => l.BaseDebit)).Should().Be(299.97m);
    }

    [SkippableFact]
    public async Task A_leaver_is_credited_the_unused_transport_and_boarding()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4220", "Transport Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "4230", "Boarding Income", AccountType.Income));
        Customer guardian = new(companyId, "SOX-G", "Guardian", Scenario.Usd);
        db.Customers.Add(guardian);
        FeeStructure g4 = new(companyId, "SOX-G4", "Grade 4 Term 1", "SOX4", "Term 1", "4200");
        g4.AddItem("Tuition", 900m, true);
        TransportRoute bus = new(companyId, "SOX-R1", "Northern route", 40, 180m, "4220");
        Hostel house = new(companyId, "SOX-H1", "Boys' house", 60, 600m, "4230");
        db.AddRange(g4, bus, house);
        await db.SaveChangesAsync();
        Student leaver = new(companyId, "SOX-001", "Bus and Board", "SOX4", guardian.Id) { TransportRouteId = bus.Id, IsBoarder = true, HostelId = house.Id };
        db.Students.Add(leaver);
        await db.SaveChangesAsync();

        ISchoolBillingService billing = scope.ServiceProvider.GetRequiredService<ISchoolBillingService>();
        (await billing.SetTermAsync(companyId, g4.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), true)).IsSuccess.Should().BeTrue();
        (await billing.BillTermAsync(companyId, g4.Id, "SOX-T1-", new DateOnly(2026, 1, 5))).IsSuccess.Should().BeTrue();

        // Leaving on 1 March (60 of 90 days): a third of the tuition, the bus (59.99 of 180) and the boarding (199.98 of 600) come back.
        WithdrawalResult left = (await billing.WithdrawAsync(companyId, leaver.Id, new DateOnly(2026, 3, 1), g4.Id, "SOX-T1-")).Value;
        left.Should().Be(new WithdrawalResult("SOX-T1-SOX-001-CR", 299.97m, 259.97m));
        (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.DocumentNumber == "SOX-T1-SOX-001-HS-CR").SelectMany(j => j.Lines)
            .Where(l => l.AccountCode == "4230").SumAsync(l => l.BaseDebit)).Should().Be(199.98m);
        (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == guardian.Id).SumAsync(i => i.OutstandingBase)).Should().Be(1_680m - 299.97m - 259.97m);
    }
}

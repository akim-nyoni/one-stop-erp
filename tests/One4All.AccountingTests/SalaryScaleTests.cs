using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers salary scales (brief §4.15.1): the annual increment moves staff one notch up to the scale's salary, stopping at the top and not twice
/// in a year; an acting appointment pays the acted grade's scale less the employee's basic; and once a company keeps a post register, staff can
/// only be engaged or moved into a vacant approved post.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SalaryScaleTests
{
    private readonly AccountingFixture _fixture;

    public SalaryScaleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Increments_move_staff_up_the_scale_and_acting_pays_the_difference()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IEstablishmentService establishment = scope.ServiceProvider.GetRequiredService<IEstablishmentService>();
        DateOnly scaleDate = new(2025, 1, 1);
        foreach ((string grade, int notch, decimal amount) in new[] { ("G1", 1, 1000m), ("G1", 2, 1100m), ("G1", 3, 1200m), ("G2", 1, 1500m) })
        {
            (await establishment.SetScaleAsync(companyId, grade, notch, amount, scaleDate)).IsSuccess.Should().BeTrue();
        }

        PayElement basic = new(companyId, "BASIC", "Basic", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement actingPay = new(companyId, "ACTING", "Acting allowance", PayElementType.Earning, CalculationBasis.Formula, 2, "6100") { Formula = "ACTINGALLOWANCE", Taxable = true };
        db.PayElements.AddRange(basic, actingPay);
        Employee low = new(companyId, "SS-1", "Lena Low", 1000m, new DateOnly(2024, 1, 1)) { Grade = "G1", Notch = 1 };
        Employee top = new(companyId, "SS-2", "Tom Top", 1200m, new DateOnly(2024, 1, 1)) { Grade = "G1", Notch = 3 };
        Employee recent = new(companyId, "SS-3", "Rita Recent", 1100m, new DateOnly(2024, 1, 1)) { Grade = "G1", Notch = 2, LastIncrementOn = new DateOnly(2025, 9, 1) };
        db.Employees.AddRange(low, top, recent);
        await db.SaveChangesAsync();
        foreach (Employee e in new[] { low, top, recent })
        {
            e.AssignPayElement(basic.Id);
            e.AssignPayElement(actingPay.Id);
        }

        await db.SaveChangesAsync();

        IncrementResult result = (await establishment.RunIncrementsAsync(companyId, new DateOnly(2026, 1, 1))).Value;
        result.Moved.Should().ContainSingle().Which.Should().Be(("SS-1", 1, 2, 1100m));
        result.AtTop.Should().Equal("SS-2");
        (await establishment.RunIncrementsAsync(companyId, new DateOnly(2026, 2, 1))).Value.Moved.Should().BeEmpty("Lena was incremented within the year");

        (await establishment.AppointActingAsync(companyId, "SS-1", "G2", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31))).IsSuccess.Should().BeTrue();
        (await establishment.AppointActingAsync(companyId, "SS-1", "G9", 1, new DateOnly(2026, 1, 1), null)).Error.Code.Should().Be("Payroll.ActingScale");

        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "SS-2026-01")).IsSuccess.Should().BeTrue();
        List<Payslip> slips = await db.Payslips.AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == companyId).ToListAsync();
        Payslip lena = slips.Single(p => p.EmployeeId == low.Id);
        lena.Lines.Single(l => l.ElementCode == "BASIC").Amount.Should().Be(1100m);
        lena.Lines.Single(l => l.ElementCode == "ACTING").Amount.Should().Be(400m, "G2 notch 1 (1,500) less her basic (1,100)");
        slips.Single(p => p.EmployeeId == top.Id).Lines.Should().NotContain(l => l.ElementCode == "ACTING" && l.Amount != 0m);
    }

    [SkippableFact]
    public async Task A_post_register_blocks_engaging_into_a_missing_or_filled_post()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await Scenario.DimensionsAsync(db, companyId, ["FIN", "OPS"]);
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        DateOnly engaged = new(2026, 1, 1);

        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PR-0", "Before Register", 500m, engaged) { Department = "OPS", Grade = "G1" })).IsSuccess
            .Should().BeTrue("a company without a post register is not held to one");

        db.EstablishmentPosts.AddRange(new EstablishmentPost(companyId, "FIN", "G1", 1, 12000m), new EstablishmentPost(companyId, "OPS", "G1", 1, 12000m));
        await db.SaveChangesAsync();

        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PR-1", "First Clerk", 500m, engaged) { Department = "FIN", Grade = "G1" })).IsSuccess.Should().BeTrue();
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PR-2", "Second Clerk", 500m, engaged) { Department = "FIN", Grade = "G1" })).Error.Code
            .Should().Be("Payroll.PostFilled");
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PR-3", "Unposted", 500m, engaged) { Department = "FIN", Grade = "G7" })).Error.Code
            .Should().Be("Payroll.NoSuchPost");
        (await setup.UpdateEmployeeAsync(companyId, "PR-0", new EmployeeUpdate { Department = "FIN" })).Error.Code.Should().Be("Payroll.PostFilled");

        IReadOnlyList<PostVacancy> posts = await scope.ServiceProvider.GetRequiredService<IEstablishmentService>().VacanciesAsync(companyId);
        posts.Single(p => p.Department == "FIN").Vacant.Should().Be(0);
        posts.Single(p => p.Department == "OPS").Filled.Should().Be(1);
    }

    [Fact]
    public void The_latest_scale_in_force_sets_the_salary_and_the_top_notch()
    {
        SalaryScale[] scales =
        [
            new(1, "G1", 1, 1000m, new DateOnly(2025, 1, 1)), new(1, "G1", 2, 1100m, new DateOnly(2025, 1, 1)),
            new(1, "G1", 1, 1050m, new DateOnly(2026, 1, 1)), new(1, "G1", 2, 1150m, new DateOnly(2026, 1, 1)), new(1, "G1", 3, 1250m, new DateOnly(2026, 1, 1)),
        ];
        SalaryScale.Amount(scales, "g1", 2, new DateOnly(2025, 6, 1)).Should().Be(1100m);
        SalaryScale.Amount(scales, "G1", 2, new DateOnly(2026, 6, 1)).Should().Be(1150m);
        SalaryScale.TopNotch(scales, "G1", new DateOnly(2025, 6, 1)).Should().Be(2);
        SalaryScale.TopNotch(scales, "G1", new DateOnly(2026, 6, 1)).Should().Be(3);
        SalaryScale.Amount(scales, "G1", 1, new DateOnly(2024, 6, 1)).Should().BeNull();
    }
}

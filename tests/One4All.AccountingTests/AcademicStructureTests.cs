using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the school's academic structure (brief §4.11): years and non-overlapping terms whose dates the term's fee structures take; classes with
/// capacity that placement respects; and a once-a-year promotion moving each class up to its next grade, final-year classes staying put.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AcademicStructureTests
{
    private readonly AccountingFixture _fixture;

    public AcademicStructureTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Terms_date_the_fees_classes_hold_their_capacity_and_the_year_promotes_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer guardian = new(companyId, "AS-G", "Parent", Scenario.Usd);
        db.Customers.Add(guardian);
        await db.SaveChangesAsync();
        FeeStructure fees = new(companyId, "AS-F1", "Form 1 term 1", "F1", "2027-t1", "4000");
        db.FeeStructures.Add(fees);
        Student ann = new(companyId, "AS-1", "Ann", "F1", guardian.Id) { Stream = "BLUE" };
        Student ben = new(companyId, "AS-2", "Ben", "F1", guardian.Id) { Stream = "BLUE" };
        Student cat = new(companyId, "AS-3", "Cat", "F4", guardian.Id);
        Student dan = new(companyId, "AS-4", "Dan", "F9", guardian.Id);
        db.Students.AddRange(ann, ben, cat, dan);
        await db.SaveChangesAsync();

        IAcademicStructureService academic = scope.ServiceProvider.GetRequiredService<IAcademicStructureService>();
        (await academic.SaveYearAsync(companyId, "2027", new DateOnly(2027, 1, 10), new DateOnly(2027, 12, 5))).IsSuccess.Should().BeTrue();
        (await academic.SaveYearAsync(companyId, "2027B", new DateOnly(2027, 6, 1), new DateOnly(2028, 5, 1))).Error.Code.Should().Be("Sch.YearOverlap");
        (await academic.SaveTermAsync(companyId, "2027", "2027-T1", "Term 1", 1, new DateOnly(2027, 1, 10), new DateOnly(2027, 4, 9))).Value.Should().Be(1, "the term's fee structure takes its dates");
        (await academic.SaveTermAsync(companyId, "2027", "2027-T2", "Term 2", 2, new DateOnly(2027, 4, 1), new DateOnly(2027, 8, 6))).Error.Code.Should().Be("Sch.TermOverlap");
        (await academic.SaveTermAsync(companyId, "2027", "2027-T2", "Term 2", 2, new DateOnly(2027, 5, 4), new DateOnly(2028, 8, 6))).Error.Code.Should().Be("Sch.Term", "it runs past the year");
        (await academic.SaveTermAsync(companyId, "2027", "2027-T2", "Term 2", 2, new DateOnly(2027, 5, 4), new DateOnly(2027, 8, 6))).IsSuccess.Should().BeTrue();
        FeeStructure dated = await db.FeeStructures.AsNoTracking().SingleAsync(f => f.Id == fees.Id);
        (dated.TermStart, dated.TermEnd).Should().Be((new DateOnly(2027, 1, 10), new DateOnly(2027, 4, 9)));
        (await academic.TermOnAsync(companyId, new DateOnly(2027, 6, 1))).Value.Term.Should().Be("2027-T2");
        (await academic.TermOnAsync(companyId, new DateOnly(2027, 4, 20))).Error.Code.Should().Be("Sch.NoTerm", "a holiday");

        (await academic.SaveClassAsync(companyId, "F1", "BLUE", "Form 1 Blue", 2, "F2")).IsSuccess.Should().BeTrue();
        (await academic.SaveClassAsync(companyId, "F1", "RED", "Form 1 Red", 2, "F2")).IsSuccess.Should().BeTrue();
        (await academic.SaveClassAsync(companyId, "F4", null, "Form 4", null, null)).IsSuccess.Should().BeTrue();
        (await academic.SaveClassAsync(companyId, "F2", "BLUE", "Form 2 Blue", 40, "F2")).Error.Code.Should().Be("Sch.Class");

        (await academic.PlaceAsync(companyId, "AS-3", "F1", "BLUE")).Error.Code.Should().Be("Sch.ClassFull");
        (await academic.PlaceAsync(companyId, "AS-2", "F1", "RED")).IsSuccess.Should().BeTrue();
        (await academic.ClassRollsAsync(companyId)).Select(r => (r.Grade, r.Stream, r.Enrolled)).Should().Equal(("F1", "BLUE", 1), ("F1", "RED", 1), ("F4", null, 1));

        PromotionResult promoted = (await academic.PromoteAsync(companyId, "2027")).Value;
        (promoted.Promoted, promoted.Completed).Should().Be((2, 1));
        promoted.Unplaced.Should().Equal("AS-4");
        (await db.Students.AsNoTracking().Where(s => s.CompanyId == companyId).OrderBy(s => s.AdmissionNumber).Select(s => s.Grade).ToListAsync())
            .Should().Equal("F2", "F2", "F4", "F9");
        (await academic.PromoteAsync(companyId, "2027")).Error.Code.Should().Be("Sch.AlreadyPromoted");
    }
}

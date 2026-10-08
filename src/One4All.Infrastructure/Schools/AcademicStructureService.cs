using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Common;
using One4All.Core.Schools;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Schools;

/// <summary>Academic years, terms and classes; placement within capacity and year-end promotion.</summary>
public sealed class AcademicStructureService : IAcademicStructureService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public AcademicStructureService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SaveYearAsync(long companyId, string code, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (key.Length == 0 || endDate <= startDate)
        {
            return Result.Failure(Error.Validation("Sch.Year", "A year needs a code and must end after it starts."));
        }

        if (await _db.AcademicYears.AnyAsync(y => y.CompanyId == companyId && y.Code != key && y.StartDate <= endDate && y.EndDate >= startDate, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Sch.YearOverlap", "Academic years cannot overlap."));
        }

        AcademicYear? year = await _db.AcademicYears.FirstOrDefaultAsync(y => y.CompanyId == companyId && y.Code == key, ct).ConfigureAwait(false);
        if (year is null)
        {
            _db.AcademicYears.Add(new AcademicYear(companyId, key, startDate, endDate));
        }
        else
        {
            year.StartDate = startDate;
            year.EndDate = endDate;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> SaveTermAsync(long companyId, string yearCode, string code, string name, int sequence, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        string yearKey = (yearCode ?? string.Empty).Trim().ToUpperInvariant(), key = (code ?? string.Empty).Trim().ToUpperInvariant();
        AcademicYear? year = await _db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(y => y.CompanyId == companyId && y.Code == yearKey, ct).ConfigureAwait(false);
        if (year is null)
        {
            return Error.NotFound("Sch.YearNotFound", $"Academic year {yearKey} not found.");
        }

        if (key.Length == 0 || string.IsNullOrWhiteSpace(name) || endDate < startDate || startDate < year.StartDate || endDate > year.EndDate)
        {
            return Error.Validation("Sch.Term", $"A term needs a code and a name, and must lie within {year.Code} ({year.StartDate:yyyy-MM-dd} to {year.EndDate:yyyy-MM-dd}).");
        }

        if (await _db.SchoolTerms.AnyAsync(t => t.CompanyId == companyId && t.Code != key && t.StartDate <= endDate && t.EndDate >= startDate, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Sch.TermOverlap", "Terms cannot overlap.");
        }

        SchoolTerm? term = await _db.SchoolTerms.FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == key, ct).ConfigureAwait(false);
        if (term is null)
        {
            _db.SchoolTerms.Add(new SchoolTerm(companyId, year.Id, key, name, sequence, startDate, endDate));
        }
        else if (term.AcademicYearId != year.Id)
        {
            return Error.Conflict("Sch.TermYear", $"Term {key} belongs to another year.");
        }
        else
        {
            term.Name = name.Trim();
            term.Sequence = sequence;
            term.StartDate = startDate;
            term.EndDate = endDate;
        }

        // The fee structures for the term bill and defer over its dates (keeping whether they pro-rate).
        List<FeeStructure> structures = [.. (await _db.FeeStructures.Where(f => f.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .Where(f => string.Equals(f.Term.Trim(), key, StringComparison.OrdinalIgnoreCase))];
        foreach (FeeStructure structure in structures)
        {
            structure.SetTerm(startDate, endDate, structure.ProRata);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return structures.Count;
    }

    /// <inheritdoc />
    public async Task<Result<TermOn>> TermOnAsync(long companyId, DateOnly date, CancellationToken ct = default)
    {
        var term = await (from t in _db.SchoolTerms.AsNoTracking()
                          join y in _db.AcademicYears.AsNoTracking() on t.AcademicYearId equals y.Id
                          where t.CompanyId == companyId && t.StartDate <= date && t.EndDate >= date
                          select new { Year = y.Code, t.Code, t.Name, t.StartDate, t.EndDate }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return term is null
            ? Error.NotFound("Sch.NoTerm", $"{date:yyyy-MM-dd} is not in any term.")
            : new TermOn(term.Year, term.Code, term.Name, term.StartDate, term.EndDate);
    }

    /// <inheritdoc />
    public async Task<Result> SaveClassAsync(long companyId, string grade, string? stream, string name, int? capacity, string? nextGrade, CancellationToken ct = default)
    {
        string gradeKey = (grade ?? string.Empty).Trim().ToUpperInvariant();
        string? streamKey = string.IsNullOrWhiteSpace(stream) ? null : stream.Trim().ToUpperInvariant();
        string? next = string.IsNullOrWhiteSpace(nextGrade) ? null : nextGrade.Trim().ToUpperInvariant();
        if (gradeKey.Length == 0 || string.IsNullOrWhiteSpace(name) || capacity is <= 0 || next == gradeKey)
        {
            return Result.Failure(Error.Validation("Sch.Class", "A class needs a grade and a name, a capacity above zero if any, and a next grade other than its own."));
        }

        SchoolClass? existing = await _db.SchoolClasses.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Grade == gradeKey && c.Stream == streamKey, ct).ConfigureAwait(false);
        if (existing is null)
        {
            existing = new SchoolClass(companyId, gradeKey, streamKey, name);
            _db.SchoolClasses.Add(existing);
        }

        existing.Name = name.Trim();
        existing.Capacity = capacity;
        existing.NextGrade = next;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> PlaceAsync(long companyId, string admissionNumber, string grade, string? stream, CancellationToken ct = default)
    {
        string number = (admissionNumber ?? string.Empty).Trim().ToUpperInvariant(), gradeKey = (grade ?? string.Empty).Trim().ToUpperInvariant();
        string? streamKey = string.IsNullOrWhiteSpace(stream) ? null : stream.Trim().ToUpperInvariant();
        Student? student = await _db.Students.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.AdmissionNumber == number, ct).ConfigureAwait(false);
        if (student is not { IsEnrolled: true })
        {
            return Result.Failure(Error.NotFound("Sch.StudentNotFound", $"No enrolled student {number}."));
        }

        SchoolClass? target = await _db.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Grade == gradeKey && c.Stream == streamKey && c.IsActive, ct)
            .ConfigureAwait(false);
        if (target is null)
        {
            return Result.Failure(Error.NotFound("Sch.ClassNotFound", $"No class {gradeKey}{(streamKey is null ? string.Empty : " " + streamKey)}."));
        }

        if (target.Capacity is int capacity && await EnrolledAsync(companyId, gradeKey, streamKey, student.Id, ct).ConfigureAwait(false) >= capacity)
        {
            return Result.Failure(Error.Rule("Sch.ClassFull", $"{target.Name} is full ({capacity})."));
        }

        student.Grade = gradeKey;
        student.Stream = streamKey;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClassRoll>> ClassRollsAsync(long companyId, CancellationToken ct = default)
    {
        List<SchoolClass> classes = await _db.SchoolClasses.AsNoTracking().Where(c => c.CompanyId == companyId && c.IsActive).OrderBy(c => c.Grade).ThenBy(c => c.Stream).ToListAsync(ct)
            .ConfigureAwait(false);
        var students = await _db.Students.AsNoTracking().Where(s => s.CompanyId == companyId && s.IsEnrolled).Select(s => new { s.Grade, s.Stream }).ToListAsync(ct).ConfigureAwait(false);
        return [.. classes.Select(c => new ClassRoll(c.Grade, c.Stream, c.Name,
            students.Count(s => string.Equals(s.Grade.Trim(), c.Grade, StringComparison.OrdinalIgnoreCase)
                && (c.Stream is null || string.Equals((s.Stream ?? string.Empty).Trim(), c.Stream, StringComparison.OrdinalIgnoreCase))),
            c.Capacity, c.NextGrade))];
    }

    /// <inheritdoc />
    public Task<Result<PromotionResult>> PromoteAsync(long companyId, string yearCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => PromoteCoreAsync(companyId, yearCode, ct), ct);

    private async Task<Result<PromotionResult>> PromoteCoreAsync(long companyId, string yearCode, CancellationToken ct)
    {
        string key = (yearCode ?? string.Empty).Trim().ToUpperInvariant();
        AcademicYear? year = await _db.AcademicYears.FirstOrDefaultAsync(y => y.CompanyId == companyId && y.Code == key, ct).ConfigureAwait(false);
        if (year is null)
        {
            return Error.NotFound("Sch.YearNotFound", $"Academic year {key} not found.");
        }

        if (year.PromotedOn is not null)
        {
            return Error.Conflict("Sch.AlreadyPromoted", $"{year.Code} was promoted on {year.PromotedOn:yyyy-MM-dd}.");
        }

        List<SchoolClass> classes = await _db.SchoolClasses.AsNoTracking().Where(c => c.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        int promoted = 0, completed = 0;
        List<string> unplaced = [];
        foreach (Student student in await _db.Students.Where(s => s.CompanyId == companyId && s.IsEnrolled).OrderBy(s => s.AdmissionNumber).ToListAsync(ct).ConfigureAwait(false))
        {
            string grade = student.Grade.Trim().ToUpperInvariant();
            string? stream = string.IsNullOrWhiteSpace(student.Stream) ? null : student.Stream.Trim().ToUpperInvariant();
            SchoolClass? current = classes.FirstOrDefault(c => c.Grade == grade && c.Stream == stream) ?? classes.FirstOrDefault(c => c.Grade == grade && c.Stream is null);
            if (current is null)
            {
                unplaced.Add(student.AdmissionNumber);
            }
            else if (current.NextGrade is null)
            {
                completed++;
            }
            else
            {
                student.Grade = current.NextGrade;
                promoted++;
            }
        }

        year.PromotedOn = year.EndDate;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PromotionResult(promoted, completed, unplaced);
    }

    private async Task<int> EnrolledAsync(long companyId, string grade, string? stream, long exceptStudentId, CancellationToken ct)
        => (await _db.Students.AsNoTracking().Where(s => s.CompanyId == companyId && s.IsEnrolled && s.Id != exceptStudentId).Select(s => new { s.Grade, s.Stream }).ToListAsync(ct)
                .ConfigureAwait(false))
            .Count(s => string.Equals(s.Grade.Trim(), grade, StringComparison.OrdinalIgnoreCase)
                && string.Equals(string.IsNullOrWhiteSpace(s.Stream) ? null : s.Stream.Trim().ToUpperInvariant(), stream, StringComparison.Ordinal));
}

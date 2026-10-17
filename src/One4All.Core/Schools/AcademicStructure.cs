using One4All.Core.Common;

namespace One4All.Core.Schools;

/// <summary>An academic year (<c>sch.academic_year</c>, brief §4.11).</summary>
public sealed class AcademicYear : Entity, ICompanyScoped
{
    private AcademicYear()
    {
        Code = string.Empty;
    }

    /// <summary>Opens a year.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="code">Its code (e.g. 2026).</param>
    /// <param name="startDate">Its first day.</param>
    /// <param name="endDate">Its last day.</param>
    public AcademicYear(long companyId, string code, DateOnly startDate, DateOnly endDate)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        StartDate = startDate;
        EndDate = endDate;
    }

    /// <summary>Gets the school.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets its code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets its first day.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Gets or sets its last day.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Gets or sets when its year-end promotion ran (null: not yet).</summary>
    public DateOnly? PromotedOn { get; set; }
}

/// <summary>A term or semester of an academic year (<c>sch.term</c>).</summary>
public sealed class SchoolTerm : Entity, ICompanyScoped
{
    private SchoolTerm()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Sets a term.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="academicYearId">The year.</param>
    /// <param name="code">Its code (what fee structures name as their term, e.g. 2026-T1).</param>
    /// <param name="name">Its name.</param>
    /// <param name="sequence">Its place in the year.</param>
    /// <param name="startDate">Its first day.</param>
    /// <param name="endDate">Its last day.</param>
    public SchoolTerm(long companyId, long academicYearId, string code, string name, int sequence, DateOnly startDate, DateOnly endDate)
    {
        CompanyId = companyId;
        AcademicYearId = academicYearId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Sequence = sequence;
        StartDate = startDate;
        EndDate = endDate;
    }

    /// <summary>Gets the school.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the year.</summary>
    public long AcademicYearId { get; private set; }

    /// <summary>Gets its code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets its name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets its place in the year.</summary>
    public int Sequence { get; set; }

    /// <summary>Gets or sets its first day.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Gets or sets its last day.</summary>
    public DateOnly EndDate { get; set; }
}

/// <summary>A class: a grade / form and stream with its capacity, class teacher and the grade it moves up to (<c>sch.class</c>).</summary>
public sealed class SchoolClass : Entity, ICompanyScoped
{
    private SchoolClass()
    {
        Grade = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Sets up a class.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="grade">The grade / form.</param>
    /// <param name="stream">The stream (null: the grade has one class).</param>
    /// <param name="name">Its name.</param>
    public SchoolClass(long companyId, string grade, string? stream, string name)
    {
        CompanyId = companyId;
        Grade = grade.Trim().ToUpperInvariant();
        Stream = string.IsNullOrWhiteSpace(stream) ? null : stream.Trim().ToUpperInvariant();
        Name = name.Trim();
        IsActive = true;
    }

    /// <summary>Gets the school.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the grade / form.</summary>
    public string Grade { get; private set; }

    /// <summary>Gets the stream.</summary>
    public string? Stream { get; private set; }

    /// <summary>Gets or sets its name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the most students it takes (null: no limit).</summary>
    public int? Capacity { get; set; }

    /// <summary>Gets or sets the class teacher (an employee).</summary>
    public long? ClassTeacherEmployeeId { get; set; }

    /// <summary>Gets or sets the grade its students move up to at year end (null: they leave - the final year).</summary>
    public string? NextGrade { get; set; }

    /// <summary>Gets or sets a value indicating whether the class is in use.</summary>
    public bool IsActive { get; set; }
}

using One4All.Core.Common;

namespace One4All.Application.Abstractions.Schools;

/// <summary>A class with how full it is.</summary>
/// <param name="Grade">The grade / form.</param>
/// <param name="Stream">The stream.</param>
/// <param name="Name">The class name.</param>
/// <param name="Enrolled">Students enrolled in it.</param>
/// <param name="Capacity">Its capacity (null: no limit).</param>
/// <param name="NextGrade">Where it moves up to (null: final year).</param>
public sealed record ClassRoll(string Grade, string? Stream, string Name, int Enrolled, int? Capacity, string? NextGrade);

/// <summary>The term a date falls in.</summary>
/// <param name="Year">The academic year.</param>
/// <param name="Term">The term code.</param>
/// <param name="Name">The term name.</param>
/// <param name="StartDate">Its first day.</param>
/// <param name="EndDate">Its last day.</param>
public sealed record TermOn(string Year, string Term, string Name, DateOnly StartDate, DateOnly EndDate);

/// <summary>A year-end promotion's outcome.</summary>
/// <param name="Promoted">Students moved up a grade.</param>
/// <param name="Completed">Students in a final-year class (left enrolled for the school to withdraw when they go).</param>
/// <param name="Unplaced">Students whose grade has no class set up (left as they were).</param>
public sealed record PromotionResult(int Promoted, int Completed, IReadOnlyList<string> Unplaced);

/// <summary>
/// The school's academic structure (brief §4.11: "classes/forms/streams, terms/semesters, academic years"): years and their terms — whose dates the
/// fee structures for each term take, for pro-rata billing and deferred income — and classes with capacity, class teacher and the grade they move up to,
/// placing students within capacity and promoting the whole school at year end.
/// </summary>
public interface IAcademicStructureService
{
    /// <summary>Opens or re-dates an academic year.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="code">The year's code.</param>
    /// <param name="startDate">Its first day.</param>
    /// <param name="endDate">Its last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SaveYearAsync(long companyId, string code, DateOnly startDate, DateOnly endDate, CancellationToken ct = default);

    /// <summary>Sets a term within a year (no overlap with the year's other terms) and gives its dates to the fee structures for that term.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="yearCode">The year.</param>
    /// <param name="code">The term code (what fee structures name as their term).</param>
    /// <param name="name">Its name.</param>
    /// <param name="sequence">Its place in the year.</param>
    /// <param name="startDate">Its first day.</param>
    /// <param name="endDate">Its last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many fee structures took the dates.</returns>
    Task<Result<int>> SaveTermAsync(long companyId, string yearCode, string code, string name, int sequence, DateOnly startDate, DateOnly endDate, CancellationToken ct = default);

    /// <summary>The term a date falls in.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="date">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The term, or why there is none (a holiday).</returns>
    Task<Result<TermOn>> TermOnAsync(long companyId, DateOnly date, CancellationToken ct = default);

    /// <summary>Sets up or changes a class.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="grade">The grade / form.</param>
    /// <param name="stream">The stream (null: one class).</param>
    /// <param name="name">Its name.</param>
    /// <param name="capacity">Its capacity (null: no limit).</param>
    /// <param name="nextGrade">The grade it moves up to (null: final year).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SaveClassAsync(long companyId, string grade, string? stream, string name, int? capacity, string? nextGrade, CancellationToken ct = default);

    /// <summary>Places a student in a class, within its capacity.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="admissionNumber">The student.</param>
    /// <param name="grade">The grade.</param>
    /// <param name="stream">The stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> PlaceAsync(long companyId, string admissionNumber, string grade, string? stream, CancellationToken ct = default);

    /// <summary>The classes with how full they are.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The classes.</returns>
    Task<IReadOnlyList<ClassRoll>> ClassRollsAsync(long companyId, CancellationToken ct = default);

    /// <summary>Moves every enrolled student up to their class's next grade (same stream) at the end of a year, once per year.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="yearCode">The year ending.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What happened.</returns>
    Task<Result<PromotionResult>> PromoteAsync(long companyId, string yearCode, CancellationToken ct = default);
}

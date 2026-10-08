using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>An increment run's outcome.</summary>
/// <param name="Moved">Employees moved up a notch (number, from notch, to notch, new basic).</param>
/// <param name="AtTop">Employees already at the top of their scale.</param>
public sealed record IncrementResult(IReadOnlyList<(string Employee, int From, int To, decimal Basic)> Moved, IReadOnlyList<string> AtTop);

/// <summary>A grade's posts: approved, filled and vacant.</summary>
/// <param name="Department">The department.</param>
/// <param name="Grade">The grade.</param>
/// <param name="Approved">Approved posts.</param>
/// <param name="Filled">Posts filled by active staff.</param>
public sealed record PostVacancy(string Department, string Grade, int Approved, int Filled)
{
    /// <summary>Gets the vacant posts (negative when over-filled).</summary>
    public int Vacant => Approved - Filled;
}

/// <summary>
/// Salary scales, annual increments, acting appointments and the post register (brief §4.15.1: "salary scales with notches and annual increment
/// rules… acting appointments… posts filled vs vacant").
/// </summary>
public interface IEstablishmentService
{
    /// <summary>Sets a grade's salary at a notch from a date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="grade">The grade.</param>
    /// <param name="notch">The notch.</param>
    /// <param name="monthlyAmount">The monthly salary.</param>
    /// <param name="effectiveFrom">When it applies from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetScaleAsync(long companyId, string grade, int notch, decimal monthlyAmount, DateOnly effectiveFrom, CancellationToken ct = default);

    /// <summary>
    /// The annual increment: every active employee on a scaled grade moves up a notch (to the scale's salary) unless at the top or already
    /// incremented within the last year.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="effectiveDate">The increment date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Who moved and who is at the top.</returns>
    Task<Result<IncrementResult>> RunIncrementsAsync(long companyId, DateOnly effectiveDate, CancellationToken ct = default);

    /// <summary>Appoints an employee to act in a grade for a time.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeNumber">The employee.</param>
    /// <param name="grade">The grade acted in.</param>
    /// <param name="notch">The notch.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day (null: until ended).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The appointment id.</returns>
    Task<Result<long>> AppointActingAsync(long companyId, string employeeNumber, string grade, int notch, DateOnly from, DateOnly? to, CancellationToken ct = default);

    /// <summary>The post register: approved, filled and vacant posts by department and grade.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posts.</returns>
    Task<IReadOnlyList<PostVacancy>> VacanciesAsync(long companyId, CancellationToken ct = default);
}

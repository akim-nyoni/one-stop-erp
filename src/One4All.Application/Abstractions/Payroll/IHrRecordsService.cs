using One4All.Core.Common;
using One4All.Core.Payroll;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>Employee HR records (brief §14 O PR21, PR47, PR55–PR57): establishment, documents, discipline, training, appraisals and court orders.</summary>
public interface IHrRecordsService
{
    /// <summary>Sets the approved establishment for a department and grade (replacing any previous figure).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="department">The department.</param>
    /// <param name="grade">The grade.</param>
    /// <param name="approvedPosts">The approved posts (≥ 0).</param>
    /// <param name="annualCostPerPost">The annual cost of a post.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> SetEstablishmentAsync(long companyId, string department, string grade, int approvedPosts, decimal annualCostPerPost, CancellationToken ct = default);

    /// <summary>Files a document on an employee's record.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="documentType">The type.</param>
    /// <param name="reference">The number.</param>
    /// <param name="expiresOn">When it expires.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document id, or an error.</returns>
    Task<Result<long>> FileDocumentAsync(long companyId, long employeeId, string documentType, string? reference, DateOnly? expiresOn, CancellationToken ct = default);

    /// <summary>Records a disciplinary sanction; warnings lapse by default after 6 (verbal) or 12 (written / final) months.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="date">The hearing date.</param>
    /// <param name="offence">The offence.</param>
    /// <param name="sanction">The sanction.</param>
    /// <param name="issuedByUserId">Who issued it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The record id, or an error.</returns>
    Task<Result<long>> RecordDisciplinaryAsync(long companyId, long employeeId, DateOnly date, string offence, DisciplinarySanction sanction, long? issuedByUserId, CancellationToken ct = default);

    /// <summary>Records training completed or a qualification gained.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="course">The course.</param>
    /// <param name="completedOn">When completed.</param>
    /// <param name="expiresOn">When it must be renewed.</param>
    /// <param name="provider">The provider.</param>
    /// <param name="cost">The cost.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The record id, or an error.</returns>
    Task<Result<long>> RecordTrainingAsync(long companyId, long employeeId, string course, DateOnly completedOn, DateOnly? expiresOn, string? provider, decimal cost, CancellationToken ct = default);

    /// <summary>Records an appraisal and when the next is due.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="date">When held.</param>
    /// <param name="rating">The rating (1–5).</param>
    /// <param name="nextDue">When the next is due (after the date).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The record id, or an error.</returns>
    Task<Result<long>> RecordAppraisalAsync(long companyId, long employeeId, DateOnly date, int rating, DateOnly nextDue, CancellationToken ct = default);

    /// <summary>Registers a garnishee / court order collected through a deduction pay element.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="reference">The order reference (unique per company).</param>
    /// <param name="creditor">Who is paid.</param>
    /// <param name="priority">The priority (1 first).</param>
    /// <param name="instalment">The amount per period.</param>
    /// <param name="totalAmount">The total ordered.</param>
    /// <param name="elementCode">The deduction element it is collected through.</param>
    /// <param name="issuedOn">When issued.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The order id, or an error.</returns>
    Task<Result<long>> RegisterGarnisheeAsync(long companyId, long employeeId, string reference, string creditor, int priority, decimal instalment, decimal totalAmount, string elementCode, DateOnly issuedOn,
        CancellationToken ct = default);
}

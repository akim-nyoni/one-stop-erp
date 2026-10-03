using One4All.Core.Common;

namespace One4All.Application.Abstractions.Schools;

/// <summary>The result of billing one student for a term.</summary>
/// <param name="StudentId">The student.</param>
/// <param name="GrossFees">The compulsory fee total before discount.</param>
/// <param name="Discount">The bursary/scholarship discount applied.</param>
/// <param name="NetFees">The net billed to the guardian.</param>
/// <param name="OpenItemId">The AR open item raised, or null when nothing was billable.</param>
/// <param name="JournalNumber">The AR journal number, or null when nothing was billable.</param>
public readonly record struct StudentBillResult(long StudentId, decimal GrossFees, decimal Discount, decimal NetFees, long? OpenItemId, string? JournalNumber);

/// <summary>The result of a term billing run.</summary>
/// <param name="Bills">The per-student results.</param>
/// <param name="TotalNetBilled">The total net billed across all students.</param>
/// <param name="AlreadyBilled">Students skipped because their invoice number for this run is already on the receivables ledger.</param>
public readonly record struct TermBillResult(IReadOnlyList<StudentBillResult> Bills, decimal TotalNetBilled, int AlreadyBilled = 0)
{
    /// <summary>The number of students billed.</summary>
    public int Count => Bills.Count;
}

/// <summary>
/// School fee billing (brief §4.11). Computes each student's term fees from a fee structure less any
/// bursary/scholarship, and raises the net on the guardian/sponsor account through the central AR ledger.
/// </summary>
public interface ISchoolBillingService
{
    /// <summary>
    /// Bills one student for a term against a fee structure. Refuses an invoice number already on the receivables
    /// ledger (<c>School.AlreadyBilled</c>), so the same bill cannot be raised twice.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="studentId">The student.</param>
    /// <param name="feeStructureId">The fee structure to apply.</param>
    /// <param name="invoiceNumber">The invoice number.</param>
    /// <param name="date">The billing date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bill result.</returns>
    Task<Result<StudentBillResult>> BillStudentAsync(long companyId, long studentId, long feeStructureId, string invoiceNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Runs a term billing for every enrolled student in the fee structure's grade, raising one invoice
    /// per student (invoice numbers are the prefix plus the admission number). The prefix identifies the term's
    /// run (e.g. <c>T1-2026-</c>) and is required: a student whose number is already on the receivables ledger is
    /// skipped and counted in <see cref="TermBillResult.AlreadyBilled"/>, so a repeated or resumed run never bills
    /// anyone twice.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="feeStructureId">The fee structure to apply.</param>
    /// <param name="invoiceNumberPrefix">The invoice-number prefix.</param>
    /// <param name="date">The billing date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run result.</returns>
    Task<Result<TermBillResult>> BillTermAsync(long companyId, long feeStructureId, string invoiceNumberPrefix, DateOnly date, CancellationToken ct = default);
}

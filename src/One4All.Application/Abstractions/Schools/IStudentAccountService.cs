using One4All.Core.Common;

namespace One4All.Application.Abstractions.Schools;

/// <summary>One fee document on a student's account.</summary>
/// <param name="Date">The document date.</param>
/// <param name="DocumentNumber">The document.</param>
/// <param name="Type">Invoice, credit note or debit note.</param>
/// <param name="Description">What it is for (the term and any note).</param>
/// <param name="Payer">The account it was charged to (the guardian or the sponsor).</param>
/// <param name="Amount">The amount (credits negative).</param>
/// <param name="Paid">How much of it has been settled.</param>
/// <param name="Outstanding">What is still owed on it.</param>
public sealed record StudentAccountLine(DateOnly Date, string DocumentNumber, string Type, string Description, string Payer, decimal Amount, decimal Paid, decimal Outstanding);

/// <summary>A student's own account within the family's (brief §4.17 student sub-ledger): every fee document raised for the student, whoever pays.</summary>
/// <param name="AdmissionNumber">The student.</param>
/// <param name="StudentName">The name.</param>
/// <param name="Lines">The fee documents, oldest first.</param>
public sealed record StudentAccount(string AdmissionNumber, string StudentName, IReadOnlyList<StudentAccountLine> Lines)
{
    /// <summary>The fees billed less credits.</summary>
    public decimal Billed => Lines.Sum(l => l.Amount);

    /// <summary>What has been settled.</summary>
    public decimal Paid => Lines.Sum(l => l.Paid);

    /// <summary>What the student's fees still owe.</summary>
    public decimal Balance => Lines.Sum(l => l.Outstanding);
}

/// <summary>
/// The student sub-ledger: fees are charged to the guardian's (or sponsor's) account, which may carry several children; this picks out one student's
/// documents with what has been paid on each and what is still owed.
/// </summary>
public interface IStudentAccountService
{
    /// <summary>A student's account.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="admissionNumber">The student.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account, or why not.</returns>
    Task<Result<StudentAccount>> AccountAsync(long companyId, string admissionNumber, CancellationToken ct = default);
}

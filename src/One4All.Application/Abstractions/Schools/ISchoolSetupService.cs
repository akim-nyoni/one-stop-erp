using One4All.Core.Common;

namespace One4All.Application.Abstractions.Schools;

/// <summary>
/// Setting up a school for billing (brief §4.11): fee structures per grade and term with their items, students with their guardian (the account
/// billed), hostels and transport routes, and putting students into them.
/// </summary>
public interface ISchoolSetupService
{
    /// <summary>Creates a fee structure for a grade and term (or renames an existing one).</summary>
    /// <returns>The structure's id.</returns>
    Task<Result<long>> SaveFeeStructureAsync(long companyId, string code, string name, string grade, string term, string revenueAccountCode, CancellationToken ct = default);

    /// <summary>Adds a fee item (tuition, levy, optional activity …) to a structure; optional items are billed only to students who take them.</summary>
    /// <returns>The item's id.</returns>
    Task<Result<long>> AddFeeItemAsync(long companyId, string structureCode, string description, decimal amount, bool isCompulsory, string? revenueAccountCode, CancellationToken ct = default);

    /// <summary>Admits a student under a guardian's account (a customer).</summary>
    /// <returns>The student's id.</returns>
    Task<Result<long>> AdmitStudentAsync(long companyId, string admissionNumber, string fullName, string grade, string guardianCustomerCode, DateOnly admittedOn, CancellationToken ct = default)
        => AdmitStudentAsync(companyId, admissionNumber, fullName, grade, guardianCustomerCode, admittedOn, Core.Payroll.Gender.Unspecified, ct);

    /// <summary>Admits a student, recording their gender (for the headcount split).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="admissionNumber">The admission number.</param>
    /// <param name="fullName">The student's name.</param>
    /// <param name="grade">The grade.</param>
    /// <param name="guardianCustomerCode">The guardian's customer code.</param>
    /// <param name="admittedOn">The admission date.</param>
    /// <param name="gender">The student's gender.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The student's id.</returns>
    Task<Result<long>> AdmitStudentAsync(long companyId, string admissionNumber, string fullName, string grade, string guardianCustomerCode, DateOnly admittedOn, Core.Payroll.Gender gender, CancellationToken ct = default);

    /// <summary>Creates or updates a hostel (boarding house) with its capacity and fee per term.</summary>
    Task<Result<long>> SaveHostelAsync(long companyId, string code, string name, int capacity, decimal feePerTerm, string revenueAccountCode, CancellationToken ct = default);

    /// <summary>Creates or updates a transport route with its capacity and fee per term.</summary>
    Task<Result<long>> SaveRouteAsync(long companyId, string code, string name, int capacity, decimal feePerTerm, string revenueAccountCode, CancellationToken ct = default);

    /// <summary>Puts a student into a hostel and/or onto a route (blank codes take them out), within capacity.</summary>
    Task<Result> PlaceStudentAsync(long companyId, string admissionNumber, string? hostelCode, string? routeCode, CancellationToken ct = default);

    /// <summary>Names the sponsor (a customer account) billed for a student's fees instead of the guardian (brief §4.11 sponsor-paid accounts); blank takes the sponsor off.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="admissionNumber">The student.</param>
    /// <param name="sponsorCustomerCode">The sponsor's customer code, or null/blank for none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> SetSponsorAsync(long companyId, string admissionNumber, string? sponsorCustomerCode, CancellationToken ct = default);
}

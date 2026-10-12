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
    Task<Result<long>> AdmitStudentAsync(long companyId, string admissionNumber, string fullName, string grade, string guardianCustomerCode, DateOnly admittedOn, CancellationToken ct = default);

    /// <summary>Creates or updates a hostel (boarding house) with its capacity and fee per term.</summary>
    Task<Result<long>> SaveHostelAsync(long companyId, string code, string name, int capacity, decimal feePerTerm, string revenueAccountCode, CancellationToken ct = default);

    /// <summary>Creates or updates a transport route with its capacity and fee per term.</summary>
    Task<Result<long>> SaveRouteAsync(long companyId, string code, string name, int capacity, decimal feePerTerm, string revenueAccountCode, CancellationToken ct = default);

    /// <summary>Puts a student into a hostel and/or onto a route (blank codes take them out), within capacity.</summary>
    Task<Result> PlaceStudentAsync(long companyId, string admissionNumber, string? hostelCode, string? routeCode, CancellationToken ct = default);
}

using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Common;
using One4All.Core.Schools;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Schools;

/// <summary>Sets up fee structures, students, hostels and routes.</summary>
public sealed class SchoolSetupService : ISchoolSetupService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public SchoolSetupService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> SaveFeeStructureAsync(long companyId, string code, string name, string grade, string term, string revenueAccountCode, CancellationToken ct = default)
    {
        string key = Code(code);
        if (key.Length == 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(grade) || string.IsNullOrWhiteSpace(term))
        {
            return Error.Validation("School.FeeStructure", "A fee structure needs a code, a name, the grade and the term.");
        }

        if (await _db.FeeStructures.FirstOrDefaultAsync(f => f.CompanyId == companyId && f.Code == key, ct).ConfigureAwait(false) is FeeStructure existing)
        {
            existing.Name = name.Trim();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return existing.Id;
        }

        if (await AccountMissingAsync(companyId, revenueAccountCode, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        FeeStructure structure = new(companyId, key, name.Trim(), grade.Trim(), term.Trim(), revenueAccountCode.Trim().ToUpperInvariant());
        _db.FeeStructures.Add(structure);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return structure.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> AddFeeItemAsync(long companyId, string structureCode, string description, decimal amount, bool isCompulsory, string? revenueAccountCode, CancellationToken ct = default)
    {
        string key = Code(structureCode);
        FeeStructure? structure = await _db.FeeStructures.Include(f => f.Items).FirstOrDefaultAsync(f => f.CompanyId == companyId && f.Code == key, ct).ConfigureAwait(false);
        if (structure is null)
        {
            return Error.NotFound("School.FeeStructureNotFound", $"Fee structure {key} not found.");
        }

        if (string.IsNullOrWhiteSpace(description) || amount <= 0m)
        {
            return Error.Validation("School.FeeItem", "A fee item needs a description and a positive amount.");
        }

        if (!string.IsNullOrWhiteSpace(revenueAccountCode) && await AccountMissingAsync(companyId, revenueAccountCode, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        FeeItem item = structure.AddItem(description.Trim(), amount, isCompulsory, string.IsNullOrWhiteSpace(revenueAccountCode) ? null : revenueAccountCode.Trim().ToUpperInvariant());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return item.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> AdmitStudentAsync(long companyId, string admissionNumber, string fullName, string grade, string guardianCustomerCode, DateOnly admittedOn, Core.Payroll.Gender gender, CancellationToken ct = default)
    {
        string number = Code(admissionNumber);
        if (number.Length == 0 || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(grade))
        {
            return Error.Validation("School.Student", "A student needs an admission number, a name and a grade.");
        }

        if (await _db.Students.AnyAsync(s => s.CompanyId == companyId && s.AdmissionNumber == number, ct).ConfigureAwait(false))
        {
            return Error.Conflict("School.StudentExists", $"Student {number} is already admitted.");
        }

        string guardian = Code(guardianCustomerCode);
        long? guardianId = await _db.Customers.Where(c => c.CompanyId == companyId && c.Code == guardian).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (guardianId is null)
        {
            return Error.NotFound("School.GuardianNotFound", $"Guardian account {guardian} not found.");
        }

        Student student = new(companyId, number, fullName.Trim(), grade.Trim(), guardianId.Value) { AdmittedOn = admittedOn, Gender = gender };
        _db.Students.Add(student);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return student.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> SaveHostelAsync(long companyId, string code, string name, int capacity, decimal feePerTerm, string revenueAccountCode, CancellationToken ct = default)
    {
        string key = Code(code);
        if (key.Length == 0 || string.IsNullOrWhiteSpace(name) || capacity < 0 || feePerTerm < 0m)
        {
            return Error.Validation("School.Hostel", "A hostel needs a code, a name, a capacity and a fee that are not negative.");
        }

        if (await _db.Hostels.FirstOrDefaultAsync(h => h.CompanyId == companyId && h.Code == key, ct).ConfigureAwait(false) is Hostel existing)
        {
            (existing.Capacity, existing.FeePerTerm) = (capacity, feePerTerm);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return existing.Id;
        }

        if (await AccountMissingAsync(companyId, revenueAccountCode, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        Hostel hostel = new(companyId, key, name.Trim(), capacity, feePerTerm, revenueAccountCode.Trim().ToUpperInvariant());
        _db.Hostels.Add(hostel);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return hostel.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> SaveRouteAsync(long companyId, string code, string name, int capacity, decimal feePerTerm, string revenueAccountCode, CancellationToken ct = default)
    {
        string key = Code(code);
        if (key.Length == 0 || string.IsNullOrWhiteSpace(name) || capacity < 0 || feePerTerm < 0m)
        {
            return Error.Validation("School.Route", "A route needs a code, a name, a capacity and a fee that are not negative.");
        }

        if (await _db.TransportRoutes.FirstOrDefaultAsync(r => r.CompanyId == companyId && r.Code == key, ct).ConfigureAwait(false) is TransportRoute existing)
        {
            (existing.Capacity, existing.FeePerTerm) = (capacity, feePerTerm);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return existing.Id;
        }

        if (await AccountMissingAsync(companyId, revenueAccountCode, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        TransportRoute route = new(companyId, key, name.Trim(), capacity, feePerTerm, revenueAccountCode.Trim().ToUpperInvariant());
        _db.TransportRoutes.Add(route);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return route.Id;
    }

    /// <inheritdoc />
    public async Task<Result> SetSponsorAsync(long companyId, string admissionNumber, string? sponsorCustomerCode, CancellationToken ct = default)
    {
        string number = Code(admissionNumber);
        if (await _db.Students.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.AdmissionNumber == number, ct).ConfigureAwait(false) is not Student student)
        {
            return Result.Failure(Error.NotFound("School.StudentNotFound", $"Student {number} not found."));
        }

        if (string.IsNullOrWhiteSpace(sponsorCustomerCode))
        {
            student.SponsorCustomerId = null;
        }
        else
        {
            string sponsor = Code(sponsorCustomerCode);
            long? sponsorId = await _db.Customers.Where(c => c.CompanyId == companyId && c.Code == sponsor).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (sponsorId is null)
            {
                return Result.Failure(Error.NotFound("School.SponsorNotFound", $"Sponsor account {sponsor} not found."));
            }

            student.SponsorCustomerId = sponsorId;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> PlaceStudentAsync(long companyId, string admissionNumber, string? hostelCode, string? routeCode, CancellationToken ct = default)
    {
        string number = Code(admissionNumber);
        if (await _db.Students.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.AdmissionNumber == number, ct).ConfigureAwait(false) is not Student student)
        {
            return Result.Failure(Error.NotFound("School.StudentNotFound", $"Student {number} not found."));
        }

        long? hostelId = null;
        if (!string.IsNullOrWhiteSpace(hostelCode))
        {
            string key = Code(hostelCode);
            if (await _db.Hostels.FirstOrDefaultAsync(h => h.CompanyId == companyId && h.Code == key, ct).ConfigureAwait(false) is not Hostel hostel)
            {
                return Result.Failure(Error.NotFound("School.HostelNotFound", $"Hostel {key} not found."));
            }

            if (student.HostelId != hostel.Id && await _db.Students.CountAsync(s => s.HostelId == hostel.Id && s.IsEnrolled, ct).ConfigureAwait(false) >= hostel.Capacity)
            {
                return Result.Failure(Error.Rule("School.HostelFull", $"Hostel {hostel.Code} is full ({hostel.Capacity})."));
            }

            hostelId = hostel.Id;
        }

        long? routeId = null;
        if (!string.IsNullOrWhiteSpace(routeCode))
        {
            string key = Code(routeCode);
            if (await _db.TransportRoutes.FirstOrDefaultAsync(r => r.CompanyId == companyId && r.Code == key, ct).ConfigureAwait(false) is not TransportRoute route)
            {
                return Result.Failure(Error.NotFound("School.RouteNotFound", $"Route {key} not found."));
            }

            if (student.TransportRouteId != route.Id && await _db.Students.CountAsync(s => s.TransportRouteId == route.Id && s.IsEnrolled, ct).ConfigureAwait(false) >= route.Capacity)
            {
                return Result.Failure(Error.Rule("School.RouteFull", $"Route {route.Code} is full ({route.Capacity})."));
            }

            routeId = route.Id;
        }

        (student.HostelId, student.TransportRouteId, student.IsBoarder) = (hostelId, routeId, hostelId is not null);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private static string Code(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private async Task<Error?> AccountMissingAsync(long companyId, string? accountCode, CancellationToken ct)
    {
        string account = Code(accountCode);
        return account.Length > 0 && await _db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == account, ct).ConfigureAwait(false)
            ? null : Error.NotFound("School.AccountNotFound", $"Account {account} does not exist.");
    }
}

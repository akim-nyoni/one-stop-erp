using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Server;

/// <summary>A student admitted through the API.</summary>
/// <param name="CompanyId">The school.</param>
/// <param name="AdmissionNumber">The admission number.</param>
/// <param name="FullName">The student's name.</param>
/// <param name="Grade">The grade admitted to.</param>
/// <param name="GuardianCustomerCode">The fee-paying guardian's customer account.</param>
/// <param name="AdmittedOn">The admission date.</param>
public sealed record AdmitStudentApiRequest(long CompanyId, string AdmissionNumber, string FullName, string Grade, string GuardianCustomerCode, DateOnly AdmittedOn);

/// <summary>
/// The REST API for schools (brief §4.17, §11): students (list, read, admit, place in a class), class rolls, the term a date falls in, billing a
/// term's fees, topping up a student wallet and the bursaries about to end, each through the application service the desktop uses.
/// </summary>
internal static class SchoolsApiEndpoints
{
    private const int PageSize = 200;

    /// <summary>Maps the endpoints onto the API group.</summary>
    /// <param name="api">The API route group (authorisation is required on every route).</param>
    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet("/api/schools/students", async (long companyId, string? grade, int? page, One4AllDbContext db, CancellationToken ct) =>
        {
            string? wanted = string.IsNullOrWhiteSpace(grade) ? null : grade.Trim().ToUpperInvariant();
            return Results.Ok(await (from s in db.Students.AsNoTracking()
                                     join g in db.Customers.AsNoTracking() on s.GuardianCustomerId equals g.Id
                                     where s.CompanyId == companyId && (wanted == null || s.Grade.ToUpper() == wanted)
                                     orderby s.AdmissionNumber
                                     select new { s.Id, s.AdmissionNumber, s.FullName, s.Grade, s.Stream, Guardian = g.Code, s.LeftOn })
                .Skip(Math.Max(0, (page ?? 1) - 1) * PageSize).Take(PageSize).ToListAsync(ct).ConfigureAwait(false));
        })
        .WithName("ListStudents");

        api.MapGet("/api/schools/students/{admissionNumber}", async (string admissionNumber, long companyId, One4AllDbContext db, CancellationToken ct) =>
        {
            string key = admissionNumber.Trim().ToUpperInvariant();
            var student = await (from s in db.Students.AsNoTracking()
                                 join g in db.Customers.AsNoTracking() on s.GuardianCustomerId equals g.Id
                                 where s.CompanyId == companyId && s.AdmissionNumber.ToUpper() == key
                                 select new { s.Id, s.AdmissionNumber, s.FullName, s.Grade, s.Stream, Guardian = g.Code, GuardianName = g.Name, s.LeftOn })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            return student is null ? Results.Json(new { error = $"No student {key}." }, statusCode: StatusCodes.Status404NotFound) : Results.Ok(student);
        })
        .WithName("GetStudent");

        api.MapGet("/api/schools/students/{admissionNumber}/account", async (string admissionNumber, long companyId, IStudentAccountService accounts, CancellationToken ct) =>
            Value(await accounts.AccountAsync(companyId, admissionNumber, ct).ConfigureAwait(false)))
        .WithName("StudentAccount");

        api.MapPost("/api/schools/students", async (AdmitStudentApiRequest request, ISchoolSetupService setup, CancellationToken ct) =>
            Value(await setup.AdmitStudentAsync(request.CompanyId, request.AdmissionNumber, request.FullName, request.Grade, request.GuardianCustomerCode, request.AdmittedOn, ct)
                .ConfigureAwait(false)))
        .WithName("AdmitStudent");

        api.MapPost("/api/schools/students/{admissionNumber}/place", async (string admissionNumber, long companyId, string grade, string? stream, IAcademicStructureService academic,
            CancellationToken ct) => Action(await academic.PlaceAsync(companyId, admissionNumber, grade, stream, ct).ConfigureAwait(false)))
        .WithName("PlaceStudent");

        api.MapGet("/api/schools/classes", async (long companyId, IAcademicStructureService academic, CancellationToken ct) =>
            Results.Ok(await academic.ClassRollsAsync(companyId, ct).ConfigureAwait(false)))
        .WithName("ClassRolls");

        api.MapGet("/api/schools/terms/on", async (long companyId, DateOnly date, IAcademicStructureService academic, CancellationToken ct) =>
            Value(await academic.TermOnAsync(companyId, date, ct).ConfigureAwait(false)))
        .WithName("TermOn");

        api.MapPost("/api/schools/fee-structures/{feeStructureId:long}/bill-term", async (long feeStructureId, long companyId, string invoicePrefix, DateOnly date, ISchoolBillingService billing,
            CancellationToken ct) => Value(await billing.BillTermAsync(companyId, feeStructureId, invoicePrefix, date, ct).ConfigureAwait(false)))
        .WithName("BillTerm");

        api.MapPost("/api/schools/students/{studentId:long}/wallet/top-up", async (long studentId, long companyId, DateOnly date, decimal amount, string bankAccountCode, string walletAccountCode,
            IStudentWalletService wallets, CancellationToken ct) =>
            Value(await wallets.TopUpAsync(companyId, studentId, date, amount, bankAccountCode, walletAccountCode, ct).ConfigureAwait(false)))
        .WithName("TopUpStudentWallet");

        api.MapGet("/api/schools/bursaries/expiring", async (long companyId, DateOnly asAt, int? withinDays, ISchoolBursaryService bursaries, CancellationToken ct) =>
            Results.Ok(await bursaries.ExpiringAsync(companyId, asAt, withinDays ?? 30, ct).ConfigureAwait(false)))
        .WithName("ExpiringBursaries");
    }

    private static IResult Value<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        int status = result.Error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Rule or ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(new { error = result.Error.Message, code = result.Error.Code }, statusCode: status);
    }

    private static IResult Action(Result result) => result.IsSuccess ? Results.Ok(new { ok = true }) : Value(Result<bool>.Failure(result.Error));
}

using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>
/// HR expiry alerts (brief §4.15.1 "work permit … expiry alert", "contract start and end with expiry alerts", probation confirmation): what lapses
/// for active employees within a number of days — work permits, contracts, probation, filed documents and training certificates.
/// </summary>
public interface IHrAlertService
{
    /// <summary>What expires (or has expired) for active employees up to so many days after a date, soonest first.</summary>
    Task<IReadOnlyList<HrExpiry>> ExpiringAsync(long companyId, DateOnly asAt, int withinDays, CancellationToken ct = default);

    /// <summary>Sends each HR user (anyone whose role covers employee records) one notification listing what expires.</summary>
    /// <returns>How many users were notified.</returns>
    Task<Result<int>> NotifyAsync(long companyId, DateOnly asAt, int withinDays, CancellationToken ct = default);
}

/// <summary>Something about an employee that lapses.</summary>
/// <param name="EmployeeNumber">The employee.</param>
/// <param name="EmployeeName">Their name.</param>
/// <param name="What">What lapses (work permit, contract end, probation, a document or a training certificate).</param>
/// <param name="ExpiresOn">When.</param>
/// <param name="DaysLeft">Days from the date (negative: already lapsed).</param>
public sealed record HrExpiry(string EmployeeNumber, string EmployeeName, string What, DateOnly ExpiresOn, int DaysLeft);

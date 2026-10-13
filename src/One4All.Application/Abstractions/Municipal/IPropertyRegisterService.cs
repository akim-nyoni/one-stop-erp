using One4All.Core.Common;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>The property register's location details and the meter register's type, size and location (brief §4.9).</summary>
public interface IPropertyRegisterService
{
    /// <summary>Sets a stand's suburb and GPS position (null leaves a value; latitude −90..90, longitude −180..180, both or neither).</summary>
    Task<Result> SetLocationAsync(long companyId, string standNumber, string? suburb, decimal? latitude, decimal? longitude, CancellationToken ct = default);

    /// <summary>Sets a meter's type, size and where it is (null leaves a value).</summary>
    Task<Result> SetMeterDetailsAsync(long companyId, string serial, string? meterType, int? sizeMm, string? location, CancellationToken ct = default);

    /// <summary>Each meter on a stand with its consumption month by month for the months to a date (accepted readings, by reading month).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="standNumber">The stand.</param>
    /// <param name="asAt">The last month shown is the month of this date.</param>
    /// <param name="months">How many months to show.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The meters' consumption, or why there is none.</returns>
    Task<Result<IReadOnlyList<Application.Municipal.MeterConsumption>>> ConsumptionAsync(long companyId, string standNumber, DateOnly asAt, int months = 12, CancellationToken ct = default);
}

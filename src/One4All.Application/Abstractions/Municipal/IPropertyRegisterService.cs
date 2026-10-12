using One4All.Core.Common;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>The property register's location details and the meter register's type, size and location (brief §4.9).</summary>
public interface IPropertyRegisterService
{
    /// <summary>Sets a stand's suburb and GPS position (null leaves a value; latitude −90..90, longitude −180..180, both or neither).</summary>
    Task<Result> SetLocationAsync(long companyId, string standNumber, string? suburb, decimal? latitude, decimal? longitude, CancellationToken ct = default);

    /// <summary>Sets a meter's type, size and where it is (null leaves a value).</summary>
    Task<Result> SetMeterDetailsAsync(long companyId, string serial, string? meterType, int? sizeMm, string? location, CancellationToken ct = default);
}

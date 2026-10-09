namespace One4All.Application.Abstractions.Licensing;

/// <summary>One licence limit and what the installation uses of it.</summary>
/// <param name="Key">The limit (e.g. companies, students).</param>
/// <param name="Module">The module it belongs to (null: the whole licence).</param>
/// <param name="Used">How many are in use.</param>
/// <param name="Limit">The limit (null: unlimited).</param>
public sealed record LicenceUsage(string Key, string? Module, int Used, int? Limit)
{
    /// <summary>Gets the share of the limit used, in percent (null: unlimited).</summary>
    public decimal? PercentUsed => Limit is int limit and > 0 ? Math.Round(Used * 100m / limit, 1) : null;
}

/// <summary>What the installation uses against each licence limit (brief §11.1: "usage against limits on the status screen").</summary>
public interface ILicenceUsageService
{
    /// <summary>The usage of every counted limit, across all companies.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The usage.</returns>
    Task<IReadOnlyList<LicenceUsage>> UsageAsync(CancellationToken ct = default);
}

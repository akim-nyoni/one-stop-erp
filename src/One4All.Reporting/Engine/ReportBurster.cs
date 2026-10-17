namespace One4All.Reporting.Engine;

/// <summary>
/// Splits one report run by a key field into many documents - one per key (brief §13.3 bursting). This is
/// how bulk statements and bills are produced: burst the dataset by customer/property/student, render each
/// slice, and deliver each to the right recipient.
/// </summary>
public static class ReportBurster
{
    /// <summary>Bursts the input by <paramref name="keyField"/>, building a render model per distinct key.</summary>
    /// <param name="engine">The report engine.</param>
    /// <param name="input">The full report input.</param>
    /// <param name="keyField">The field to split on (e.g. <c>CustomerId</c>).</param>
    /// <returns>A sequence of (key, render model) pairs, one per distinct key value.</returns>
    public static IEnumerable<(string Key, RenderModel Model)> Burst(ReportEngine engine, ReportInput input, string keyField)
    {
        foreach (IGrouping<string, IReadOnlyDictionary<string, object?>> group in input.Rows
            .GroupBy(r => r.GetValueOrDefault(keyField)?.ToString() ?? string.Empty))
        {
            ReportInput slice = new()
            {
                Definition = input.Definition,
                Rows = group.ToList(),
                Parameters = input.Parameters,
                UserName = input.UserName,
                CompanyName = input.CompanyName,
                PrintDate = input.PrintDate,
                ToBase = input.ToBase,
            };

            yield return (group.Key, engine.Build(slice));
        }
    }
}

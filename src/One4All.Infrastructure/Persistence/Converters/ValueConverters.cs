using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using One4All.Core.Accounting;

namespace One4All.Infrastructure.Persistence.Converters;

/// <summary>Converts a <see cref="CurrencyCode"/> value object to/from its three-letter string form.</summary>
public sealed class CurrencyCodeConverter : ValueConverter<CurrencyCode, string>
{
    /// <summary>Creates the converter.</summary>
    public CurrencyCodeConverter()
        : base(code => code.Value, value => CurrencyCode.Parse(value))
    {
    }
}

/// <summary>
/// Converts a string list to/from a single delimited text column. Used for small, ordered string lists
/// (audit changed-field names, scope value lists) where a child table would be overkill and provider
/// array support should not be relied upon.
/// </summary>
public sealed class StringListConverter : ValueConverter<IReadOnlyList<string>, string>
{
    private const char Separator = ''; // ASCII unit separator - safe against commas in values.

    /// <summary>Creates the converter.</summary>
    public StringListConverter()
        : base(
            list => string.Join(Separator, list),
            text => Split(text))
    {
    }

    private static IReadOnlyList<string> Split(string text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Split(Separator, StringSplitOptions.None);
}

/// <summary>Converts a list of longs to/from a comma-delimited text column (e.g. eligible approver ids).</summary>
public sealed class LongListConverter : ValueConverter<List<long>, string>
{
    /// <summary>Creates the converter.</summary>
    public LongListConverter()
        : base(
            list => string.Join(',', list),
            text => Parse(text))
    {
    }

    private static List<long> Parse(string text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToList();
}

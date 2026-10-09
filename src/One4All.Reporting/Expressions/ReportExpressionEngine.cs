using System.Globalization;
using DynamicExpresso;

namespace One4All.Reporting.Expressions;

/// <summary>
/// The runtime context an expression is evaluated against: the current row's fields, the report
/// parameters, page/report variables and a currency-conversion helper (brief §13.1).
/// </summary>
public sealed class ReportExpressionContext
{
    /// <summary>The current data row's field values, keyed by field name.</summary>
    public IReadOnlyDictionary<string, object?> Fields { get; init; } = new Dictionary<string, object?>();

    /// <summary>The report parameter values, keyed by parameter name.</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();

    /// <summary>The current page number.</summary>
    public int? PageNumber { get; init; }

    /// <summary>The total number of pages (when known during a second pass).</summary>
    public int? TotalPages { get; init; }

    /// <summary>The print timestamp.</summary>
    public DateTime PrintDate { get; init; } = DateTime.Now;

    /// <summary>The user running the report.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>The company name.</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>Converts an amount in a currency to base as at a date (injected; returns the amount unchanged if absent).</summary>
    public Func<decimal, string, DateOnly, decimal>? ToBase { get; init; }
}

/// <summary>
/// Evaluates report expressions against a <see cref="ReportExpressionContext"/> using a sandboxed
/// interpreter limited to primitive types plus a curated function library (math, string, date, logical,
/// <c>NumberToWords</c>, <c>ToBase</c>). No file, network or reflection access is exposed (brief §13.1).
/// The same engine backs payroll pay-element formulas (brief §4.15.2).
/// </summary>
public sealed class ReportExpressionEngine
{
    private readonly Interpreter _interpreter;

    /// <summary>Creates the engine and registers the function library.</summary>
    public ReportExpressionEngine()
    {
        // Default options resolve DateTime/Math/primitives but expose no file, network or reflection surface.
        _interpreter = new Interpreter(InterpreterOptions.Default);
        RegisterLibrary();
    }

    /// <summary>Evaluates an expression and returns its value.</summary>
    /// <param name="expression">The expression text.</param>
    /// <param name="context">The evaluation context.</param>
    /// <returns>The evaluated value (may be null).</returns>
    public object? Evaluate(string expression, ReportExpressionContext context)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        List<Parameter> parameters =
        [
            // Before layout the page is not known: the variables stand as "[Page]" / "[Pages]", which pagination fills in.
            context.PageNumber is int page ? new Parameter("PageNumber", page) : new Parameter("PageNumber", "[Page]"),
            context.TotalPages is int pages ? new Parameter("TotalPages", pages) : new Parameter("TotalPages", "[Pages]"),
            new Parameter("PrintDate", context.PrintDate),
            new Parameter("UserName", context.UserName),
            new Parameter("CompanyName", context.CompanyName),
            new Parameter("Field", (Func<string, object?>)(name => context.Fields.GetValueOrDefault(name))),
            new Parameter("Param", (Func<string, object?>)(name => context.Parameters.GetValueOrDefault(name))),
            new Parameter("ToBase", (Func<decimal, string, DateTime, decimal>)((amount, currency, date) =>
                context.ToBase?.Invoke(amount, currency, DateOnly.FromDateTime(date)) ?? amount)),
        ];

        // Expose identifier-safe field names directly (e.g. AccountCode), so an author can write the field.
        foreach ((string name, object? value) in context.Fields)
        {
            if (IsIdentifier(name) && parameters.All(p => p.Name != name))
            {
                parameters.Add(new Parameter(name, value ?? string.Empty));
            }
        }

        return _interpreter.Eval(expression, parameters.ToArray());
    }

    /// <summary>Evaluates an expression to a boolean (null/false → false).</summary>
    /// <param name="expression">The expression text.</param>
    /// <param name="context">The evaluation context.</param>
    /// <returns>The boolean result.</returns>
    public bool EvaluateBool(string expression, ReportExpressionContext context) =>
        Evaluate(expression, context) is true;

    private void RegisterLibrary()
    {
        // String
        _interpreter.SetFunction("Upper", (Func<string?, string>)(s => s?.ToUpperInvariant() ?? string.Empty));
        _interpreter.SetFunction("Lower", (Func<string?, string>)(s => s?.ToLowerInvariant() ?? string.Empty));
        _interpreter.SetFunction("Trim", (Func<string?, string>)(s => s?.Trim() ?? string.Empty));
        _interpreter.SetFunction("Left", (Func<string?, int, string>)((s, n) => s is null ? string.Empty : s[..Math.Min(n, s.Length)]));
        _interpreter.SetFunction("Right", (Func<string?, int, string>)((s, n) => s is null ? string.Empty : s[^Math.Min(n, s.Length)..]));
        _interpreter.SetFunction("Mid", (Func<string?, int, int, string>)((s, start, len) =>
            s is null || start >= s.Length ? string.Empty : s.Substring(start, Math.Min(len, s.Length - start))));
        _interpreter.SetFunction("Replace", (Func<string?, string, string, string>)((s, a, b) => s?.Replace(a, b) ?? string.Empty));
        _interpreter.SetFunction("Len", (Func<string?, int>)(s => s?.Length ?? 0));
        _interpreter.SetFunction("Proper", (Func<string?, string>)(s =>
            s is null ? string.Empty : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant())));

        // Math
        _interpreter.SetFunction("Abs", (Func<decimal, decimal>)Math.Abs);
        _interpreter.SetFunction("Round", (Func<decimal, int, decimal>)((v, d) => Math.Round(v, d, MidpointRounding.ToEven)));
        _interpreter.SetFunction("Min", (Func<decimal, decimal, decimal>)Math.Min);
        _interpreter.SetFunction("Max", (Func<decimal, decimal, decimal>)Math.Max);
        _interpreter.SetFunction("Ceiling", (Func<decimal, decimal>)Math.Ceiling);
        _interpreter.SetFunction("Floor", (Func<decimal, decimal>)Math.Floor);

        // Date
        _interpreter.SetFunction("Today", (Func<DateTime>)(() => DateTime.Today));
        _interpreter.SetFunction("AddDays", (Func<DateTime, int, DateTime>)((d, n) => d.AddDays(n)));
        _interpreter.SetFunction("AddMonths", (Func<DateTime, int, DateTime>)((d, n) => d.AddMonths(n)));
        _interpreter.SetFunction("Year", (Func<DateTime, int>)(d => d.Year));
        _interpreter.SetFunction("Month", (Func<DateTime, int>)(d => d.Month));
        _interpreter.SetFunction("Day", (Func<DateTime, int>)(d => d.Day));
        _interpreter.SetFunction("EndOfMonth", (Func<DateTime, DateTime>)(d => new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month))));
        _interpreter.SetFunction("DateDiff", (Func<DateTime, DateTime, int>)((a, b) => (int)(b.Date - a.Date).TotalDays));
        _interpreter.SetFunction("DateDiffIn", (Func<string, DateTime, DateTime, int>)DateDiffIn);
        _interpreter.SetFunction("FiscalPeriod", (Func<DateTime, int, int>)((d, firstMonth) => ((d.Month - Math.Clamp(firstMonth, 1, 12) + 12) % 12) + 1));
        _interpreter.SetFunction("FiscalYear", (Func<DateTime, int, int>)((d, firstMonth) => firstMonth <= 1 || d.Month < firstMonth ? d.Year : d.Year + 1));

        // Logical
        _interpreter.SetFunction("If", (Func<bool, object, object, object>)((c, a, b) => c ? a : b));
        _interpreter.SetFunction("IsNull", (Func<object?, object, object>)((v, fallback) => v ?? fallback));
        _interpreter.SetFunction("Coalesce", (Func<object?, object?, object?>)((a, b) => a ?? b));
        _interpreter.SetFunction("Case", (Func<bool, object?, object?, object?>)((c1, r1, otherwise) => c1 ? r1 : otherwise));
        _interpreter.SetFunction("Case2", (Func<bool, object?, bool, object?, object?, object?>)((c1, r1, c2, r2, otherwise) => c1 ? r1 : c2 ? r2 : otherwise));
        _interpreter.SetFunction("Case3", (Func<bool, object?, bool, object?, bool, object?, object?, object?>)((c1, r1, c2, r2, c3, r3, otherwise) =>
            c1 ? r1 : c2 ? r2 : c3 ? r3 : otherwise));

        // Formatting / words
        _interpreter.SetFunction("Format", (Func<object?, string, string>)((v, f) => NumberToWords.Format(v, f)));
        _interpreter.SetFunction("NumberToWords", (Func<decimal, string>)(v => NumberToWords.Convert((long)v)));
        _interpreter.SetFunction("AmountToWords", (Func<decimal, string, string, string>)((v, major, minor) => NumberToWords.Amount(v, major, minor)));
        _interpreter.SetFunction("NumberToWordsPt", (Func<decimal, string>)(v => NumberToWordsPortuguese.Convert((long)v)));
        _interpreter.SetFunction("AmountToWordsPt", (Func<decimal, string, string, string, string, string>)((v, majorOne, majorMany, minorOne, minorMany)
            => NumberToWordsPortuguese.Amount(v, majorOne, majorMany, minorOne, minorMany)));
    }

    // Whole units from a to b: "d" days, "w" weeks, "m" months, "q" quarters, "y" years (completed units, like an age).
    private static int DateDiffIn(string unit, DateTime a, DateTime b)
    {
        int months = ((b.Year - a.Year) * 12) + b.Month - a.Month - (b.Day < a.Day ? 1 : 0);
        return (unit ?? "d").Trim().ToLowerInvariant() switch
        {
            "w" or "week" or "weeks" => (int)((b.Date - a.Date).TotalDays / 7),
            "m" or "month" or "months" => months,
            "q" or "quarter" or "quarters" => months / 3,
            "y" or "year" or "years" => months / 12,
            _ => (int)(b.Date - a.Date).TotalDays,
        };
    }

    private static bool IsIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}

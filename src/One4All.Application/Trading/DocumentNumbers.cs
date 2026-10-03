namespace One4All.Application.Trading;

/// <summary>
/// Comparison of trading document numbers as people actually type them: only letters and digits count, and
/// case is ignored — so <c>INV-0042</c>, <c>inv 0042</c> and <c>INV0042</c> are the same document. Used by the
/// duplicate supplier-invoice control and the supplier statement reconciliation. Pure.
/// </summary>
public static class DocumentNumbers
{
    /// <summary>Reduces a document number to its comparison key.</summary>
    /// <param name="documentNumber">The document number.</param>
    /// <returns>The upper-cased letters and digits.</returns>
    public static string Key(string? documentNumber) =>
        documentNumber is null ? string.Empty : new([.. documentNumber.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    /// <summary>Whether two document numbers are the same document.</summary>
    /// <param name="a">One number.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when their keys are equal and not empty.</returns>
    public static bool Same(string? a, string? b)
    {
        string key = Key(a);
        return key.Length > 0 && key == Key(b);
    }
}

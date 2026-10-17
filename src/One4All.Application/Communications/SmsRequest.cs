using System.Text.Encodings.Web;

namespace One4All.Application.Communications;

/// <summary>
/// Fills an SMS provider's request template (brief §8 gateway abstraction): <c>{to}</c>, <c>{body}</c> and <c>{from}</c> are replaced,
/// escaped for the request's content type - JSON strings for JSON bodies, URL encoding for form or query bodies. Pure.
/// </summary>
public static class SmsRequest
{
    /// <summary>Fills a template.</summary>
    /// <param name="template">The provider's template, e.g. <c>{"to":"{to}","text":"{body}"}</c> or <c>to={to}&amp;text={body}</c>.</param>
    /// <param name="contentType">The request content type.</param>
    /// <param name="to">The recipient number.</param>
    /// <param name="body">The message.</param>
    /// <param name="from">The sender id.</param>
    /// <returns>The filled request body.</returns>
    public static string Fill(string template, string contentType, string to, string body, string? from)
    {
        bool json = contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
        string Escape(string value) => json ? JavaScriptEncoder.UnsafeRelaxedJsonEscaping.Encode(value) : Uri.EscapeDataString(value);
        return template.Replace("{to}", Escape(to), StringComparison.Ordinal).Replace("{body}", Escape(body), StringComparison.Ordinal)
            .Replace("{from}", Escape(from ?? string.Empty), StringComparison.Ordinal);
    }

    /// <summary>Normalises a phone number to digits with an optional leading plus; null when it cannot be a number.</summary>
    /// <param name="recipient">The number as given.</param>
    /// <returns>The number, or null.</returns>
    public static string? Number(string? recipient)
    {
        string trimmed = (recipient ?? string.Empty).Trim();
        string digits = new([.. trimmed.Where(char.IsDigit)]);
        return digits.Length is >= 7 and <= 15 ? (trimmed.StartsWith('+') ? "+" : string.Empty) + digits : null;
    }
}

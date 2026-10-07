using One4All.Application.Localization;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.System;

/// <summary>A company's language, formats and terminology (brief §15 and §13.1).</summary>
public interface ILocalizationService
{
    /// <summary>The company's text catalog: its locale's translations with its terminology.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The catalog.</returns>
    Task<TextCatalog> CatalogAsync(long companyId, CancellationToken ct = default);

    /// <summary>The company's terminology (term → its word).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The terms.</returns>
    Task<IReadOnlyDictionary<string, string>> TermsAsync(long companyId, CancellationToken ct = default);

    /// <summary>Sets the company's word for a term (a blank replacement removes it).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="term">The standard word.</param>
    /// <param name="replacement">The company's word.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetTermAsync(long companyId, string term, string? replacement, CancellationToken ct = default);

    /// <summary>Sets the company's locale.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="locale">The locale (e.g. "pt-MZ"; blank: English and invariant formats).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not (an unknown locale).</returns>
    Task<Result> SetLocaleAsync(long companyId, string? locale, CancellationToken ct = default);
}

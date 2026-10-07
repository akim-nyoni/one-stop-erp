using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.System;
using One4All.Application.Localization;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.MasterData;

/// <summary>A company's language, formats and terminology.</summary>
public sealed class LocalizationService : ILocalizationService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public LocalizationService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<TextCatalog> CatalogAsync(long companyId, CancellationToken ct = default)
    {
        string? locale = await _db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Locale).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return TextCatalog.For(locale, await TermsAsync(companyId, ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> TermsAsync(long companyId, CancellationToken ct = default)
        => await _db.TerminologyOverrides.AsNoTracking().Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Term, t => t.Replacement, StringComparer.OrdinalIgnoreCase, ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result> SetTermAsync(long companyId, string term, string? replacement, CancellationToken ct = default)
    {
        string key = (term ?? string.Empty).Trim();
        if (key.Length == 0 || key.Length > 60 || key.Any(char.IsWhiteSpace) && key.Split(' ').Length > 3 || (replacement?.Trim().Length ?? 0) > 60)
        {
            return Result.Failure(Error.Validation("Terms.Invalid", "A term is a word or short phrase (up to 60 characters)."));
        }

        TerminologyOverride? existing = await _db.TerminologyOverrides.FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Term == key, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(replacement))
        {
            if (existing is not null)
            {
                _db.TerminologyOverrides.Remove(existing);
            }
        }
        else if (existing is null)
        {
            _db.TerminologyOverrides.Add(new TerminologyOverride(companyId, key, replacement.Trim()));
        }
        else
        {
            existing.Replacement = replacement.Trim();
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetLocaleAsync(long companyId, string? locale, CancellationToken ct = default)
    {
        string? name = string.IsNullOrWhiteSpace(locale) ? null : locale.Trim();
        if (name is not null)
        {
            try
            {
                name = CultureInfo.GetCultureInfo(name, predefinedOnly: true).Name;
            }
            catch (CultureNotFoundException)
            {
                return Result.Failure(Error.Validation("Locale.Unknown", $"'{locale}' is not a known locale (e.g. en-ZW, pt-MZ, fr-FR)."));
            }
        }

        Company? company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Result.Failure(Error.NotFound("Company.NotFound", $"Company {companyId} not found."));
        }

        company.Locale = name;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}

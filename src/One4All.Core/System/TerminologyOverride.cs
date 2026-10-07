using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>A company's own word for a term (brief §13.1: "Stand" vs "Erf" vs "Property"), used on screens and report labels (<c>sys.terminology_override</c>).</summary>
public sealed class TerminologyOverride : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TerminologyOverride()
    {
        Term = string.Empty;
        Replacement = string.Empty;
    }

    /// <summary>Records a term.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="term">The standard word.</param>
    /// <param name="replacement">The company's word.</param>
    public TerminologyOverride(long companyId, string term, string replacement)
    {
        CompanyId = companyId;
        Term = term;
        Replacement = replacement;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the standard word.</summary>
    public string Term { get; private set; }

    /// <summary>Gets or sets the company's word.</summary>
    public string Replacement { get; set; }
}

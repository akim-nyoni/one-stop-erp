using One4All.Core.Common;

namespace One4All.Application.Abstractions.Documents;

/// <summary>
/// Any document of the §S list as its standard text, found by its number and, where the document needs one, a date: the one entry point the designable
/// DOC reports and the "Print a document" screen use, whichever service renders the document.
/// </summary>
public interface ITextDocumentService
{
    /// <summary>Renders a document as text.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="kind">The document kind (a key or name from <see cref="One4All.Application.Documents.DocumentKinds"/>).</param>
    /// <param name="number">The document's number, as its kind's number label says (a code, a number, or "a/b" where two parts are needed).</param>
    /// <param name="date">The date for a document that needs one (a statement's as-at date, a payslip's pay date, a bill's month); today when omitted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The text, or Validation for an unknown kind or NotFound when the document does not exist.</returns>
    Task<Result<string>> RenderAsync(long companyId, string kind, string number, DateOnly? date = null, CancellationToken ct = default);
}

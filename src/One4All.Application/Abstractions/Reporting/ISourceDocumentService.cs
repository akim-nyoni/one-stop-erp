namespace One4All.Application.Abstractions.Reporting;

/// <summary>
/// The last step of the drill-down (brief §4.4: trial balance → account → transaction → source document): finds the document a journal
/// came from — the customer or supplier item, the till sale, the counter receipt, the order — and describes it.
/// </summary>
public interface ISourceDocumentService
{
    /// <summary>Describes the documents behind a journal, one block of lines each.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="journalNumber">The journal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The description, or a line saying no originating document is held (the journal is its own source).</returns>
    Task<string> DescribeAsync(long companyId, string journalNumber, CancellationToken ct = default);
}

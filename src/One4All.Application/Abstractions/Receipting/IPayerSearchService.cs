namespace One4All.Application.Abstractions.Receipting;

/// <summary>A payer found by the teller's search.</summary>
/// <param name="CustomerId">The account paid into.</param>
/// <param name="Code">The account code.</param>
/// <param name="Name">The account holder.</param>
/// <param name="Matched">What matched: the account itself, a stand it pays rates on, or a student it pays fees for.</param>
/// <param name="Balance">What the account owes (base currency; negative: in credit).</param>
public sealed record PayerHit(long CustomerId, string Code, string Name, string Matched, decimal Balance);

/// <summary>
/// The teller's live payer search across the registers (brief §RCP-3): an account by code or name, a stand by number (its ratepayer), a student by
/// admission number or name (whoever pays the fees) — each with what the account owes.
/// </summary>
public interface IPayerSearchService
{
    /// <summary>Finds payers matching what was typed.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="text">What was typed (at least two characters).</param>
    /// <param name="max">The most hits to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The hits, exact codes first.</returns>
    Task<IReadOnlyList<PayerHit>> SearchAsync(long companyId, string text, int max = 20, CancellationToken ct = default);
}

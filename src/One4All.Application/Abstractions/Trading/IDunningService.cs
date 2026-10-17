using One4All.Application.Trading;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>A dunning notice for one overdue customer.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="CustomerName">The customer name.</param>
/// <param name="Level">The dunning level (1 = first reminder, 2 = second, 3 = final notice).</param>
/// <param name="LevelLabel">A human label for the level.</param>
/// <param name="OverdueAmount">The total overdue balance.</param>
/// <param name="OldestDaysOverdue">Days the oldest overdue item is past due.</param>
public readonly record struct DunningNotice(long CustomerId, string CustomerName, int Level, string LevelLabel, decimal OverdueAmount, int OldestDaysOverdue);

/// <summary>
/// Assesses customers for dunning (overdue reminders): scans the AR ledger for overdue balances and, using
/// escalating day thresholds, assigns each overdue customer a reminder level with their overdue total and
/// oldest days outstanding - the basis for reminder letters and collection follow-up.
/// </summary>
public interface IDunningService
{
    /// <summary>Assesses overdue customers and returns a dunning notice per customer at level 1 or above.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="stages">The stage day thresholds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dunning notices, ordered by level then customer.</returns>
    Task<Result<IReadOnlyList<DunningNotice>>> AssessAsync(long companyId, DateOnly asAtDate, DunningStages stages, CancellationToken ct = default);

    /// <summary>
    /// Renders a payment-reminder letter for one customer at the dunning level their oldest overdue item
    /// warrants, listing every overdue invoice and the total. Refuses a customer with nothing overdue, or
    /// one whose oldest item is still inside the first reminder threshold.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="asAtDate">The letter date.</param>
    /// <param name="stages">The stage day thresholds.</param>
    /// <param name="width">The letter character width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The letter text, or NotFound/Rule.</returns>
    Task<Result<string>> RenderReminderTextAsync(long companyId, long customerId, DateOnly asAtDate, DunningStages stages, int width = DunningLetterFormatter.DefaultWidth, CancellationToken ct = default);

    /// <summary>Saves a dunning group: its own reminder timetable, or exempt from reminders.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="group">The group.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or what is wrong with it.</returns>
    Task<Result> SaveGroupAsync(long companyId, DunningGroupInput group, CancellationToken ct = default);

    /// <summary>Puts a customer in a dunning group (null: the standard stages) and sets their customer type.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerCode">The customer.</param>
    /// <param name="groupCode">The dunning group (null: none).</param>
    /// <param name="customerType">The customer type (null: none).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an unknown customer or group.</returns>
    Task<Result> SetCustomerAsync(long companyId, string customerCode, string? groupCode, string? customerType, CancellationToken ct = default);

    /// <summary>
    /// Emails each customer due a reminder their letter (brief §4.4 dunning run): not again at the same level within the resend interval;
    /// customers without an email address are counted for posting. Each reminder sent is logged.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="stages">The standard stages (groups use their own).</param>
    /// <param name="templateCode">The email template.</param>
    /// <param name="language">The language.</param>
    /// <param name="resendDays">The days before the same level is sent again.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was sent.</returns>
    Task<Result<DunningSend>> SendAsync(long companyId, DateOnly asAtDate, DunningStages stages, string templateCode, string language, int resendDays = 14, CancellationToken ct = default);
}

/// <summary>A dunning group to save.</summary>
/// <param name="Code">The code.</param>
/// <param name="Name">The name.</param>
/// <param name="Stages">Its reminder timetable.</param>
/// <param name="IsExempt">Whether its customers are never chased.</param>
public sealed record DunningGroupInput(string Code, string Name, DunningStages Stages, bool IsExempt = false);

/// <summary>A dunning run's sending.</summary>
/// <param name="BatchId">The email batch (null: nothing to send).</param>
/// <param name="Queued">Reminders queued.</param>
/// <param name="NoEmail">Customers due a reminder with no email address (to post).</param>
/// <param name="AlreadySent">Customers already sent this level within the resend interval.</param>
public sealed record DunningSend(long? BatchId, int Queued, int NoEmail, int AlreadySent);

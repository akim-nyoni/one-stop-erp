using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A move to the next stage of the title pipeline.</summary>
public sealed class TitleStepInput
{
    /// <summary>The stage reached.</summary>
    public TitleStage Stage { get; init; }

    /// <summary>When.</summary>
    public DateOnly Date { get; init; }

    /// <summary>A note (the registry's query, say).</summary>
    public string? Note { get; init; }

    /// <summary>The conveyancer (required when one is assigned).</summary>
    public string? Conveyancer { get; init; }

    /// <summary>The deed number (required on registration).</summary>
    public string? DeedNumber { get; init; }

    /// <summary>Who signed the receipt (required on hand-over).</summary>
    public string? ReceivedBy { get; init; }
}

/// <summary>An open application on the pipeline report.</summary>
/// <param name="ApplicationNumber">The application.</param>
/// <param name="StandNumber">The stand.</param>
/// <param name="Stage">Its stage.</param>
/// <param name="DaysAtStage">Days at the stage.</param>
/// <param name="DaysSinceApplied">Days since it was applied for.</param>
/// <param name="Conveyancer">The conveyancer.</param>
public sealed record OpenTitle(string ApplicationNumber, string StandNumber, TitleStage Stage, int DaysAtStage, int DaysSinceApplied, string? Conveyancer);

/// <summary>The title pipeline as at a date.</summary>
/// <param name="Stages">Ageing by stage.</param>
/// <param name="Applications">The open applications, oldest at their stage first.</param>
public sealed record TitlePipelineReport(IReadOnlyList<StageAgeing> Stages, IReadOnlyList<OpenTitle> Applications);

/// <summary>
/// The title deed pipeline (brief §4.10.6): title is applied for once a stand's holder has paid in full; the application
/// moves through conveyancer, lodging, registry queries, registration (deed number and date), council custody and
/// hand-over against a signed receipt; a blocked stand cannot have title processed; and the pipeline is reported with its
/// age at every stage — including deeds held in custody and not yet collected.
/// </summary>
public interface ITitleDeedService
{
    /// <summary>Applies for title for a stand's current holder.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The application number, or why it was refused.</returns>
    Task<Result<string>> ApplyAsync(long companyId, long standId, DateOnly date, CancellationToken ct = default);

    /// <summary>Moves an application to its next stage.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicationNumber">The application.</param>
    /// <param name="step">The stage reached and its particulars.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> AdvanceAsync(long companyId, string applicationNumber, TitleStepInput step, CancellationToken ct = default);

    /// <summary>The open applications and their ageing by stage, as at a date.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="asAt">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<TitlePipelineReport> PipelineAsync(long companyId, DateOnly asAt, CancellationToken ct = default);
}

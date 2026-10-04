using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>The title deed pipeline: application, stage-by-stage progress and ageing.</summary>
public sealed class TitleDeedService : ITitleDeedService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IHousingMunicipalLinkService _municipalLink;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="municipalLink">The link to the municipal register.</param>
    public TitleDeedService(One4AllDbContext db, INumberSequenceService numbers, IHousingMunicipalLinkService municipalLink)
    {
        _db = db;
        _numbers = numbers;
        _municipalLink = municipalLink;
    }

    /// <inheritdoc />
    public async Task<Result<string>> ApplyAsync(long companyId, long standId, DateOnly date, CancellationToken ct = default)
    {
        HousingStand? stand = await _db.HousingStands.AsNoTracking().FirstOrDefaultAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Error.NotFound("Hse.StandNotFound", $"Stand {standId} not found.");
        }

        if (stand.CurrentHolderCustomerId is not long holderId || stand.Status is not (StandStatus.Sold or StandStatus.Transferred))
        {
            return Error.Rule("Hse.NoHolder", $"Stand {stand.StandNumber} has not been sold; there is no holder to take title.");
        }

        if (await StandClaims.AnyBlockedAsync(_db, [standId], ct).ConfigureAwait(false))
        {
            return Error.Rule("Hse.StandBlocked", $"Stand {stand.StandNumber} is blocked by a caveat or an open land dispute; title cannot be processed.");
        }

        if (await _db.Set<TitleDeed>().AnyAsync(t => t.StandId == standId, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.TitleApplied", $"Title has already been applied for on stand {stand.StandNumber}.");
        }

        // Fully paid: no instalment sale still running, and nothing owing on the holder's account.
        bool instalmentsRunning = await _db.Set<InstalmentAgreement>().AnyAsync(a => a.StandId == standId && a.Status == InstalmentAgreementStatus.Active, ct).ConfigureAwait(false);
        decimal owing = await _db.OpenItems.Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == holderId)
            .SumAsync(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase, ct).ConfigureAwait(false);
        if (instalmentsRunning || owing > 0m)
        {
            return Error.Rule("Hse.NotFullyPaid", $"Stand {stand.StandNumber} is not fully paid; title can be applied for once the holder's account is clear.");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, "TD", date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<TitleDeed>().Add(new TitleDeed(companyId, number.Value, standId, holderId, date));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public Task<Result> AdvanceAsync(long companyId, string applicationNumber, TitleStepInput step, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        return _db.AtomicAsync(() => AdvanceCoreAsync(companyId, applicationNumber, step, ct), ct);
    }

    private async Task<Result> AdvanceCoreAsync(long companyId, string applicationNumber, TitleStepInput step, CancellationToken ct)
    {
        string number = (applicationNumber ?? string.Empty).Trim();
        TitleDeed? title = await _db.Set<TitleDeed>().Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.ApplicationNumber == number, ct).ConfigureAwait(false);
        if (title is null)
        {
            return Result.Failure(Error.NotFound("Hse.TitleNotFound", $"Title application {number} not found."));
        }

        if (!TitlePipeline.CanMove(title.Stage, step.Stage))
        {
            return Result.Failure(Error.Rule("Hse.TitleStage", $"{number} is at {title.Stage}; it cannot move to {step.Stage}."));
        }

        if (step.Date < title.StageSince)
        {
            return Result.Failure(Error.Validation("Hse.TitleDate", $"{number} reached {title.Stage} on {title.StageSince:yyyy-MM-dd}; the next step cannot be earlier."));
        }

        if (await StandClaims.AnyBlockedAsync(_db, [title.StandId], ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Hse.StandBlocked", "The stand is blocked by a caveat or an open land dispute; title cannot be processed."));
        }

        switch (step.Stage)
        {
            case TitleStage.ConveyancerAssigned when string.IsNullOrWhiteSpace(step.Conveyancer):
                return Result.Failure(Error.Validation("Hse.Conveyancer", "Name the conveyancer."));
            case TitleStage.QueryRaised when string.IsNullOrWhiteSpace(step.Note):
                return Result.Failure(Error.Validation("Hse.Query", "Record the registry's query."));
            case TitleStage.Registered when string.IsNullOrWhiteSpace(step.DeedNumber):
                return Result.Failure(Error.Validation("Hse.DeedNumber", "Capture the deed number."));
            case TitleStage.HandedOver when string.IsNullOrWhiteSpace(step.ReceivedBy):
                return Result.Failure(Error.Validation("Hse.Receipt", "Record who signed for the deed."));
            case TitleStage.ConveyancerAssigned:
                title.AssignConveyancer(step.Conveyancer!.Trim());
                break;
            case TitleStage.Registered:
                title.Register(step.DeedNumber!.Trim(), step.Date);
                break;
            case TitleStage.HandedOver:
                title.HandOver(step.ReceivedBy!.Trim());
                break;
        }

        title.Advance(step.Stage, step.Date, string.IsNullOrWhiteSpace(step.Note) ? null : step.Note.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (step.Stage == TitleStage.Registered)
        {
            Result<MunicipalLinkResult> linked = await _municipalLink.OnEventAsync(companyId, title.StandId, title.HolderCustomerId, MunicipalLinkTrigger.TitleRegistered, step.Date, title.DeedNumber, ct).ConfigureAwait(false);
            if (linked.IsFailure)
            {
                return Result.Failure(linked.Error);
            }
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<TitlePipelineReport> PipelineAsync(long companyId, DateOnly asAt, CancellationToken ct = default)
    {
        // As at the date, from the steps taken by then — never the current stage, which may have moved on since.
        var titles = await (
            from t in _db.Set<TitleDeed>().AsNoTracking().Include(t => t.Steps)
            join s in _db.HousingStands.AsNoTracking() on t.StandId equals s.Id
            where t.CompanyId == companyId && t.AppliedOn <= asAt
            select new { Title = t, s.StandNumber }).ToListAsync(ct).ConfigureAwait(false);
        var open = titles
            .Select(x => new { x.Title, x.StandNumber, Step = x.Title.Steps.Where(s => s.Date <= asAt).OrderBy(s => s.Date).ThenBy(s => s.Id).Last() })
            .Where(x => x.Step.Stage != TitleStage.HandedOver)
            .Select(x => new
            {
                x.Title.ApplicationNumber, x.StandNumber, x.Step.Stage, StageSince = x.Step.Date, x.Title.AppliedOn,
                Conveyancer = x.Title.Steps.Any(s => s.Stage == TitleStage.ConveyancerAssigned && s.Date <= asAt) ? x.Title.Conveyancer : null,
            })
            .ToList();
        IReadOnlyList<StageAgeing> stages = TitlePipeline.Ageing(open.Select(o => new TitleAge(o.Stage, o.StageSince, o.AppliedOn)), asAt);
        List<OpenTitle> applications = [.. open
            .Select(o => new OpenTitle(o.ApplicationNumber, o.StandNumber, o.Stage, asAt.DayNumber - o.StageSince.DayNumber, asAt.DayNumber - o.AppliedOn.DayNumber, o.Conveyancer))
            .OrderBy(o => o.Stage).ThenByDescending(o => o.DaysAtStage).ThenBy(o => o.ApplicationNumber, StringComparer.Ordinal)];
        return new TitlePipelineReport(stages, applications);
    }
}

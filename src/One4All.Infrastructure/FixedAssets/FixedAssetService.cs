using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Core.Common;
using One4All.Core.FixedAssets;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.FixedAssets;

/// <summary>
/// Fixed Assets (brief §4.14). Registers and capitalises assets, runs monthly depreciation posting to the
/// GL, and disposes assets recognising the profit or loss — all through the central posting engine.
/// </summary>
public sealed class FixedAssetService : IFixedAssetService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    public FixedAssetService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RegisterAsync(RegisterAssetRequest request, CancellationToken ct = default)
    {
        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Fa.CompanyNotFound", $"Company {request.CompanyId} not found.");
        }

        FixedAsset asset = new(request.CompanyId, request.AssetNumber, request.Description, request.AssetClass, request.Cost, request.ResidualValue, request.AcquisitionDate, request.Method, request.UsefulLifeMonths, request.ReducingBalanceRatePercent, request.CostAccountCode, request.AccumulatedDepreciationAccountCode, request.DepreciationExpenseAccountCode);
        _db.Set<FixedAsset>().Add(asset);

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "GL",
            PostingDate = request.AcquisitionDate,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "FA-ACQ",
            DocumentNumber = request.DocumentNumber,
            Narrative = $"Capitalise asset {asset.AssetNumber}",
            Lines =
            [
                new JournalLineRequest { AccountCode = request.CostAccountCode, Side = JournalSide.Debit, TransactionAmount = request.Cost },
                new JournalLineRequest { AccountCode = request.FundingAccountCode, Side = JournalSide.Credit, TransactionAmount = request.Cost },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return asset.Id;
    }

    /// <inheritdoc />
    public async Task<Result<DepreciationRunResult>> RunDepreciationAsync(long companyId, DateOnly date, string documentNumber, CancellationToken ct = default)
    {
        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Fa.CompanyNotFound", $"Company {companyId} not found.");
        }

        List<FixedAsset> assets = await _db.Set<FixedAsset>()
            .Where(a => a.CompanyId == companyId && a.Status == FixedAssetStatus.Active)
            .ToListAsync(ct).ConfigureAwait(false);

        List<JournalLineRequest> lines = [];
        decimal total = 0m;
        int count = 0;
        foreach (FixedAsset asset in assets)
        {
            decimal charge = asset.MonthlyDepreciation();
            if (charge <= 0m)
            {
                continue;
            }

            lines.Add(new JournalLineRequest { AccountCode = asset.DepreciationExpenseAccountCode, Side = JournalSide.Debit, TransactionAmount = charge, Narrative = asset.AssetNumber });
            lines.Add(new JournalLineRequest { AccountCode = asset.AccumulatedDepreciationAccountCode, Side = JournalSide.Credit, TransactionAmount = charge, Narrative = asset.AssetNumber });
            asset.ApplyDepreciation(charge);
            total += charge;
            count++;
        }

        if (count == 0)
        {
            return new DepreciationRunResult(0, 0m, null);
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "FA-DEPR",
            DocumentNumber = documentNumber,
            Narrative = $"Depreciation run {date:yyyy-MM}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DepreciationRunResult(count, total, posted.Value.JournalNumber);
    }

    /// <inheritdoc />
    public async Task<Result<DisposalResult>> DisposeAsync(long companyId, long assetId, decimal proceeds, DateOnly date, string fundingAccountCode, string gainAccountCode, string lossAccountCode, string documentNumber, CancellationToken ct = default)
    {
        FixedAsset? asset = await _db.Set<FixedAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            return Error.NotFound("Fa.AssetNotFound", $"Asset {assetId} not found.");
        }

        if (asset.Status == FixedAssetStatus.Disposed)
        {
            return Error.Rule("Fa.AlreadyDisposed", $"Asset {asset.AssetNumber} is already disposed.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Fa.CompanyNotFound", $"Company {companyId} not found.");
        }

        decimal nbv = asset.NetBookValue;
        decimal gainOrLoss = proceeds - nbv;

        // Clear cost and accumulated depreciation, take the proceeds, and book the gain/loss balancing figure.
        List<JournalLineRequest> lines =
        [
            new JournalLineRequest { AccountCode = fundingAccountCode, Side = JournalSide.Debit, TransactionAmount = proceeds },
            new JournalLineRequest { AccountCode = asset.AccumulatedDepreciationAccountCode, Side = JournalSide.Debit, TransactionAmount = asset.AccumulatedDepreciation },
            new JournalLineRequest { AccountCode = asset.CostAccountCode, Side = JournalSide.Credit, TransactionAmount = asset.Cost },
        ];

        if (gainOrLoss > 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = gainAccountCode, Side = JournalSide.Credit, TransactionAmount = gainOrLoss });
        }
        else if (gainOrLoss < 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = lossAccountCode, Side = JournalSide.Debit, TransactionAmount = -gainOrLoss });
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "FA-DISP",
            DocumentNumber = documentNumber,
            Narrative = $"Disposal of asset {asset.AssetNumber}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        asset.Dispose();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DisposalResult(proceeds, nbv, gainOrLoss, posted.Value.JournalNumber);
    }

    /// <inheritdoc />
    public async Task<Result<RevaluationResult>> RevalueAsync(long companyId, long assetId, decimal newCarryingAmount, DateOnly date, string revaluationReserveAccountCode, string impairmentExpenseAccountCode, string documentNumber, CancellationToken ct = default)
    {
        if (newCarryingAmount < 0m)
        {
            return Error.Rule("Fa.NegativeCarryingAmount", "The revalued carrying amount cannot be negative.");
        }

        FixedAsset? asset = await _db.Set<FixedAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            return Error.NotFound("Fa.AssetNotFound", $"Asset {assetId} not found.");
        }

        if (asset.Status == FixedAssetStatus.Disposed)
        {
            return Error.Rule("Fa.AlreadyDisposed", $"Asset {asset.AssetNumber} is disposed and cannot be revalued.");
        }

        decimal delta = newCarryingAmount - asset.NetBookValue;
        if (delta == 0m)
        {
            return new RevaluationResult(0m, newCarryingAmount, string.Empty);
        }

        // Uplift → Dr asset cost / Cr revaluation reserve; write-down → Dr impairment expense / Cr asset cost.
        List<JournalLineRequest> lines = delta > 0m
            ?
            [
                new JournalLineRequest { AccountCode = asset.CostAccountCode, Side = JournalSide.Debit, TransactionAmount = delta },
                new JournalLineRequest { AccountCode = revaluationReserveAccountCode, Side = JournalSide.Credit, TransactionAmount = delta },
            ]
            :
            [
                new JournalLineRequest { AccountCode = impairmentExpenseAccountCode, Side = JournalSide.Debit, TransactionAmount = -delta },
                new JournalLineRequest { AccountCode = asset.CostAccountCode, Side = JournalSide.Credit, TransactionAmount = -delta },
            ];

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "FA-REVAL",
            DocumentNumber = documentNumber,
            Narrative = $"Revaluation of asset {asset.AssetNumber}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        asset.Revalue(delta);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RevaluationResult(delta, asset.NetBookValue, posted.Value.JournalNumber);
    }

    /// <inheritdoc />
    public async Task<Result> TransferAsync(long companyId, long assetId, string? newLocation, string? newCustodian, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newLocation) && string.IsNullOrWhiteSpace(newCustodian))
        {
            return Result.Failure(Error.Validation("Fa.NothingToTransfer", "Provide a new location and/or custodian."));
        }

        FixedAsset? asset = await _db.Set<FixedAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            return Result.Failure(Error.NotFound("Fa.AssetNotFound", $"Asset {assetId} not found."));
        }

        if (asset.Status == FixedAssetStatus.Disposed)
        {
            return Result.Failure(Error.Rule("Fa.AlreadyDisposed", $"Asset {asset.AssetNumber} is disposed and cannot be transferred."));
        }

        if (!string.IsNullOrWhiteSpace(newLocation))
        {
            asset.Location = newLocation;
        }

        if (!string.IsNullOrWhiteSpace(newCustodian))
        {
            asset.Custodian = newCustodian;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<DepreciationForecastReport>> DepreciationForecastAsync(long companyId, DateOnly startMonth, int months, CancellationToken ct = default)
    {
        if (months <= 0)
        {
            return Error.Rule("Fa.ForecastMonths", "The number of months to forecast must be positive.");
        }

        List<FixedAsset> assets = await _db.Set<FixedAsset>()
            .Where(a => a.CompanyId == companyId && a.Status == FixedAssetStatus.Active)
            .ToListAsync(ct).ConfigureAwait(false);

        // Simulate each asset month by month on local balances, never touching the entities.
        (decimal Nbv, decimal Residual, decimal Straight, decimal RbRate, DepreciationMethod Method, bool ByLife)[] state = assets
            .Select(a => (a.NetBookValue, a.ResidualValue,
                a.UsefulLifeMonths <= 0 ? 0m : decimal.Round(a.DepreciableAmount / a.UsefulLifeMonths, 4, MidpointRounding.ToEven),
                a.ReducingBalanceRatePercent, a.Method, a.Method == DepreciationMethod.StraightLine))
            .ToArray();

        DateOnly month = new(startMonth.Year, startMonth.Month, 1);
        List<DepreciationForecastPeriod> periods = [];
        for (int m = 0; m < months; m++)
        {
            decimal periodCharge = 0m;
            decimal closingNbv = 0m;
            for (int i = 0; i < state.Length; i++)
            {
                decimal remaining = state[i].Nbv - state[i].Residual;
                decimal charge = 0m;
                if (remaining > 0m)
                {
                    charge = state[i].ByLife
                        ? state[i].Straight
                        : decimal.Round(state[i].Nbv * state[i].RbRate / 100m, 4, MidpointRounding.ToEven);
                    if (charge > remaining) { charge = remaining; }
                    state[i].Nbv -= charge;
                }

                periodCharge += charge;
                closingNbv += state[i].Nbv;
            }

            periods.Add(new DepreciationForecastPeriod(month.ToString("yyyy-MM"), periodCharge, closingNbv));
            month = month.AddMonths(1);
        }

        return new DepreciationForecastReport(new DateOnly(startMonth.Year, startMonth.Month, 1), months, periods);
    }
}

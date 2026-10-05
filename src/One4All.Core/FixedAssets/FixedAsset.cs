using One4All.Core.Common;

namespace One4All.Core.FixedAssets;

/// <summary>The depreciation method for an asset (brief §4.14).</summary>
public enum DepreciationMethod
{
    /// <summary>Straight line: (cost − residual) spread evenly over the useful life.</summary>
    StraightLine = 1,

    /// <summary>Reducing balance: a fixed percentage of the net book value each period.</summary>
    ReducingBalance = 2,
}

/// <summary>Why a depreciation run passed over an asset.</summary>
public enum DepreciationSkip
{
    /// <summary>Disposed or already fully depreciated.</summary>
    NotActive = 1,

    /// <summary>Acquired after the run month.</summary>
    NotYetAcquired = 2,

    /// <summary>The run month (or a later one) has already been charged.</summary>
    AlreadyDepreciated = 3,
}

/// <summary>The lifecycle of a fixed asset (brief §4.14).</summary>
public enum FixedAssetStatus
{
    /// <summary>In use and depreciating.</summary>
    Active = 1,

    /// <summary>Fully written down (NBV at residual).</summary>
    FullyDepreciated = 2,

    /// <summary>Disposed.</summary>
    Disposed = 3,
}

/// <summary>
/// A fixed asset (brief §4.14, <c>fa.asset</c>). Holds cost, accumulated depreciation and the parameters
/// that drive the monthly depreciation run. Depreciation and disposal post through the central engine.
/// </summary>
public sealed class FixedAsset : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private FixedAsset()
    {
        AssetNumber = string.Empty;
        Description = string.Empty;
        AssetClass = string.Empty;
        CostAccountCode = string.Empty;
        AccumulatedDepreciationAccountCode = string.Empty;
        DepreciationExpenseAccountCode = string.Empty;
    }

    /// <summary>Creates a fixed asset.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetNumber">The asset number/tag.</param>
    /// <param name="description">The description.</param>
    /// <param name="assetClass">The asset class.</param>
    /// <param name="cost">The acquisition cost.</param>
    /// <param name="residualValue">The residual value.</param>
    /// <param name="acquisitionDate">The acquisition date.</param>
    /// <param name="method">The depreciation method.</param>
    /// <param name="usefulLifeMonths">The useful life in months (straight line).</param>
    /// <param name="reducingBalanceRatePercent">The monthly reducing-balance rate (reducing balance).</param>
    /// <param name="costAccountCode">The asset-cost account.</param>
    /// <param name="accumulatedDepreciationAccountCode">The accumulated-depreciation account.</param>
    /// <param name="depreciationExpenseAccountCode">The depreciation-expense account.</param>
    public FixedAsset(long companyId, string assetNumber, string description, string assetClass, decimal cost, decimal residualValue, DateOnly acquisitionDate, DepreciationMethod method, int usefulLifeMonths, decimal reducingBalanceRatePercent, string costAccountCode, string accumulatedDepreciationAccountCode, string depreciationExpenseAccountCode)
    {
        CompanyId = companyId;
        AssetNumber = assetNumber.Trim().ToUpperInvariant();
        Description = description;
        AssetClass = assetClass;
        Cost = cost;
        ResidualValue = residualValue;
        AcquisitionDate = acquisitionDate;
        Method = method;
        UsefulLifeMonths = usefulLifeMonths;
        ReducingBalanceRatePercent = reducingBalanceRatePercent;
        CostAccountCode = costAccountCode;
        AccumulatedDepreciationAccountCode = accumulatedDepreciationAccountCode;
        DepreciationExpenseAccountCode = depreciationExpenseAccountCode;
        Status = FixedAssetStatus.Active;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The asset number/tag.</summary>
    public string AssetNumber { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; set; }

    /// <summary>The asset class.</summary>
    public string AssetClass { get; private set; }

    /// <summary>The custodian.</summary>
    public string? Custodian { get; set; }

    /// <summary>The location.</summary>
    public string? Location { get; set; }

    /// <summary>The acquisition cost.</summary>
    public decimal Cost { get; private set; }

    /// <summary>The residual value.</summary>
    public decimal ResidualValue { get; private set; }

    /// <summary>The acquisition date.</summary>
    public DateOnly AcquisitionDate { get; private set; }

    /// <summary>The depreciation method.</summary>
    public DepreciationMethod Method { get; private set; }

    /// <summary>The useful life in months (straight line).</summary>
    public int UsefulLifeMonths { get; private set; }

    /// <summary>The monthly reducing-balance rate (reducing balance).</summary>
    public decimal ReducingBalanceRatePercent { get; private set; }

    /// <summary>The accumulated depreciation to date.</summary>
    public decimal AccumulatedDepreciation { get; private set; }

    /// <summary>The status.</summary>
    public FixedAssetStatus Status { get; private set; }

    /// <summary>
    /// The last month depreciation was charged for, as <c>yyyyMM</c> (null before the first run). Guards a month
    /// against being charged twice.
    /// </summary>
    public int? LastDepreciatedPeriod { get; private set; }

    /// <summary>The asset-cost account.</summary>
    public string CostAccountCode { get; private set; }

    /// <summary>The accumulated-depreciation account.</summary>
    public string AccumulatedDepreciationAccountCode { get; private set; }

    /// <summary>The depreciation-expense account.</summary>
    public string DepreciationExpenseAccountCode { get; private set; }

    /// <summary>Gets or sets the insurer.</summary>
    public string? Insurer { get; set; }

    /// <summary>Gets or sets the insurance policy number.</summary>
    public string? PolicyNumber { get; set; }

    /// <summary>Gets or sets the sum insured.</summary>
    public decimal? InsuredValue { get; set; }

    /// <summary>Gets or sets when the insurance cover ends.</summary>
    public DateOnly? InsuranceExpiry { get; set; }

    /// <summary>Gets or sets when the manufacturer's / supplier's warranty ends.</summary>
    public DateOnly? WarrantyExpiry { get; set; }

    /// <summary>The net book value (cost − accumulated depreciation).</summary>
    public decimal NetBookValue => Cost - AccumulatedDepreciation;

    /// <summary>The depreciable amount (cost − residual).</summary>
    public decimal DepreciableAmount => Cost - ResidualValue;

    /// <summary>
    /// Computes the depreciation charge for one month, never taking the net book value below the residual.
    /// </summary>
    /// <returns>The monthly depreciation charge.</returns>
    public decimal MonthlyDepreciation()
    {
        if (Status != FixedAssetStatus.Active)
        {
            return 0m;
        }

        decimal charge = Method switch
        {
            DepreciationMethod.StraightLine => UsefulLifeMonths <= 0 ? 0m : decimal.Round(DepreciableAmount / UsefulLifeMonths, 4, MidpointRounding.ToEven),
            DepreciationMethod.ReducingBalance => decimal.Round(NetBookValue * ReducingBalanceRatePercent / 100m, 4, MidpointRounding.ToEven),
            _ => 0m,
        };

        decimal remaining = NetBookValue - ResidualValue;
        if (remaining <= 0m)
        {
            return 0m;
        }

        return charge > remaining ? remaining : charge;
    }

    /// <summary>The <c>yyyyMM</c> period key of a date.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The period key.</returns>
    public static int PeriodOf(DateOnly date) => (date.Year * 100) + date.Month;

    /// <summary>
    /// Why the asset would not be charged for the month containing <paramref name="runDate"/>, or null when it is
    /// due. Full-month convention: an asset is depreciated from the month it was acquired in.
    /// </summary>
    /// <param name="runDate">Any date in the run month.</param>
    /// <returns>The reason it is skipped, or null.</returns>
    public DepreciationSkip? SkipReasonFor(DateOnly runDate)
    {
        int period = PeriodOf(runDate);
        if (Status != FixedAssetStatus.Active)
        {
            return DepreciationSkip.NotActive;
        }

        if (PeriodOf(AcquisitionDate) > period)
        {
            return DepreciationSkip.NotYetAcquired;
        }

        if (LastDepreciatedPeriod is { } last && last >= period)
        {
            return DepreciationSkip.AlreadyDepreciated;
        }

        return null;
    }

    /// <summary>Applies a depreciation charge for a month, updating accumulated depreciation, status and the last period.</summary>
    /// <param name="charge">The charge to apply.</param>
    /// <param name="runDate">Any date in the month charged.</param>
    public void ApplyDepreciation(decimal charge, DateOnly runDate)
    {
        AccumulatedDepreciation += charge;
        LastDepreciatedPeriod = PeriodOf(runDate);
        if (NetBookValue <= ResidualValue)
        {
            Status = FixedAssetStatus.FullyDepreciated;
        }
    }

    /// <summary>Marks the asset disposed.</summary>
    public void Dispose() => Status = FixedAssetStatus.Disposed;

    /// <summary>
    /// Revalues the asset by adjusting its gross cost by <paramref name="delta"/> (a positive uplift or a
    /// negative write-down), leaving accumulated depreciation unchanged so the net book value moves by the same
    /// amount. Future depreciation is then based on the revalued carrying amount.
    /// </summary>
    /// <param name="delta">The change to the carrying amount (+ up, − down).</param>
    public void Revalue(decimal delta) => Cost += delta;
}

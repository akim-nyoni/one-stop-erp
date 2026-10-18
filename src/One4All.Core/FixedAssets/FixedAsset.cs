using One4All.Core.Common;

namespace One4All.Core.FixedAssets;

/// <summary>The depreciation method for an asset (brief §4.14).</summary>
public enum DepreciationMethod
{
    /// <summary>Straight line: (cost − residual) spread evenly over the useful life.</summary>
    StraightLine = 1,

    /// <summary>Reducing balance: a fixed percentage of the net book value each period.</summary>
    ReducingBalance = 2,

    /// <summary>Units of production: (cost − residual) × units used in the month ÷ the total units the asset is expected to produce.</summary>
    UnitsOfProduction = 3,
}

/// <summary>When an asset's depreciation starts within its first month (brief §4.14: "depreciation conventions").</summary>
public enum DepreciationConvention
{
    /// <summary>A full month's charge from the month of acquisition.</summary>
    FullMonth = 0,

    /// <summary>Acquired by the 15th: charged from that month; acquired after it: from the next month.</summary>
    MidMonth = 1,

    /// <summary>Charged from the month after acquisition.</summary>
    NextMonth = 2,

    /// <summary>The first month is charged for the days held (from the acquisition day to the month end).</summary>
    DailyProRata = 3,

    /// <summary>Half a year's depreciation in the year of acquisition whatever the month (the rest of that year's months share it), then full years.</summary>
    MidYear = 4,
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

    /// <summary>Gets or sets the barcode or tag number fixed to the asset (what a count scans); unique in the company.</summary>
    public string? TagNumber { get; set; }

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

    /// <summary>Gets or sets the asset this one is a component of (IAS 16 significant parts, depreciated separately).</summary>
    public long? ParentAssetId { get; set; }

    /// <summary>Gets or sets the GL cost centre the asset's cost, depreciation and disposal are analysed by.</summary>
    public string? CostCentre { get; set; }

    /// <summary>Gets or sets the GL department the asset's cost, depreciation and disposal are analysed by.</summary>
    public string? Department { get; set; }

    /// <summary>Gets or sets the total units (hours, kilometres, tonnes…) a units-of-production asset is expected to produce in its life.</summary>
    public decimal? TotalEstimatedUnits { get; set; }

    /// <summary>Gets or sets when depreciation starts within the first month.</summary>
    public DepreciationConvention Convention { get; set; }

    /// <summary>Gets or sets the supplier invoice (AP open item) the asset was capitalised from.</summary>
    public long? SourceOpenItemId { get; set; }

    /// <summary>Gets or sets the account the supplier invoice was coded to when the asset was capitalised from it.</summary>
    public string? SourceAccountCode { get; set; }

    /// <summary>The first month (<c>yyyyMM</c>) the convention charges depreciation for.</summary>
    /// <returns>The period.</returns>
    public int FirstChargePeriod() => Convention switch
    {
        DepreciationConvention.NextMonth => PeriodOf(AcquisitionDate.AddMonths(1)),
        DepreciationConvention.MidMonth when AcquisitionDate.Day > 15 => PeriodOf(AcquisitionDate.AddMonths(1)),
        _ => PeriodOf(AcquisitionDate),
    };

    /// <summary>The share of a month's charge due for the month of a run: the days held in a daily pro-rata asset's first month, otherwise all of it.</summary>
    /// <param name="runDate">Any date in the run month.</param>
    /// <returns>The share (0–1).</returns>
    public decimal FirstMonthShare(DateOnly runDate)
    {
        if (Convention != DepreciationConvention.DailyProRata || LastDepreciatedPeriod is not null || PeriodOf(runDate) != PeriodOf(AcquisitionDate))
        {
            return 1m;
        }

        int days = DateTime.DaysInMonth(AcquisitionDate.Year, AcquisitionDate.Month);
        return (decimal)(days - AcquisitionDate.Day + 1) / days;
    }

    /// <summary>The net book value (cost − accumulated depreciation).</summary>
    public decimal NetBookValue => Cost - AccumulatedDepreciation;

    /// <summary>The depreciable amount (cost − residual).</summary>
    public decimal DepreciableAmount => Cost - ResidualValue;

    /// <summary>
    /// Computes the depreciation charge for one month, never taking the net book value below the residual.
    /// </summary>
    /// <returns>The monthly depreciation charge.</returns>
    /// <param name="unitsUsed">Units used in the month (units-of-production assets).</param>
    /// <param name="runDate">The run month, for a daily pro-rata first month (null: a full month).</param>
    public decimal MonthlyDepreciation(decimal unitsUsed = 0m, DateOnly? runDate = null)
    {
        if (Status != FixedAssetStatus.Active)
        {
            return 0m;
        }

        // After an impairment (IAS 36) the reduced carrying amount is spread over the life that is left.
        int monthsLeft = UsefulLifeMonths - MonthsDepreciated;
        decimal charge = Method switch
        {
            DepreciationMethod.StraightLine when AccumulatedImpairment != 0m => monthsLeft <= 0 ? NetBookValue - ResidualValue
                : decimal.Round((NetBookValue - ResidualValue) / monthsLeft, 4, MidpointRounding.ToEven),
            DepreciationMethod.StraightLine => UsefulLifeMonths <= 0 ? 0m : decimal.Round(DepreciableAmount / UsefulLifeMonths, 4, MidpointRounding.ToEven),
            DepreciationMethod.ReducingBalance => decimal.Round(NetBookValue * ReducingBalanceRatePercent / 100m, 4, MidpointRounding.ToEven),
            DepreciationMethod.UnitsOfProduction => TotalEstimatedUnits is > 0m and decimal total ? decimal.Round(DepreciableAmount * unitsUsed / total, 4, MidpointRounding.ToEven) : 0m,
            _ => 0m,
        };
        if (runDate is DateOnly month && Method != DepreciationMethod.UnitsOfProduction && FirstMonthShare(month) is var share && share < 1m)
        {
            charge = decimal.Round(charge * share, 4, MidpointRounding.ToEven);
        }

        // Half-year convention: the acquisition year carries six months' charge spread over the months from acquisition to its end.
        if (Convention == DepreciationConvention.MidYear && runDate is DateOnly inYear && inYear.Year == AcquisitionDate.Year && Method != DepreciationMethod.UnitsOfProduction)
        {
            charge = decimal.Round(charge * 6m / (13 - AcquisitionDate.Month), 4, MidpointRounding.ToEven);
        }

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

        if (FirstChargePeriod() > period)
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

    /// <summary>The share of the disposal month's charge due up to the disposal date, by the convention (a full month; half of a month after the 15th; the days held).</summary>
    /// <param name="disposalDate">The disposal date.</param>
    /// <returns>The share (0–1).</returns>
    public decimal DisposalMonthShare(DateOnly disposalDate) => Convention switch
    {
        DepreciationConvention.MidMonth => disposalDate.Day > 15 ? 1m : 0m,
        DepreciationConvention.DailyProRata => (decimal)disposalDate.Day / DateTime.DaysInMonth(disposalDate.Year, disposalDate.Month),
        _ => 1m,
    };

    /// <summary>
    /// Revalues the asset by adjusting its gross cost by <paramref name="delta"/> (a positive uplift or a
    /// negative write-down), leaving accumulated depreciation unchanged so the net book value moves by the same
    /// amount. Future depreciation is then based on the revalued carrying amount.
    /// </summary>
    /// <param name="delta">The change to the carrying amount (+ up, − down).</param>
    public void Revalue(decimal delta) => Cost += delta;

    /// <summary>Gets the revaluation surplus held in equity for this asset (IAS 16.39-40): uplifts credited to the reserve, less decreases charged against it.</summary>
    public decimal RevaluationSurplus { get; private set; }

    /// <summary>Gets the revaluation decreases charged to profit and not yet reversed by a later uplift (IAS 16.39).</summary>
    public decimal RevaluationLossInProfit { get; private set; }

    /// <summary>Gets the equity account the asset's revaluation surplus is held in.</summary>
    public string? RevaluationReserveAccountCode { get; private set; }

    /// <summary>
    /// Splits a change in carrying amount the way IAS 16.39-40 requires, per asset: an increase first reverses decreases this asset charged to profit
    /// (credited to profit), the rest goes to the revaluation surplus; a decrease first uses up this asset's surplus, the rest is charged to profit.
    /// </summary>
    /// <param name="delta">The change to the carrying amount (+ up, - down).</param>
    /// <returns>To the reserve (+ credit, - debit) and to profit (+ a loss, - a gain); they add up to the change.</returns>
    public (decimal ToReserve, decimal ToProfit) SplitRevaluation(decimal delta)
    {
        if (delta >= 0m)
        {
            decimal reversal = Math.Min(delta, RevaluationLossInProfit);
            return (delta - reversal, -reversal);
        }

        decimal fromReserve = Math.Min(-delta, RevaluationSurplus);
        return (-fromReserve, -delta - fromReserve);
    }

    /// <summary>Applies a revaluation split by <see cref="SplitRevaluation"/>: the cost moves by the change, the surplus and the loss in profit by their shares.</summary>
    /// <param name="delta">The change to the carrying amount.</param>
    /// <param name="toReserve">The reserve's share (+ credit).</param>
    /// <param name="toProfit">Profit's share (+ a loss).</param>
    /// <param name="reserveAccountCode">The reserve account, kept for the transfer on disposal.</param>
    public void ApplyRevaluation(decimal delta, decimal toReserve, decimal toProfit, string? reserveAccountCode)
    {
        Cost += delta;
        RevaluationSurplus += toReserve;
        RevaluationLossInProfit += toProfit;
        if (toReserve != 0m && !string.IsNullOrWhiteSpace(reserveAccountCode))
        {
            RevaluationReserveAccountCode = reserveAccountCode.Trim();
        }
    }

    /// <summary>Takes the surplus out of the reserve as the asset is derecognised (IAS 16.41), returning what was held.</summary>
    /// <returns>The surplus transferred.</returns>
    public decimal RealiseSurplus()
    {
        decimal surplus = RevaluationSurplus;
        RevaluationSurplus = 0m;
        return surplus;
    }

    /// <summary>The impairment losses held (net of reversals), included in <see cref="AccumulatedDepreciation"/> (IAS 36).</summary>
    public decimal AccumulatedImpairment { get; private set; }

    /// <summary>The months depreciation has been charged for, from the first month the convention charges to the last period charged.</summary>
    public int MonthsDepreciated => LastDepreciatedPeriod is int last && FirstChargePeriod() is int first
        ? Math.Max(0, ((last / 100) - (first / 100)) * 12 + (last % 100) - (first % 100) + 1)
        : 0;

    /// <summary>
    /// The carrying amount the asset would have had with no impairment ever recognised (straight-line: cost less the original schedule's
    /// depreciation to date); a reversal can never take it above this. Other methods: the carrying amount plus the impairment held.
    /// </summary>
    public decimal CarryingAmountWithoutImpairment => Method == DepreciationMethod.StraightLine && UsefulLifeMonths > 0
        ? Cost - Math.Min(DepreciableAmount, decimal.Round(DepreciableAmount * MonthsDepreciated / UsefulLifeMonths, 2, MidpointRounding.AwayFromZero))
        : NetBookValue + AccumulatedImpairment;

    /// <summary>Recognises an impairment loss (positive) or reverses one (negative), within accumulated depreciation and impairment.</summary>
    /// <param name="amount">The loss (+) or reversal (−).</param>
    public void Impair(decimal amount)
    {
        AccumulatedDepreciation += amount;
        AccumulatedImpairment += amount;
        if (amount < 0m && Status == FixedAssetStatus.FullyDepreciated && NetBookValue > ResidualValue)
        {
            Status = FixedAssetStatus.Active;
        }
        else if (NetBookValue <= ResidualValue && Status == FixedAssetStatus.Active)
        {
            Status = FixedAssetStatus.FullyDepreciated;
        }
    }
}

/// <summary>
/// An asset class (<c>fa.asset_class</c>, brief §4.13): the defaults an asset registered in it takes - method, useful life, residual value
/// as a percentage of cost, reducing-balance rate, and the cost, accumulated depreciation, depreciation expense and impairment accounts.
/// </summary>
public sealed class FixedAssetClass : AuditableEntity, ICompanyScoped
{
    private FixedAssetClass()
    {
        Code = string.Empty;
        Name = string.Empty;
        CostAccountCode = string.Empty;
        AccumulatedDepreciationAccountCode = string.Empty;
        DepreciationExpenseAccountCode = string.Empty;
    }

    /// <summary>Creates the class.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">Its code (what assets carry as their class).</param>
    /// <param name="name">Its name.</param>
    public FixedAssetClass(long companyId, string code, string name)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        CostAccountCode = string.Empty;
        AccumulatedDepreciationAccountCode = string.Empty;
        DepreciationExpenseAccountCode = string.Empty;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Its code.</summary>
    public string Code { get; private set; }

    /// <summary>Its name.</summary>
    public string Name { get; set; }

    /// <summary>The default depreciation method.</summary>
    public DepreciationMethod Method { get; set; } = DepreciationMethod.StraightLine;

    /// <summary>The default useful life in months.</summary>
    public int UsefulLifeMonths { get; set; }

    /// <summary>The default residual value as a percentage of cost.</summary>
    public decimal ResidualPercent { get; set; }

    /// <summary>The default reducing-balance rate.</summary>
    public decimal ReducingBalanceRatePercent { get; set; }

    /// <summary>The cost account.</summary>
    public string CostAccountCode { get; set; }

    /// <summary>The accumulated depreciation (and impairment) account.</summary>
    public string AccumulatedDepreciationAccountCode { get; set; }

    /// <summary>The depreciation expense account.</summary>
    public string DepreciationExpenseAccountCode { get; set; }

    /// <summary>The impairment loss (and reversal) account.</summary>
    public string? ImpairmentAccountCode { get; set; }

    /// <summary>The default depreciation convention.</summary>
    public DepreciationConvention Convention { get; set; }
}

/// <summary>An impairment test's outcome on an asset (<c>fa.impairment</c>, IAS 36): the recoverable amount found and the loss or reversal booked.</summary>
public sealed class AssetImpairment : Entity, ICompanyScoped
{
    private AssetImpairment()
    {
        Reason = string.Empty;
        JournalNumber = string.Empty;
    }

    /// <summary>Records the test.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fixedAssetId">The asset.</param>
    /// <param name="date">The test date.</param>
    /// <param name="carryingAmountBefore">The carrying amount before.</param>
    /// <param name="recoverableAmount">The recoverable amount (higher of fair value less costs of disposal and value in use).</param>
    /// <param name="amount">The loss (+) or reversal (−) booked.</param>
    /// <param name="reason">The indicator / reason.</param>
    /// <param name="journalNumber">The posting.</param>
    public AssetImpairment(long companyId, long fixedAssetId, DateOnly date, decimal carryingAmountBefore, decimal recoverableAmount, decimal amount, string reason, string journalNumber)
    {
        CompanyId = companyId;
        FixedAssetId = fixedAssetId;
        Date = date;
        CarryingAmountBefore = carryingAmountBefore;
        RecoverableAmount = recoverableAmount;
        Amount = amount;
        Reason = reason;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The asset.</summary>
    public long FixedAssetId { get; private set; }

    /// <summary>The test date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The carrying amount before.</summary>
    public decimal CarryingAmountBefore { get; private set; }

    /// <summary>The recoverable amount.</summary>
    public decimal RecoverableAmount { get; private set; }

    /// <summary>The loss (+) or reversal (−).</summary>
    public decimal Amount { get; private set; }

    /// <summary>The indicator / reason.</summary>
    public string Reason { get; private set; }

    /// <summary>The posting.</summary>
    public string JournalNumber { get; private set; }
}

/// <summary>Units an asset was used for in a month (<c>fa.usage_reading</c>), which a units-of-production asset is depreciated on.</summary>
public sealed class AssetUsageReading : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AssetUsageReading() => Reference = string.Empty;

    /// <summary>Creates the reading.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="date">The date (its month is the one charged).</param>
    /// <param name="units">The units used.</param>
    /// <param name="reference">Where the reading came from (meter, log book).</param>
    public AssetUsageReading(long companyId, long assetId, DateOnly date, decimal units, string reference)
    {
        CompanyId = companyId;
        AssetId = assetId;
        Date = date;
        Units = units;
        Reference = reference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The asset.</summary>
    public long AssetId { get; private set; }

    /// <summary>The date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The units used.</summary>
    public decimal Units { get; private set; }

    /// <summary>Where the reading came from.</summary>
    public string Reference { get; private set; }
}

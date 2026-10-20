using System.Data;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.JobCosting;
using One4All.Core.FixedAssets;
using One4All.Core.JobCosting;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Weighbridge, fixed-asset and contract operations.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Weighbridge()
    {
        const string module = "Weighbridge";
        yield return new Operation
        {
            Module = module, Title = "Concrete slump and strength tests", ListTitle = "Failed tests (last 90 days)",
            Description = "Records a slump test (mm, passes within the tolerance of the target) or a 7- or 28-day cube strength (MPa, passes at or above the specified strength) "
                + "against a batch run; Show lists a run's tests.",
            Fields = [Pick("Step", "Step", ["Record", "Show"]), Req("Run", "Batch run number"), Opt("Sample", "Sample / cube reference"),
                Pick("Kind", "Test", Enum.GetNames<Core.Inventory.ConcreteTestKind>()), Num("Result", "Result (mm or MPa)", "0"), Num("Specified", "Target slump or specified strength", "0"),
                Num("Tolerance", "Slump tolerance (mm)", "25"), Opt("By", "Tested by"), Today()],
            Run = async (c, i) =>
            {
                Application.Abstractions.Inventory.IConcreteTestService tests = c.Get<Application.Abstractions.Inventory.IConcreteTestService>();
                if (i.Text("Step") == "Show")
                {
                    IReadOnlyList<Core.Inventory.ConcreteTest> found = await tests.ForRunAsync(c.CompanyId, i.Text("Run")).ConfigureAwait(true);
                    return found.Count == 0 ? "No tests on that run." : string.Join(Environment.NewLine, found.Select(t =>
                        $"{D(t.TestedOn)} {t.SampleReference,-10} {t.Kind,-10} {Q(t.Result),8} vs {Q(t.Specified),8}  {(t.Passed ? "pass" : "FAIL")}"));
                }

                (long id, bool passed) = Check(await tests.RecordAsync(c.CompanyId, new Application.Abstractions.Inventory.ConcreteTestEntry(i.Text("Run"), i.Optional("Sample") ?? string.Empty,
                    Enum.Parse<Core.Inventory.ConcreteTestKind>(i.Text("Kind")), i.Date("Date"), i.Number("Result"), i.Number("Specified"), i.Number("Tolerance")) { TestedBy = i.Optional("By") })
                    .ConfigureAwait(true));
                return passed ? $"Test {id} recorded: pass." : $"Test {id} recorded: FAILED - follow up with the plant and the customer.";
            },
            List = async c => Table(await c.Get<Application.Abstractions.Inventory.IConcreteTestService>().FailuresAsync(c.CompanyId, DateOnly.FromDateTime(DateTime.Today).AddDays(-90),
                    DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true),
                ("Date", t => D(t.TestedOn)), ("Run", t => t.RunNumber), ("Sample", t => t.SampleReference), ("Test", t => t.Kind), ("Result", t => Q(t.Result)), ("Specified", t => Q(t.Specified))),
        };
        yield return new Operation
        {
            Module = module, Title = "Vehicle register", ListTitle = "Vehicles",
            Description = "The vehicles that use the weighbridge (brief §4.2): the haulier, the stored tare an outbound first weigh can take instead of weighing empty, the gross the vehicle may "
                + "not exceed (an overload is flagged on the ticket), and whether it is barred from the bridge.",
            Fields = [Req("Vehicle", "Registration"), Opt("Haulier", "Haulier"), OptNum("Tare", "Stored tare kg"), OptNum("MaxGross", "Permitted gross kg"), Tick("Active", "May use the bridge", true)],
            Run = async (c, i) => Check(await c.Get<Application.Abstractions.JobCosting.IWeighbridgeService>().SaveVehicleAsync(c.CompanyId, i.Text("Vehicle"), i.Optional("Haulier"), i.OptionalNumber("Tare"),
                i.OptionalNumber("MaxGross"), i.Flag("Active")).ConfigureAwait(true), id => $"Vehicle {i.Text("Vehicle").ToUpperInvariant()} saved (id {id})."),
            List = async c => Table(await c.Db.WeighbridgeVehicles.AsNoTracking().Where(v => v.CompanyId == c.CompanyId).OrderBy(v => v.Registration).ToListAsync().ConfigureAwait(true),
                ("Vehicle", v => v.Registration), ("Haulier", v => v.Haulier ?? string.Empty), ("Stored tare kg", v => v.StoredTareKg?.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty),
                ("Max gross kg", v => v.MaxGrossKg?.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty), ("Active", v => v.IsActive ? "yes" : "barred")),
        };
        yield return new Operation
        {
            Module = module, Title = "First weigh (open a ticket)", ListTitle = "Tickets still on the bridge",
            Description = "Outbound loads weigh empty first (leave the weight blank to use the vehicle's stored tare); inbound loads weigh loaded first. A keyed weight needs a reason and another person's authority.",
            Fields = [Pick("Direction", "Direction", "Outbound", "Inbound"), Req("Vehicle", "Vehicle registration"), Req("Item", "Product (item code)"), Num("Density", "m³ per tonne", "0.6"), Today(),
                OptNum("Weight", "Weight kg (blank = stored tare)"), CustomerPick("Customer", "Customer", required: false), SupplierPick("Supplier", "Supplier", required: false), Opt("Driver", "Driver"), Opt("Site", "Delivery site"),
                Opt("Stockpile", "Stockpile (warehouse code)"), OptNum("Distance", "Haulage distance km"), Opt("Batch", "Concrete batch run"), Tick("Manual", "Weight keyed by hand"), Opt("Reason", "Why keyed"),
                Opt("AuthorisedBy", "Authorised by (user name)"), Opt("Order", "Sales order (outbound) or purchase order (inbound)"), Tick("Scale", "Read the weight from the scale")],
            Run = async (c, i) =>
            {
                decimal? weight = i.Flag("Scale") ? await ScaleWeightAsync(c).ConfigureAwait(true) : i.OptionalNumber("Weight");
                var (_, number) = Check(await c.Get<IWeighbridgeService>().FirstWeighAsync(new FirstWeighRequest
                {
                    CompanyId = c.CompanyId, Direction = Enum.Parse<WeighDirection>(i.Text("Direction")), Vehicle = i.Text("Vehicle"), ItemId = await ItemAsync(c, i.Text("Item")).ConfigureAwait(true),
                    CubicMetresPerTonne = i.Number("Density"), Date = i.Date("Date"), WeightKg = weight,
                    SalesOrderNumber = i.Text("Direction") == nameof(WeighDirection.Outbound) ? i.Optional("Order") : null,
                    PurchaseOrderNumber = i.Text("Direction") == nameof(WeighDirection.Inbound) ? i.Optional("Order") : null,
                    CustomerId = i.Optional("Customer") is string cu ? await CustomerAsync(c, cu).ConfigureAwait(true) : null,
                    SupplierId = i.Optional("Supplier") is string su ? await SupplierAsync(c, su).ConfigureAwait(true) : null,
                    Driver = i.Optional("Driver"), Site = i.Optional("Site"), WarehouseId = i.Optional("Stockpile") is string sp ? await WarehouseAsync(c, sp).ConfigureAwait(true) : null,
                    DistanceKm = i.OptionalNumber("Distance"), BatchRunNumber = i.Optional("Batch"), OperatorUserId = c.UserId, IsManual = i.Flag("Manual"), ManualReason = i.Optional("Reason"),
                    AuthorisedByUserId = await OptionalUserAsync(c, i.Optional("AuthorisedBy")).ConfigureAwait(true),
                }).ConfigureAwait(true));
                return $"Ticket {number} opened.";
            },
            List = OpenTicketsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Second weigh (complete a ticket)", ListTitle = "Tickets still on the bridge",
            Description = "Completes the ticket: net weight, haulage by zone and - for an outbound load from a stockpile - the stock issue.",
            Fields = [Req("Ticket", "Ticket number"), OptNum("Weight", "Weight kg"), Num("Deduction", "Deduction kg (moisture etc.)", "0"), Tick("Manual", "Weight keyed by hand"), Opt("Reason", "Why keyed"),
                Opt("AuthorisedBy", "Authorised by (user name)"), Tick("Scale", "Read the weight from the scale"), Tick("Print", "Show the printed ticket")],
            Run = async (c, i) =>
            {
                long ticket = await FindAsync(c.Db.WeighbridgeTickets, t => t.CompanyId == c.CompanyId && t.TicketNumber == i.Text("Ticket").ToUpper(), t => t.Id, "ticket", i.Text("Ticket")).ConfigureAwait(true);
                decimal weight = (i.Flag("Scale") ? await ScaleWeightAsync(c).ConfigureAwait(true) : i.OptionalNumber("Weight")) ?? throw new OperationException("Give the weight, or read it from the scale.");
                WeighbridgeCompletion done = Check(await c.Get<IWeighbridgeService>().SecondWeighAsync(new SecondWeighRequest
                {
                    CompanyId = c.CompanyId, TicketId = ticket, WeightKg = weight, DeductionKg = i.Number("Deduction"), OperatorUserId = c.UserId, IsManual = i.Flag("Manual"),
                    ManualReason = i.Optional("Reason"), AuthorisedByUserId = await OptionalUserAsync(c, i.Optional("AuthorisedBy")).ConfigureAwait(true),
                }).ConfigureAwait(true));
                string summary = $"Net {Q(done.NetTonnes)} t ({Q(done.NetCubicMetres)} m³), haulage {M(done.HaulageCharge)}." + (done.Overloaded ? " OVERLOADED - over the vehicle's legal maximum." : string.Empty);
                return i.Flag("Print") ? summary + Environment.NewLine + Environment.NewLine + Check(await c.Get<IWeighbridgeService>().RenderTicketAsync(c.CompanyId, i.Text("Ticket")).ConfigureAwait(true)) : summary;
            },
            List = OpenTicketsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Invoice weighbridge loads", ListTitle = "Completed loads not yet invoiced",
            Description = "Bills a customer for every completed outbound load up to the invoice date that has not been invoiced: net tonnes at the price, plus each load's haulage, "
                + "with tax on top. Each ticket is marked with the invoice so it is never billed twice.",
            Fields = [CustomerPick("Customer", "Customer"), Req("Invoice", "Invoice number"), Today(), OptNum("Price", "Price per tonne (blank: the order's or the price book's)"), Req("Sales", "Sales account", "4000"),
                Opt("Haulage", "Haulage revenue account (blank = sales)"), Opt("TaxCode", "Tax code")],
            Run = async (c, i) =>
            {
                WeighbridgeInvoice done = Check(await c.Get<IWeighbridgeService>().InvoiceTicketsAsync(new WeighbridgeInvoiceRequest
                {
                    CompanyId = c.CompanyId, CustomerId = await CustomerAsync(c, i.Text("Customer")).ConfigureAwait(true), DocumentNumber = i.Text("Invoice"), DocumentDate = i.Date("Date"),
                    PricePerTonne = i.OptionalNumber("Price") ?? 0m, SalesAccountCode = i.Text("Sales"), HaulageAccountCode = i.Optional("Haulage"), TaxCode = i.Optional("TaxCode"), EnteredByUserId = c.UserId,
                }).ConfigureAwait(true));
                return $"Invoiced {done.Tickets.Count} load(s), {Q(done.Tonnes)} t: product {M(done.Product)}, haulage {M(done.Haulage)}, tax {M(done.Tax)}, total {M(done.Total)}.";
            },
            List = async c => Table(await c.Db.WeighbridgeTickets.AsNoTracking()
                    .Where(t => t.CompanyId == c.CompanyId && t.IsComplete && t.Direction == WeighDirection.Outbound && t.CustomerId != null && t.InvoiceNumber == null)
                    .OrderBy(t => t.Date).ThenBy(t => t.Id).ToListAsync().ConfigureAwait(true),
                ("Ticket", t => t.TicketNumber), ("Date", t => D(t.Date)), ("Vehicle", t => t.VehicleRegistration), ("Product", t => t.Product), ("Net t", t => Q(t.NetTonnes)),
                ("Haulage", t => M(t.HaulageCharge)), ("Site", t => t.SiteName)),
        };
    }

    // The weight on the weighbridge scale: a settled reading only (a moving lorry gives a moving weight).
    private static async Task<decimal> ScaleWeightAsync(OperationContext c)
    {
        Application.Abstractions.Pos.IScale scale = c.Get<Application.Abstractions.Pos.IDeviceProvider>().Scale ?? throw new OperationException("No scale is connected to this computer.");
        Application.Pos.ScaleReading reading = await scale.ReadAsync().ConfigureAwait(true) ?? throw new OperationException("The scale did not answer.");
        return reading.Stable ? reading.Kilograms : throw new OperationException($"The scale is still moving ({reading.Kilograms:0} kg); wait for it to settle.");
    }

    private static async Task<System.Data.DataTable> OpenTicketsAsync(OperationContext c)
        => Table(await c.Db.WeighbridgeTickets.AsNoTracking().Where(t => t.CompanyId == c.CompanyId && !t.IsComplete).OrderBy(t => t.Id).ToListAsync().ConfigureAwait(true),
            ("Ticket", t => t.TicketNumber), ("Date", t => D(t.Date)), ("Vehicle", t => t.VehicleRegistration), ("Direction", t => t.Direction), ("Product", t => t.Product),
            ("First weight", t => Q(t.Direction == WeighDirection.Outbound ? t.TareWeightKg : t.GrossWeightKg)), ("Driver", t => t.DriverName));

    private static IEnumerable<Operation> FixedAssets()
    {
        const string module = "Fixed Assets";
        yield return new Operation
        {
            Module = module, Title = "Asset register", ListTitle = "Fixed asset register",
            Description = "Register an asset (Dr cost, Cr the funding account; the class fills in method, life, residual and accounts left blank), run the month's depreciation "
                + "for every active asset (a month already charged is skipped), dispose of an asset with the profit or loss on disposal, revalue it (IAS 16: an uplift to the "
                + "revaluation reserve, a write-down to expense), move it to a new location or custodian, or record maintenance work on it.",
            Fields = [Pick("Step", "Step", ["Register", "Run depreciation", "Dispose", "Revalue", "Move", "Maintenance"]), Opt("Asset", "Asset number (all but the run)"), Today(),
                Opt("Description", "Description (register, maintenance)"), Opt("Class", "Asset class (register)"), OptNum("Amount", "Cost / proceeds / new carrying amount / maintenance cost"),
                Opt("Funding", "Funding account (register: credited; dispose: debited with proceeds)"), Opt("Parent", "Parent asset (register a component)"),
                Opt("CostCentre", "Cost centre (register)"), Opt("Department", "Department (register)"), Opt("Gain", "Gain on disposal account"), Opt("Loss", "Loss on disposal account"),
                Opt("Customer", "Sold to customer (dispose: invoiced; Funding is then the proceeds account)"), Opt("TaxCode", "Tax code on the sale (dispose to a customer)"),
                Opt("Reserve", "Revaluation reserve account"), Opt("WriteDown", "Revaluation write-down expense account"), Opt("Location", "New location (move)"),
                Opt("Custodian", "New custodian (move)"), Opt("Provider", "Service provider (maintenance)"), Opt("Document", "Document number"),
                Opt("Tag", "Barcode / tag number (register, move)")],
            Run = async (c, i) =>
            {
                IFixedAssetService assets = c.Get<IFixedAssetService>();
                string step = i.Text("Step");
                DateOnly date = i.Date("Date");
                string document = i.Optional("Document") ?? $"FA-{date:yyyyMMdd}-{(i.Optional("Asset") ?? "RUN").ToUpperInvariant()}";
                if (step == "Run depreciation")
                {
                    DepreciationRunResult run = Check(await assets.RunDepreciationAsync(c.CompanyId, date, document).ConfigureAwait(true));
                    return $"{run.AssetsDepreciated} asset(s) depreciated, {M(run.TotalDepreciation)} charged" + (run.JournalNumber is string j ? $" (journal {j})" : string.Empty)
                        + $"; {run.SkippedAlreadyDepreciated} already charged this month, {run.SkippedNotYetAcquired} not yet acquired.";
                }

                string number = (i.Optional("Asset") ?? throw new OperationException("Give the asset number.")).Trim().ToUpperInvariant();
                if (step == "Register")
                {
                    long registered = Check(await assets.RegisterAsync(new RegisterAssetRequest
                    {
                        CompanyId = c.CompanyId, AssetNumber = number, Description = i.Optional("Description") ?? number, AssetClass = i.Optional("Class") ?? string.Empty,
                        Cost = i.OptionalNumber("Amount") ?? throw new OperationException("Give the asset's cost."), AcquisitionDate = date,
                        FundingAccountCode = i.Optional("Funding") ?? throw new OperationException("Give the funding account credited with the cost."), DocumentNumber = document,
                        ParentAssetNumber = i.Optional("Parent"), CostCentre = i.Optional("CostCentre"), Department = i.Optional("Department"),
                    }).ConfigureAwait(true));
                    await TagAsync(c, registered, i.Optional("Tag")).ConfigureAwait(true);
                    return $"Asset {number} registered and capitalised" + (i.Optional("Tag") is string tagged ? $", tag {tagged.ToUpperInvariant()}." : ".");
                }

                long asset = await FindAsync(c.Db.FixedAssets, a => a.CompanyId == c.CompanyId && a.AssetNumber.ToUpper() == number, a => a.Id, "asset", number).ConfigureAwait(true);
                switch (step)
                {
                    case "Dispose":
                        DisposalResult disposal = i.Optional("Customer") is string buyer
                            ? Check(await assets.SellToCustomerAsync(new AssetSaleRequest
                            {
                                CompanyId = c.CompanyId, AssetId = asset, CustomerId = await CustomerAsync(c, buyer).ConfigureAwait(true), NetProceeds = i.OptionalNumber("Amount") ?? 0m,
                                TaxCode = i.Optional("TaxCode"), Date = date, InvoiceNumber = document, ProceedsAccountCode = i.Optional("Funding") ?? string.Empty,
                                GainAccountCode = i.Optional("Gain") ?? string.Empty, LossAccountCode = i.Optional("Loss") ?? string.Empty,
                            }).ConfigureAwait(true))
                            : Check(await assets.DisposeAsync(c.CompanyId, asset, i.OptionalNumber("Amount") ?? 0m, date, i.Optional("Funding") ?? string.Empty,
                                i.Optional("Gain") ?? string.Empty, i.Optional("Loss") ?? string.Empty, document).ConfigureAwait(true));
                        return $"{number} disposed: proceeds {M(disposal.Proceeds)} against book value {M(disposal.NetBookValue)}, "
                            + (disposal.GainOrLoss >= 0m ? $"profit {M(disposal.GainOrLoss)}" : $"loss {M(-disposal.GainOrLoss)}") + $" (journal {disposal.JournalNumber}).";
                    case "Revalue":
                        RevaluationResult revaluation = Check(await assets.RevalueAsync(c.CompanyId, asset, i.OptionalNumber("Amount") ?? throw new OperationException("Give the new carrying amount."),
                            date, i.Optional("Reserve") ?? string.Empty, i.Optional("WriteDown") ?? string.Empty, document).ConfigureAwait(true));
                        return $"{number} revalued to {M(revaluation.NewCarryingAmount)} ({(revaluation.Delta >= 0m ? "uplift" : "write-down")} {M(Math.Abs(revaluation.Delta))}: "
                            + $"reserve {M(revaluation.ToReserve)}, profit {M(-revaluation.ToProfit)}; journal {revaluation.JournalNumber}).";
                    case "Move":
                        _ = Check(await assets.TransferAsync(c.CompanyId, asset, i.Optional("Location"), i.Optional("Custodian")).ConfigureAwait(true), string.Empty);
                        await TagAsync(c, asset, i.Optional("Tag")).ConfigureAwait(true);
                        return $"{number} moved" + (i.Optional("Tag") is string retagged ? $" and tagged {retagged.ToUpperInvariant()}." : ".");
                    default:
                        return Check(await assets.RecordMaintenanceAsync(c.CompanyId, asset, date, i.Optional("Description") ?? throw new OperationException("Describe the work done."),
                            i.OptionalNumber("Amount") ?? 0m, i.Optional("Provider")).ConfigureAwait(true), id => $"Maintenance on {number} recorded.");
                }
            },
            List = async c => Table(await c.Db.FixedAssets.AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderBy(a => a.AssetNumber).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Asset", a => a.AssetNumber), ("Description", a => a.Description), ("Class", a => a.AssetClass), ("Cost", a => M(a.Cost)), ("Depreciation", a => M(a.AccumulatedDepreciation)),
                ("Book value", a => M(a.NetBookValue)), ("Status", a => a.Status.ToString()), ("Location", a => a.Location ?? "-"), ("Custodian", a => a.Custodian ?? "-"),
                ("Tag", a => a.TagNumber ?? "-")),
        };
        yield return new Operation
        {
            Module = module, Title = "Asset classes and impairment", ListTitle = "Asset classes",
            Description = "Classes hold the defaults an asset registered in them takes (method, life, residual %, rate and accounts). An impairment test (IAS 36) books a "
                + "recoverable amount below the carrying amount as a loss, or reverses an earlier loss when it recovers - never above what it would have been unimpaired.",
            Fields = [Pick("Step", "Step", ["Save class", "Impairment test"]), Opt("Class", "Class code"), Opt("Name", "Class name"),
                Pick("Method", "Method", Enum.GetNames<Core.FixedAssets.DepreciationMethod>()), OptNum("Life", "Useful life (months)"), OptNum("Residual", "Residual % of cost"),
                OptNum("Rate", "Reducing-balance rate %"), Opt("CostAccount", "Cost account"), Opt("AccumAccount", "Accumulated depreciation account"), Opt("ExpenseAccount", "Depreciation expense account"),
                Opt("ImpairmentAccount", "Impairment account"), Opt("Asset", "Asset number (test)"), OptNum("Recoverable", "Recoverable amount (test)"), Opt("Reason", "Indicator / reason (test)"),
                Opt("Document", "Document number (test)"), Pick("Convention", "Depreciation convention", Enum.GetNames<Core.FixedAssets.DepreciationConvention>()), Today()],
            Run = async (c, i) =>
            {
                Application.Abstractions.FixedAssets.IFixedAssetService assets = c.Get<Application.Abstractions.FixedAssets.IFixedAssetService>();
                if (i.Text("Step") == "Save class")
                {
                    return Check(await assets.SaveClassAsync(new Application.Abstractions.FixedAssets.AssetClassRequest(c.CompanyId, i.Optional("Class") ?? string.Empty, i.Optional("Name") ?? string.Empty,
                        Enum.Parse<Core.FixedAssets.DepreciationMethod>(i.Text("Method")), (int)(i.OptionalNumber("Life") ?? 0m), i.OptionalNumber("Residual") ?? 0m, i.OptionalNumber("Rate") ?? 0m,
                        i.Optional("CostAccount") ?? string.Empty, i.Optional("AccumAccount") ?? string.Empty, i.Optional("ExpenseAccount") ?? string.Empty, i.Optional("ImpairmentAccount"),
                        Enum.Parse<Core.FixedAssets.DepreciationConvention>(i.Text("Convention"))))
                        .ConfigureAwait(true), "Asset class saved.");
                }

                string number = (i.Optional("Asset") ?? throw new OperationException("Type the asset number.")).ToUpperInvariant();
                long asset = await FindAsync(c.Db.FixedAssets, a => a.CompanyId == c.CompanyId && a.AssetNumber == number, a => a.Id, "asset", number).ConfigureAwait(true);
                Application.Abstractions.FixedAssets.ImpairmentResult r = Check(await assets.ImpairAsync(c.CompanyId, asset, i.Number("Recoverable"), i.Date("Date"), i.Optional("Reason") ?? string.Empty,
                    i.Optional("Document") ?? string.Empty, i.Optional("ImpairmentAccount")).ConfigureAwait(true));
                return r.Amount == 0m ? $"Nothing to book; carrying amount {M(r.CarryingAmount)}."
                    : $"{(r.Amount > 0m ? "Impairment loss" : "Reversal")} of {M(Math.Abs(r.Amount))} (journal {r.JournalNumber}); carrying amount now {M(r.CarryingAmount)}.";
            },
            List = async c => Table(await c.Db.Set<Core.FixedAssets.FixedAssetClass>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Class", x => x.Code), ("Name", x => x.Name), ("Method", x => x.Method), ("Life (months)", x => x.UsefulLifeMonths), ("Residual %", x => x.ResidualPercent),
                ("Cost", x => x.CostAccountCode), ("Depreciation", x => x.AccumulatedDepreciationAccountCode), ("Expense", x => x.DepreciationExpenseAccountCode), ("Impairment", x => x.ImpairmentAccountCode)),
        };
        yield return new Operation
        {
            Module = module, Title = "Move an asset to another cost centre", Description = "Moves the asset's cost and accumulated depreciation to another cost centre / department; future depreciation follows.",
            Fields = [Req("Asset", "Asset number"), Opt("CostCentre", "Cost centre code"), Opt("Department", "Department code"), Today(), Req("Document", "Document number")],
            Run = async (c, i) =>
            {
                long asset = await FindAsync(c.Db.FixedAssets, a => a.CompanyId == c.CompanyId && a.AssetNumber.ToUpper() == i.Text("Asset").ToUpper(), a => a.Id, "asset", i.Text("Asset")).ConfigureAwait(true);
                return $"Transferred: {Check(await c.Get<IFixedAssetService>().ReassignAsync(c.CompanyId, asset, i.Optional("CostCentre"), i.Optional("Department"), i.Date("Date"), i.Text("Document")).ConfigureAwait(true))}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Capitalise from a supplier invoice", ListTitle = "Assets capitalised from invoices",
            Description = "The invoice's amount on the account it was coded to becomes the asset's cost (moved to the class's cost account when coded elsewhere); never more than is left.",
            Fields = [Req("Invoice", "Supplier invoice number"), Opt("Supplier", "Supplier code (if the number repeats)"), Req("Asset", "Asset number"), Req("Description", "Description"),
                Req("Class", "Asset class"), OptNum("Cost", "Cost (blank: all that is left)"), Opt("Account", "Account the invoice was coded to (blank: the class's cost account)"),
                Opt("Document", "Reclassification document number")],
            Run = async (c, i) => Check(await c.Get<IFixedAssetService>().CapitaliseFromInvoiceAsync(new Application.Abstractions.FixedAssets.CapitaliseInvoiceRequest
            {
                CompanyId = c.CompanyId, InvoiceNumber = i.Text("Invoice"), SupplierCode = i.Optional("Supplier"), AssetNumber = i.Text("Asset"), Description = i.Text("Description"),
                AssetClass = i.Text("Class"), Cost = i.OptionalNumber("Cost"), InvoiceAccountCode = i.Optional("Account"), DocumentNumber = i.Optional("Document"),
            }).ConfigureAwait(true), id => $"Asset {i.Text("Asset").ToUpperInvariant()} capitalised (id {id})."),
            List = async c => Table(await c.Db.FixedAssets.AsNoTracking().Where(a => a.CompanyId == c.CompanyId && a.SourceOpenItemId != null).OrderByDescending(a => a.Id).Page(c, 200)
                .Join(c.Db.OpenItems.AsNoTracking(), a => a.SourceOpenItemId, o => (long?)o.Id, (a, o) => new { a.AssetNumber, a.Description, a.Cost, o.DocumentNumber, a.SourceAccountCode, a.AcquisitionDate })
                .ToListAsync().ConfigureAwait(true), ("Asset", a => a.AssetNumber), ("Description", a => a.Description), ("Cost", a => M(a.Cost)), ("Invoice", a => a.DocumentNumber),
                ("Coded to", a => a.SourceAccountCode), ("Acquired", a => D(a.AcquisitionDate))),
        };
        yield return new Operation
        {
            Module = module, Title = "Record asset usage", Description = "Units (hours, km, tonnes) a units-of-production asset was used for; the month's depreciation run charges them.",
            Fields = [Req("Asset", "Asset number"), Today(), Num("Units", "Units used"), Opt("Reference", "Reading reference", "Meter")],
            Run = async (c, i) =>
            {
                long asset = await FindAsync(c.Db.FixedAssets, a => a.CompanyId == c.CompanyId && a.AssetNumber.ToUpper() == i.Text("Asset").ToUpper(), a => a.Id, "asset", i.Text("Asset")).ConfigureAwait(true);
                return Check(await c.Get<IFixedAssetService>().RecordUsageAsync(c.CompanyId, asset, i.Date("Date"), i.Number("Units"), i.Optional("Reference") ?? string.Empty).ConfigureAwait(true),
                    id => $"Usage reading {id} recorded.");
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Start an asset count", ListTitle = "Open counts", Description = "Lists every held asset the register places at the location (blank for everywhere) for verification.",
            Fields = [Opt("Location", "Location"), Today()],
            Run = async (c, i) =>
            {
                long id = Check(await c.Get<IAssetControlService>().StartCountAsync(c.CompanyId, i.Date("Date"), i.Optional("Location")).ConfigureAwait(true));
                string number = await c.Db.AssetCounts.Where(x => x.Id == id).Select(x => x.CountNumber).SingleAsync().ConfigureAwait(true);
                return $"Count {number} started.";
            },
            List = OpenCountsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Record an asset sighting", ListTitle = "Open counts", Description = "Records an asset (or unknown tag) seen during a count, where and in what condition.",
            Fields = [Req("Count", "Count number"), Req("Tag", "Asset number / tag"), Req("Location", "Seen at"), Opt("Condition", "Condition")],
            Run = async (c, i) => Check(await c.Get<IAssetControlService>().RecordSightingAsync(c.CompanyId, await CountAsync(c, i.Text("Count")).ConfigureAwait(true), i.Text("Tag"), i.Text("Location"), i.Optional("Condition"))
                .ConfigureAwait(true), $"{i.Text("Tag")} recorded."),
            List = OpenCountsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Close an asset count", ListTitle = "Open counts", Description = "Closes the count: anything not seen is missing. Optionally moves assets found elsewhere to where they were seen.",
            Fields = [Req("Count", "Count number"), Tick("Move", "Update locations of assets found elsewhere", true)],
            Run = async (c, i) =>
            {
                int missing = Check(await c.Get<IAssetControlService>().CloseCountAsync(c.CompanyId, await CountAsync(c, i.Text("Count")).ConfigureAwait(true), i.Flag("Move")).ConfigureAwait(true));
                return $"Count closed - {missing} asset(s) missing. See report FA210.";
            },
            List = OpenCountsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Plan recurring maintenance", ListTitle = "Maintenance plans", Description = "A task repeated every N months for an asset.",
            Fields = [Req("Asset", "Asset number"), Req("Task", "Task"), Num("Interval", "Every (months)", "6"), Today("FirstDue", "First due")],
            Run = async (c, i) =>
            {
                long asset = await FindAsync(c.Db.FixedAssets, a => a.CompanyId == c.CompanyId && a.AssetNumber.ToUpper() == i.Text("Asset").ToUpper(), a => a.Id, "asset", i.Text("Asset")).ConfigureAwait(true);
                long plan = Check(await c.Get<IAssetControlService>().PlanMaintenanceAsync(c.CompanyId, asset, i.Text("Task"), i.Whole("Interval"), i.Date("FirstDue")).ConfigureAwait(true));
                return $"Plan {plan} created.";
            },
            List = PlansAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Complete planned maintenance", ListTitle = "Maintenance plans", Description = "Logs the planned task as done and rolls the next due date forward.",
            Fields = [Num("Plan", "Plan id (from the list)"), Today(), Num("Cost", "Cost", "0"), Opt("Provider", "Done by")],
            Run = async (c, i) => Check(await c.Get<IAssetControlService>().CompletePlannedAsync(c.CompanyId, (long)i.Number("Plan"), i.Date("Date"), i.Number("Cost"), i.Optional("Provider")).ConfigureAwait(true), log => $"Logged (maintenance record {log})."),
            List = PlansAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Open a capital project (CWIP)", ListTitle = "Capital projects", Description = "A project collecting cost in a capital work-in-progress account until capitalised.",
            Fields = [Req("Code", "Project code"), Req("Name", "Name"), Req("Cwip", "CWIP account"), Num("Budget", "Budget"), Today("Opened", "Opened")],
            Run = async (c, i) => Check(await c.Get<ICapitalProjectService>().OpenAsync(c.CompanyId, i.Text("Code"), i.Text("Name"), i.Text("Cwip"), i.Number("Budget"), i.Date("Opened")).ConfigureAwait(true), $"Project {i.Text("Code").ToUpperInvariant()} opened."),
            List = ProjectsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Add a cost to a capital project", ListTitle = "Capital projects", Description = "Dr CWIP, Cr the funding account.",
            Fields = [Req("Project", "Project code"), Today(), Num("Amount", "Amount"), Req("Funding", "Funding account", "1100"), Req("Description", "Description")],
            Run = async (c, i) => $"Posted ({Check(await c.Get<ICapitalProjectService>().AddCostAsync(c.CompanyId, await ProjectAsync(c, i.Text("Project")).ConfigureAwait(true), i.Date("Date"), i.Number("Amount"), i.Text("Funding"), i.Text("Description")).ConfigureAwait(true))}).",
            List = ProjectsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Capitalise a capital project", ListTitle = "Capital projects", Description = "Registers the asset at the project's whole cost, clearing CWIP.",
            Fields = [Req("Project", "Project code"), Req("Asset", "New asset number"), Req("Description", "Asset description"), Req("Class", "Asset class"), Today("InUse", "In use from"),
                Num("Life", "Useful life (months)", "240"), Req("CostAccount", "Asset cost account", "1600"), Req("AccDep", "Accumulated depreciation account", "1650"), Req("DepExpense", "Depreciation expense account", "6000")],
            Run = async (c, i) =>
            {
                long asset = Check(await c.Get<ICapitalProjectService>().CapitaliseAsync(await ProjectAsync(c, i.Text("Project")).ConfigureAwait(true), new RegisterAssetRequest
                {
                    CompanyId = c.CompanyId, AssetNumber = i.Text("Asset"), Description = i.Text("Description"), AssetClass = i.Text("Class"), AcquisitionDate = i.Date("InUse"), Method = DepreciationMethod.StraightLine,
                    UsefulLifeMonths = i.Whole("Life"), CostAccountCode = i.Text("CostAccount"), AccumulatedDepreciationAccountCode = i.Text("AccDep"), DepreciationExpenseAccountCode = i.Text("DepExpense"),
                }).ConfigureAwait(true));
                return $"Capitalised as asset {i.Text("Asset")} (id {asset}).";
            },
            List = ProjectsAsync,
        };
    }

    private static Task<long> CountAsync(OperationContext c, string number)
        => FindAsync(c.Db.AssetCounts, x => x.CompanyId == c.CompanyId && x.CountNumber == number, x => x.Id, "asset count", number);

    private static Task<long> ProjectAsync(OperationContext c, string code)
        => FindAsync(c.Db.CapitalProjects, x => x.CompanyId == c.CompanyId && x.Code == code.ToUpper(), x => x.Id, "capital project", code);

    private static async Task<System.Data.DataTable> OpenCountsAsync(OperationContext c)
        => Table(await c.Db.AssetCounts.AsNoTracking().Include(x => x.Lines).Where(x => x.CompanyId == c.CompanyId && !x.IsClosed).OrderBy(x => x.Date).ToListAsync().ConfigureAwait(true),
            ("Count", x => x.CountNumber), ("Date", x => D(x.Date)), ("Location", x => x.Location ?? "everywhere"), ("Expected", x => x.Lines.Count(l => l.Result != AssetCountResult.Unregistered)),
            ("Seen", x => x.Lines.Count(l => l.Result is AssetCountResult.Found or AssetCountResult.FoundElsewhere)), ("Not yet seen", x => x.Lines.Count(l => l.Result == AssetCountResult.NotSeen)));

    private static async Task<System.Data.DataTable> PlansAsync(OperationContext c)
    {
        Dictionary<long, string> assets = await c.Db.FixedAssets.Where(a => a.CompanyId == c.CompanyId).ToDictionaryAsync(a => a.Id, a => a.AssetNumber).ConfigureAwait(true);
        return Table(await c.Db.MaintenancePlans.AsNoTracking().Where(p => p.CompanyId == c.CompanyId && p.IsActive).OrderBy(p => p.NextDue).ToListAsync().ConfigureAwait(true),
            ("Plan", p => p.Id), ("Asset", p => assets.GetValueOrDefault(p.AssetId)), ("Task", p => p.Description), ("Every", p => $"{p.IntervalMonths} months"),
            ("Last done", p => p.LastDone is DateOnly d ? D(d) : "-"), ("Next due", p => D(p.NextDue)));
    }

    private static async Task<System.Data.DataTable> ProjectsAsync(OperationContext c)
        => Table(await c.Db.CapitalProjects.AsNoTracking().Include(p => p.Costs).Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.Code).ToListAsync().ConfigureAwait(true),
            ("Project", p => p.Code), ("Name", p => p.Name), ("Budget", p => M(p.Budget)), ("Cost to date", p => M(p.TotalCost)), ("Status", p => p.IsCapitalised ? "Capitalised" : "In progress"));

    private static IEnumerable<Operation> Contracts()
    {
        const string module = "Contracts";
        yield return new Operation
        {
            Module = module, Title = "Contract revenue, losses and budget", ListTitle = "Job contracts",
            Description = "Recognise a contract's revenue by stage of completion (cost to date over budgeted cost) against certified billing - the difference to the contract asset "
                + "or liability; provide for a foreseeable loss (the whole expected loss at once, the provision trued up); or revise the budgeted cost with the reason.",
            Fields = [Pick("Step", "Step", ["Recognise revenue", "Provide for loss", "Revise budget"]), Req("Contract", "Contract number"), Today(), Opt("AssetAccount", "Contract asset account (revenue)"),
                Opt("LiabilityAccount", "Contract liability account (revenue)"), Opt("LossAccount", "Loss expense account (loss)"), Opt("ProvisionAccount", "Loss provision account (loss)"),
                Opt("Document", "Document number"), OptNum("Budget", "New budgeted cost (revise)"), Opt("Reason", "Why the budget changes (revise)")],
            Run = async (c, i) =>
            {
                IJobCostingService jobs = c.Get<IJobCostingService>();
                long contract = await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true);
                string document = i.Optional("Document") ?? $"{i.Text("Contract").ToUpperInvariant()}-{i.Date("Date"):yyyyMMdd}";
                switch (i.Text("Step"))
                {
                    case "Recognise revenue":
                        RevenueRecognitionResult recognised = Check(await jobs.RecogniseRevenueAsync(c.CompanyId, contract, i.Date("Date"), i.Optional("AssetAccount") ?? string.Empty,
                            i.Optional("LiabilityAccount") ?? string.Empty, document).ConfigureAwait(true));
                        return $"{recognised.PercentComplete:0.#}% complete: earned {M(recognised.EarnedToDate)} against {M(recognised.CertifiedToDate)} certified; movement {M(recognised.Movement)}"
                            + (recognised.JournalNumber is string j ? $" (journal {j})." : ".");
                    case "Provide for loss":
                        ExpectedLossResult loss = Check(await jobs.ProvideForExpectedLossAsync(c.CompanyId, contract, i.Date("Date"), i.Optional("LossAccount") ?? string.Empty,
                            i.Optional("ProvisionAccount") ?? string.Empty, document).ConfigureAwait(true));
                        return $"Expected loss {M(loss.ExpectedLoss)} (provided before {M(loss.PriorProvision)}); movement {M(loss.Movement)}" + (loss.JournalNumber is string lj ? $" (journal {lj})." : ".");
                    default:
                        return Check(await jobs.ReviseBudgetAsync(c.CompanyId, contract, i.OptionalNumber("Budget") ?? throw new OperationException("Give the new budgeted cost."),
                            i.Optional("Reason") ?? string.Empty).ConfigureAwait(true), "Budget revised.");
                }
            },
            List = async c => Table(await c.Db.Set<JobContract>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.ContractNumber).Page(c, 300).ToListAsync().ConfigureAwait(true),
                ("Contract", x => x.ContractNumber), ("Name", x => x.Name), ("Value", x => M(x.ContractValue)), ("Budget", x => M(x.BudgetCost))),
        };
        yield return new Operation
        {
            Module = module, Title = "Open a job contract", ListTitle = "Job contracts",
            Description = "A contract (job) for a customer: its value, budgeted cost, retention % on certificates and the revenue account. The WBS, BOQ, variations, certificates and "
                + "subcontracts all hang off it. Capture a cost books a manual cost to one of its heads (purchases, stock, labour, plant, subcontract, overhead).",
            Fields = [Pick("Step", "Step", ["Open", "Capture a cost"]), Req("Contract", "Contract number"), Opt("Name", "Name"), CustomerPick("Customer", "Customer", required: false), Num("Value", "Contract value", "0"),
                Num("Budget", "Budgeted cost", "0"), Num("Retention", "Retention %", "0"), Opt("Revenue", "Revenue account"), Opt("Head", "Cost head (capture)"),
                Pick("Source", "Cost from", Enum.GetNames<JobCostSource>()), Num("Amount", "Cost amount", "0"), Opt("Reference", "Reference"), Today()],
            Run = async (c, i) =>
            {
                IJobCostingService jobs = c.Get<IJobCostingService>();
                if (i.Text("Step") == "Capture a cost")
                {
                    long contract = await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true);
                    return Check(await jobs.CaptureCostAsync(c.CompanyId, contract, i.Optional("Head") ?? string.Empty, Enum.Parse<JobCostSource>(i.Text("Source")), i.Number("Amount"), i.Date("Date"),
                        i.Optional("Reference")).ConfigureAwait(true), "Cost captured.");
                }

                long id = Check(await jobs.CreateContractAsync(c.CompanyId, i.Text("Contract"), i.Optional("Name") ?? i.Text("Contract"), await CustomerAsync(c, i.Optional("Customer") ?? string.Empty)
                    .ConfigureAwait(true), i.Number("Value"), i.Number("Budget"), i.Number("Retention"), i.Optional("Revenue") ?? string.Empty).ConfigureAwait(true));
                return $"Contract {i.Text("Contract").ToUpperInvariant()} opened (id {id}).";
            },
            List = async c => Table(await c.Db.JobContracts.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.ContractNumber).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Contract", x => x.ContractNumber), ("Name", x => x.Name), ("Value", x => M(x.ContractValue)), ("Budget", x => M(x.BudgetCost)), ("Status", x => x.Status)),
        };
        yield return new Operation
        {
            Module = module, Title = "Job cost position and overhead rates", ListTitle = "Overhead rates",
            Description = "A job's actual cost (purchases, goods received for it, stock issues, labour, plant, subcontractors, overheads), what is still on order for it, and the "
                + "forecast. Overhead rates add a percentage on a kind of direct cost (e.g. 35% on labour) as it is captured; 0% removes a rate.",
            Fields = [Pick("Step", "Step", ["Position", "Set overhead rate"]), Opt("Contract", "Contract number (position)"),
                Pick("On", "Overhead charged on", [.. Enum.GetNames<Core.JobCosting.JobCostSource>().Where(n => n != nameof(Core.JobCosting.JobCostSource.Overhead))]),
                OptNum("Percent", "Overhead %"), Opt("Head", "Overhead cost head", "Overheads")],
            Run = async (c, i) =>
            {
                Application.Abstractions.JobCosting.IJobCostingService jobs = c.Get<Application.Abstractions.JobCosting.IJobCostingService>();
                if (i.Text("Step") == "Set overhead rate")
                {
                    return Check(await jobs.SetOverheadRateAsync(c.CompanyId, Enum.Parse<Core.JobCosting.JobCostSource>(i.Text("On")), i.OptionalNumber("Percent") ?? 0m, i.Optional("Head") ?? string.Empty)
                        .ConfigureAwait(true), "Overhead rate saved.");
                }

                string number = i.Optional("Contract") ?? throw new OperationException("Type the contract number.");
                long contract = await FindAsync(c.Db.JobContracts, j => j.CompanyId == c.CompanyId && j.ContractNumber.ToUpper() == number.ToUpper(), j => j.Id, "contract", number).ConfigureAwait(true);
                Application.Abstractions.JobCosting.JobProfitability p = Check(await jobs.GetProfitabilityAsync(c.CompanyId, contract).ConfigureAwait(true));
                var bySource = await c.Db.JobContracts.AsNoTracking().Where(j => j.Id == contract).SelectMany(j => j.Costs).GroupBy(e => e.Source)
                    .Select(g => new { g.Key, Amount = g.Sum(e => e.Amount) }).ToListAsync().ConfigureAwait(true);
                return $"Value {M(p.ContractValue)}, budget {M(p.BudgetCost)}, actual {M(p.ActualCost)} (overheads {M(p.Overheads)}), on order {M(p.CommittedCost)}, "
                    + $"actual + committed {M(p.CostToDateAndCommitted)}, {p.PercentComplete:0.##}% complete{Environment.NewLine}"
                    + string.Join(Environment.NewLine, bySource.OrderBy(x => x.Key).Select(x => $"  {x.Key,-16} {M(x.Amount),14}"));
            },
            List = async c => Table(await c.Db.Set<Core.JobCosting.JobOverheadRate>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderBy(r => r.AppliesTo).ToListAsync().ConfigureAwait(true),
                ("On", r => r.AppliesTo), ("Percent", r => r.Percent), ("Cost head", r => r.CostHead)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a WBS element", ListTitle = "Work breakdown structure",
            Description = "Phases (no cost head) group activities; an activity carries a budget and the cost head its costs are captured under. Codes are dotted: 1, 1.2, 1.2.3.",
            Fields = [Req("Contract", "Contract number"), Req("Code", "WBS code"), Req("Name", "Name"), OptNum("Budget", "Budget (activities)"), Opt("CostHead", "Cost head (activities)")],
            Run = async (c, i) => Check(await c.Get<IJobWbsService>().AddElementAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true), i.Text("Code"), i.Text("Name"),
                i.OptionalNumber("Budget") ?? 0m, i.Optional("CostHead")).ConfigureAwait(true), id => $"WBS element {i.Text("Code")} added."),
            List = WbsElementsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Give an estimate to complete", ListTitle = "Work breakdown structure",
            Description = "The site's estimate of what an activity still needs beyond actual and committed cost. Leave blank to fall back to the budget left.",
            Fields = [Req("Contract", "Contract number"), Req("Code", "Activity code"), OptNum("Estimate", "Estimate to complete"), Today()],
            Run = async (c, i) => Check(await c.Get<IJobWbsService>().SetEstimateAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true), i.Text("Code"), i.OptionalNumber("Estimate"),
                i.Date("Date")).ConfigureAwait(true), "Estimate recorded."),
            List = WbsElementsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Cost to complete by WBS", ListTitle = "Work breakdown structure",
            Description = "Budget, actual, committed (open subcontracts), estimate to complete and forecast final cost per phase and activity.",
            Fields = [Req("Contract", "Contract number")],
            Run = async (c, i) =>
            {
                WbsBreakdown b = Check(await c.Get<IJobWbsService>().BreakdownAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true)).ConfigureAwait(true));
                IEnumerable<string> rows = b.Lines.Append(b.Total).Select(l => $"{new string(' ', Math.Max(0, l.Level - 1) * 2)}{l.Code} {l.Name}: budget {M(l.Budget)}, actual {M(l.Actual)}, committed {M(l.Committed)}, " +
                    $"to complete {M(l.EstimateToComplete)}, forecast {M(l.ForecastFinalCost)}, variance {M(l.Variance)}");
                return $"{b.ContractNumber} (contract budget {M(b.ContractBudget)}){Environment.NewLine}{string.Join(Environment.NewLine, rows)}";
            },
            List = WbsElementsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Submit a variation order", ListTitle = "Variation orders", Description = "A change to the contract's scope, with its effect on value and budgeted cost.",
            Fields = [Req("Contract", "Contract number"), Req("Number", "VO number"), Req("Description", "What changes"), Num("Value", "Value impact"), Num("Cost", "Cost impact"), Today()],
            Run = async (c, i) => Check(await c.Get<IContractControlService>().SubmitVariationAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true), i.Text("Number"), i.Text("Description"),
                i.Number("Value"), i.Number("Cost"), i.Date("Date")).ConfigureAwait(true), $"Variation {i.Text("Number").ToUpperInvariant()} submitted."),
            List = VariationsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Decide a variation order", ListTitle = "Variation orders", Description = "Approve (moves the contract value and budget) or reject a submitted variation.",
            Fields = [Req("Contract", "Contract number"), Req("Number", "VO number"), Pick("Decision", "Decision", "Approve", "Reject"), Today(), Opt("Note", "Note")],
            Run = async (c, i) =>
            {
                long contract = await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true);
                long vo = await FindAsync(c.Db.VariationOrders, v => v.JobContractId == contract && v.Number == i.Text("Number").ToUpper(), v => v.Id, "variation", i.Text("Number")).ConfigureAwait(true);
                return Check(await c.Get<IContractControlService>().DecideVariationAsync(c.CompanyId, vo, i.Text("Decision") == "Approve", i.Date("Date"), c.UserId, i.Optional("Note")).ConfigureAwait(true),
                    $"Variation {i.Text("Number").ToUpperInvariant()} {(i.Text("Decision") == "Approve" ? "approved" : "rejected")}.");
            },
            List = VariationsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Client certificates, advance and retention", ListTitle = "Contract positions",
            Description = "Certify progress (cumulative work, approved variation work and materials on site; retention held and any client advance recovered at its percentage), "
                + "receive a client advance, record practical completion with the defects-liability period, and release retention - half after completion, the rest when the period ends.",
            Fields = [Pick("Step", "Step", ["Certify", "Receive advance", "Practical completion", "Release retention"]), Req("Contract", "Contract number"), Opt("Number", "Document number"), Today(),
                OptNum("Work", "Work done to date"), OptNum("Variations", "Variation work to date"), OptNum("Materials", "Materials on site"), Opt("RetentionAccount", "Retention receivable account"),
                OptNum("Amount", "Amount (advance / release)"), OptNum("Percent", "Advance recovery %"), Opt("AdvanceAccount", "Advance liability account"), Opt("Bank", "Bank account"),
                OptNum("Months", "Defects-liability months")],
            Run = async (c, i) =>
            {
                Application.Abstractions.JobCosting.IJobCostingService jobs = c.Get<Application.Abstractions.JobCosting.IJobCostingService>();
                long contract = await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true);
                string number = i.Optional("Number") ?? string.Empty;
                return i.Text("Step") switch
                {
                    "Certify" => Check(await jobs.CertifyProgressAsync(c.CompanyId, contract, number, i.Date("Date"), i.OptionalNumber("Work") ?? 0m, i.OptionalNumber("Materials") ?? 0m,
                        i.Optional("RetentionAccount") ?? string.Empty, default, i.OptionalNumber("Variations") ?? 0m).ConfigureAwait(true),
                        r => $"Certificate {number.ToUpperInvariant()}: gross {M(r.Gross)}, retention {M(r.Retention)}, advance recovered {M(r.AdvanceRecovered)}, payable {M(r.NetPayable)}."),
                    "Receive advance" => Check(await jobs.ReceiveAdvanceAsync(c.CompanyId, contract, i.OptionalNumber("Amount") ?? 0m, i.OptionalNumber("Percent") ?? 0m, i.Optional("AdvanceAccount") ?? string.Empty,
                        i.Optional("Bank") ?? string.Empty, i.Date("Date"), number).ConfigureAwait(true), j => $"Advance received (journal {j})."),
                    "Practical completion" => Check(await jobs.SetPracticalCompletionAsync(c.CompanyId, contract, i.Date("Date"), (int)(i.OptionalNumber("Months") ?? 12m)).ConfigureAwait(true),
                        "Practical completion recorded."),
                    _ => Check(await jobs.ReleaseRetentionAsync(c.CompanyId, contract, i.OptionalNumber("Amount") ?? 0m, i.Date("Date"), i.Optional("RetentionAccount") ?? string.Empty, number).ConfigureAwait(true),
                        r => $"Retention of {M(i.OptionalNumber("Amount") ?? 0m)} released."),
                };
            },
            List = async c => Table(await c.Db.Set<Core.JobCosting.JobContract>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.ContractNumber).ToListAsync().ConfigureAwait(true),
                ("Contract", x => x.ContractNumber), ("Certified", x => M(x.CertifiedToDate)), ("Retention held", x => M(x.RetentionHeld)), ("Released", x => M(x.RetentionReleased)),
                ("Advance left", x => M(x.AdvanceOutstanding)), ("Completed", x => x.PracticalCompletionDate is DateOnly d ? D(d) : "-"),
                ("Defects end", x => x.DefectsLiabilityEnds is DateOnly e ? D(e) : "-")),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a BOQ line", Description = "A bill-of-quantities line on a contract.",
            Fields = [Req("Contract", "Contract number"), Req("Item", "BOQ item ref"), Req("Description", "Work"), Req("Unit", "Unit", "m3"), Num("Quantity", "BOQ quantity"), Num("Rate", "Rate")],
            Run = async (c, i) => Check(await c.Get<IContractControlService>().AddBoqItemAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true), i.Text("Item"), i.Text("Description"),
                i.Text("Unit"), i.Number("Quantity"), i.Number("Rate")).ConfigureAwait(true), "BOQ line added."),
        };
        yield return new Operation
        {
            Module = module, Title = "Import a BOQ", Description = "A CSV file (or lines separated by ';'): item, description, unit, quantity, rate, cost head (optional); a header row and "
                + "quoted descriptions are fine. Untick 'Import' to preview the errors first; any error stops the import. 'By cost head' shows the BOQ budget against measured value and cost.",
            Fields = [Req("Contract", "Contract number"), Opt("File", "CSV file path"), Opt("Lines", "Or lines: ITEM,description,unit,qty,rate,head; …"), Tick("Import", "Import (otherwise preview)"),
                Tick("Heads", "By cost head instead")],
            Run = async (c, i) =>
            {
                IContractControlService service = c.Get<IContractControlService>();
                long contract = await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true);
                if (i.Flag("Heads"))
                {
                    IReadOnlyList<BoqCostHeadRow> heads = Check(await service.BoqByCostHeadAsync(c.CompanyId, contract).ConfigureAwait(true));
                    return string.Join(Environment.NewLine, heads.Select(h => $"{h.CostHead,-20} BOQ {M(h.BoqValue),14}  measured {M(h.MeasuredValue),14}  cost {M(h.ActualCost),14}"));
                }

                string csv = i.Optional("File") is string path ? await System.IO.File.ReadAllTextAsync(path).ConfigureAwait(true) : (i.Optional("Lines") ?? string.Empty).Replace(';', '\n');
                if (i.Flag("Import"))
                {
                    return $"{Check(await service.ImportBoqAsync(c.CompanyId, contract, csv).ConfigureAwait(true))} BOQ line(s) imported.";
                }

                BoqImportPreview preview = Check(await service.PreviewBoqImportAsync(c.CompanyId, contract, csv).ConfigureAwait(true));
                return $"{preview.Lines.Count} line(s), {preview.ErrorCount} with errors, value {M(preview.TotalValue)}:" + Environment.NewLine + string.Join(Environment.NewLine, preview.Lines.Select(l =>
                    $"{l.Line,4}  {l.ItemCode,-10} {l.Description,-30} {l.Unit,-6} {(l.Quantity is decimal q ? Q(q) : "-"),10} {(l.Rate is decimal r ? M(r) : "-"),12} {l.CostHead ?? "",-12} {l.Error ?? "OK"}"));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Measure a BOQ line", Description = "Records the quantity measured to date (never earlier than the last measurement).",
            Fields = [Req("Contract", "Contract number"), Req("Item", "BOQ item ref"), Num("Quantity", "Measured to date"), Today()],
            Run = async (c, i) =>
            {
                long contract = await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true);
                long boq = await FindAsync(c.Db.BoqItems, b => b.JobContractId == contract && b.ItemCode == i.Text("Item").ToUpper(), b => b.Id, "BOQ item", i.Text("Item")).ConfigureAwait(true);
                return Check(await c.Get<IContractControlService>().MeasureAsync(c.CompanyId, boq, i.Number("Quantity"), i.Date("Date")).ConfigureAwait(true), "Measured.");
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Book timesheet hours to a job", Description = "Hours × rate into the job's labour head (at most 24 hours a day per employee).",
            Fields = [Req("Contract", "Contract number"), EmployeePick("Employee", "Employee"), Today(), Num("Hours", "Hours"), Num("Rate", "Cost per hour"), Opt("Head", "Cost head", "Labour")],
            Run = async (c, i) => Check(await c.Get<IContractControlService>().RecordTimeAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true), await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true),
                i.Date("Date"), i.Number("Hours"), i.Number("Rate"), i.Optional("Head")).ConfigureAwait(true), "Hours booked."),
        };
        yield return new Operation
        {
            Module = module, Title = "Issue materials to a job", Description = "Stock out of the warehouse to the contract's WIP account, into the job's materials head.",
            Fields = [Req("Contract", "Contract number"), ItemPick("Item", "Product"), Req("Warehouse", "Warehouse / site store"), Num("Quantity", "Quantity"), Today(), Req("Wip", "Contract WIP account")],
            Run = async (c, i) => $"Issued at a cost of {M(Check(await c.Get<IContractControlService>().IssueMaterialsAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true),
                await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), i.Date("Date"), i.Number("Quantity"), i.Text("Wip")).ConfigureAwait(true)))}.",
        };
        yield return new Operation
        {
            Module = module, Title = "Let a subcontract", ListTitle = "Subcontracts", Description = "A package let to a supplier on a contract, with the retention held on its certificates.",
            Fields = [Req("Contract", "Contract number"), Req("Supplier", "Subcontractor (supplier code)"), Req("Reference", "Subcontract reference"), Req("Description", "Package"), Num("Value", "Value"), Num("Retention", "Retention %", "10"),
                Opt("CostHead", "Job cost head (default Subcontract)")],
            Run = async (c, i) => Check(await c.Get<ISubcontractService>().LetAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true), await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true),
                i.Text("Reference"), i.Text("Description"), i.Number("Value"), i.Number("Retention"), i.Optional("CostHead")).ConfigureAwait(true), $"Subcontract {i.Text("Reference").ToUpperInvariant()} let."),
            List = SubcontractsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Pay a subcontract advance", ListTitle = "Subcontracts",
            Description = "A mobilisation advance (Dr advance account, Cr bank), recovered as a share of each later certificate until cleared.",
            Fields = [Req("Subcontract", "Subcontract reference"), Num("Amount", "Advance"), Num("Recovery", "Recovered per certificate %", "20"), Today(), Req("Bank", "Bank account", "1100"),
                Req("AdvanceAccount", "Advances to subcontractors account"), Req("Document", "Payment document number")],
            Run = async (c, i) => $"Advance paid, journal {Check(await c.Get<ISubcontractService>().PayAdvanceAsync(c.CompanyId, await SubcontractAsync(c, i.Text("Subcontract")).ConfigureAwait(true), i.Number("Amount"),
                i.Number("Recovery"), i.Date("Date"), i.Text("Bank"), i.Text("AdvanceAccount"), i.Text("Document")).ConfigureAwait(true))}.",
            List = SubcontractsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Set subcontract withholding tax", ListTitle = "Subcontracts",
            Description = "Withholding tax deducted from each certificate unless the subcontractor's tax clearance is valid. Leave blank to stop withholding.",
            Fields = [Req("Subcontract", "Subcontract reference"), Opt("Code", "Withholding tax code")],
            Run = async (c, i) => Check(await c.Get<ISubcontractService>().SetWithholdingAsync(c.CompanyId, await SubcontractAsync(c, i.Text("Subcontract")).ConfigureAwait(true), i.Optional("Code")).ConfigureAwait(true),
                "Withholding updated."),
            List = SubcontractsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Certify a subcontract", ListTitle = "Subcontracts", Description = "Invoices the subcontractor for the work since the last certificate, net of retention.",
            Fields = [Req("Subcontract", "Subcontract reference"), Req("Certificate", "Certificate number"), Today(), Num("ToDate", "Work done to date"), Req("CostAccount", "Job cost account", "5000"),
                Req("RetentionAccount", "Retention payable account")],
            Run = async (c, i) =>
            {
                SubcontractCertificateResult cert = Check(await c.Get<ISubcontractService>().CertifyAsync(new SubcontractCertificateRequest
                {
                    CompanyId = c.CompanyId, SubcontractId = await SubcontractAsync(c, i.Text("Subcontract")).ConfigureAwait(true), Number = i.Text("Certificate"), Date = i.Date("Date"),
                    WorkDoneToDate = i.Number("ToDate"), CostAccountCode = i.Text("CostAccount"), RetentionPayableAccountCode = i.Text("RetentionAccount"),
                }).ConfigureAwait(true));
                return $"Certified gross {M(cert.Gross)}, retention {M(cert.Retention)}, advance recovered {M(cert.AdvanceRecovered)}, tax withheld {M(cert.Withheld)}, payable {M(cert.Payable)}.";
            },
            List = SubcontractsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Release subcontract retention", ListTitle = "Subcontracts", Description = "Invoices held retention to the subcontractor (never more than held).",
            Fields = [Req("Subcontract", "Subcontract reference"), Num("Amount", "Amount"), Today(), Req("RetentionAccount", "Retention payable account"), Req("Document", "Release document number")],
            Run = async (c, i) => Check(await c.Get<ISubcontractService>().ReleaseRetentionAsync(c.CompanyId, await SubcontractAsync(c, i.Text("Subcontract")).ConfigureAwait(true), i.Number("Amount"), i.Date("Date"),
                i.Text("RetentionAccount"), i.Text("Document")).ConfigureAwait(true), "Retention released."),
            List = SubcontractsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Log plant hours / running cost", Description = "Hours on a job charge it at the plant's hire rate; leave the contract blank to log a running cost instead.",
            Fields = [Req("Plant", "Plant code"), Opt("Contract", "Contract number (hours)"), Today(), OptNum("Hours", "Hours"), OptNum("Cost", "Running cost"), Opt("Description", "Running cost description")],
            Run = async (c, i) =>
            {
                long plant = await FindAsync(c.Db.PlantItems, p => p.CompanyId == c.CompanyId && p.Code == i.Text("Plant").ToUpper(), p => p.Id, "plant", i.Text("Plant")).ConfigureAwait(true);
                ISubcontractService service = c.Get<ISubcontractService>();
                return i.Optional("Contract") is string contract
                    ? Check(await service.LogPlantHoursAsync(c.CompanyId, plant, await ContractAsync(c, contract).ConfigureAwait(true), i.Date("Date"), i.Number("Hours")).ConfigureAwait(true), "Hours logged.")
                    : Check(await service.LogPlantCostAsync(c.CompanyId, plant, i.Date("Date"), i.Number("Cost"), i.Text("Description")).ConfigureAwait(true), "Running cost logged.");
            },
        };
    }

    private static Task<long> SubcontractAsync(OperationContext c, string reference)
        => FindAsync(c.Db.Subcontracts, s => s.CompanyId == c.CompanyId && s.Reference == reference.ToUpper(), s => s.Id, "subcontract", reference);

    private static async Task<System.Data.DataTable> VariationsAsync(OperationContext c)
    {
        Dictionary<long, string> contracts = await c.Db.JobContracts.Where(x => x.CompanyId == c.CompanyId).ToDictionaryAsync(x => x.Id, x => x.ContractNumber).ConfigureAwait(true);
        return Table(await c.Db.VariationOrders.AsNoTracking().Where(v => v.CompanyId == c.CompanyId).OrderByDescending(v => v.Id).Take(50).ToListAsync().ConfigureAwait(true),
            ("Contract", v => contracts.GetValueOrDefault(v.JobContractId)), ("VO", v => v.Number), ("Description", v => v.Description), ("Submitted", v => D(v.SubmittedOn)), ("Status", v => v.Status),
            ("Value", v => M(v.ValueImpact)), ("Cost", v => M(v.CostImpact)));
    }

    private static async Task<System.Data.DataTable> SubcontractsAsync(OperationContext c)
    {
        Dictionary<long, string> contracts = await c.Db.JobContracts.Where(x => x.CompanyId == c.CompanyId).ToDictionaryAsync(x => x.Id, x => x.ContractNumber).ConfigureAwait(true);
        return Table(await c.Db.Subcontracts.AsNoTracking().Include(s => s.Certificates).Where(s => s.CompanyId == c.CompanyId).OrderBy(s => s.Reference).ToListAsync().ConfigureAwait(true),
            ("Subcontract", s => s.Reference), ("Contract", s => contracts.GetValueOrDefault(s.JobContractId)), ("Package", s => s.Description), ("Value", s => M(s.Value)),
            ("Certified", s => M(s.CertifiedToDate)), ("Retention held", s => M(s.RetentionHeld)));
    }

    private static async Task<DataTable> WbsElementsAsync(OperationContext c)
    {
        Dictionary<long, string> contracts = await c.Db.JobContracts.AsNoTracking().Where(j => j.CompanyId == c.CompanyId).ToDictionaryAsync(j => j.Id, j => j.ContractNumber).ConfigureAwait(true);
        return Table((await c.Db.JobWbsElements.AsNoTracking().Where(e => e.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true)).OrderBy(e => contracts.GetValueOrDefault(e.JobContractId)).ThenBy(e => e.Code),
            ("Contract", e => contracts.GetValueOrDefault(e.JobContractId)), ("Code", e => e.Code), ("Name", e => e.Name), ("Cost head", e => e.CostHead), ("Budget", e => M(e.BudgetCost)),
            ("Estimate to complete", e => e.EstimateToComplete is decimal etc ? M(etc) : null));
    }

    // Fixes a barcode / tag number to an asset; a tag already on another asset is refused.
    private static async Task TagAsync(OperationContext c, long assetId, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        string code = tag.Trim().ToUpperInvariant();
        if (await c.Db.FixedAssets.AnyAsync(a => a.CompanyId == c.CompanyId && a.TagNumber == code && a.Id != assetId).ConfigureAwait(true))
        {
            throw new OperationException($"Tag {code} is already on another asset.");
        }

        Core.FixedAssets.FixedAsset asset = await c.Db.FixedAssets.FirstAsync(a => a.Id == assetId).ConfigureAwait(true);
        asset.TagNumber = code;
        await c.Db.SaveChangesAsync().ConfigureAwait(true);
    }
}

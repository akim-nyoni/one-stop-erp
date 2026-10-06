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
            Module = module, Title = "First weigh (open a ticket)", ListTitle = "Tickets still on the bridge",
            Description = "Outbound loads weigh empty first (leave the weight blank to use the vehicle's stored tare); inbound loads weigh loaded first. A keyed weight needs a reason and another person's authority.",
            Fields = [Pick("Direction", "Direction", "Outbound", "Inbound"), Req("Vehicle", "Vehicle registration"), Req("Item", "Product (item code)"), Num("Density", "m³ per tonne", "0.6"), Today(),
                OptNum("Weight", "Weight kg (blank = stored tare)"), Opt("Customer", "Customer code"), Opt("Supplier", "Supplier code"), Opt("Driver", "Driver"), Opt("Site", "Delivery site"),
                Opt("Stockpile", "Stockpile (warehouse code)"), OptNum("Distance", "Haulage distance km"), Opt("Batch", "Concrete batch run"), Tick("Manual", "Weight keyed by hand"), Opt("Reason", "Why keyed"),
                Opt("AuthorisedBy", "Authorised by (user name)")],
            Run = async (c, i) =>
            {
                var (_, number) = Check(await c.Get<IWeighbridgeService>().FirstWeighAsync(new FirstWeighRequest
                {
                    CompanyId = c.CompanyId, Direction = Enum.Parse<WeighDirection>(i.Text("Direction")), Vehicle = i.Text("Vehicle"), ItemId = await ItemAsync(c, i.Text("Item")).ConfigureAwait(true),
                    CubicMetresPerTonne = i.Number("Density"), Date = i.Date("Date"), WeightKg = i.OptionalNumber("Weight"),
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
            Description = "Completes the ticket: net weight, haulage by zone and — for an outbound load from a stockpile — the stock issue.",
            Fields = [Req("Ticket", "Ticket number"), Num("Weight", "Weight kg"), Num("Deduction", "Deduction kg (moisture etc.)", "0"), Tick("Manual", "Weight keyed by hand"), Opt("Reason", "Why keyed"),
                Opt("AuthorisedBy", "Authorised by (user name)")],
            Run = async (c, i) =>
            {
                long ticket = await FindAsync(c.Db.WeighbridgeTickets, t => t.CompanyId == c.CompanyId && t.TicketNumber == i.Text("Ticket").ToUpper(), t => t.Id, "ticket", i.Text("Ticket")).ConfigureAwait(true);
                WeighbridgeCompletion done = Check(await c.Get<IWeighbridgeService>().SecondWeighAsync(new SecondWeighRequest
                {
                    CompanyId = c.CompanyId, TicketId = ticket, WeightKg = i.Number("Weight"), DeductionKg = i.Number("Deduction"), OperatorUserId = c.UserId, IsManual = i.Flag("Manual"),
                    ManualReason = i.Optional("Reason"), AuthorisedByUserId = await OptionalUserAsync(c, i.Optional("AuthorisedBy")).ConfigureAwait(true),
                }).ConfigureAwait(true));
                return $"Net {Q(done.NetTonnes)} t ({Q(done.NetCubicMetres)} m³), haulage {M(done.HaulageCharge)}." + (done.Overloaded ? " OVERLOADED — over the vehicle's legal maximum." : string.Empty);
            },
            List = OpenTicketsAsync,
        };
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
                return $"Count closed — {missing} asset(s) missing. See report FA210.";
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
            ("Last done", p => p.LastDone is DateOnly d ? D(d) : "—"), ("Next due", p => D(p.NextDue)));
    }

    private static async Task<System.Data.DataTable> ProjectsAsync(OperationContext c)
        => Table(await c.Db.CapitalProjects.AsNoTracking().Include(p => p.Costs).Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.Code).ToListAsync().ConfigureAwait(true),
            ("Project", p => p.Code), ("Name", p => p.Name), ("Budget", p => M(p.Budget)), ("Cost to date", p => M(p.TotalCost)), ("Status", p => p.IsCapitalised ? "Capitalised" : "In progress"));

    private static IEnumerable<Operation> Contracts()
    {
        const string module = "Contracts";
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
            Module = module, Title = "Add a BOQ line", Description = "A bill-of-quantities line on a contract.",
            Fields = [Req("Contract", "Contract number"), Req("Item", "BOQ item ref"), Req("Description", "Work"), Req("Unit", "Unit", "m3"), Num("Quantity", "BOQ quantity"), Num("Rate", "Rate")],
            Run = async (c, i) => Check(await c.Get<IContractControlService>().AddBoqItemAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true), i.Text("Item"), i.Text("Description"),
                i.Text("Unit"), i.Number("Quantity"), i.Number("Rate")).ConfigureAwait(true), "BOQ line added."),
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
            Fields = [Req("Contract", "Contract number"), Req("Employee", "Employee number"), Today(), Num("Hours", "Hours"), Num("Rate", "Cost per hour"), Opt("Head", "Cost head", "Labour")],
            Run = async (c, i) => Check(await c.Get<IContractControlService>().RecordTimeAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true), await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true),
                i.Date("Date"), i.Number("Hours"), i.Number("Rate"), i.Optional("Head")).ConfigureAwait(true), "Hours booked."),
        };
        yield return new Operation
        {
            Module = module, Title = "Issue materials to a job", Description = "Stock out of the warehouse to the contract's WIP account, into the job's materials head.",
            Fields = [Req("Contract", "Contract number"), Req("Item", "Item code"), Req("Warehouse", "Warehouse / site store"), Num("Quantity", "Quantity"), Today(), Req("Wip", "Contract WIP account")],
            Run = async (c, i) => $"Issued at a cost of {M(Check(await c.Get<IContractControlService>().IssueMaterialsAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true),
                await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), i.Date("Date"), i.Number("Quantity"), i.Text("Wip")).ConfigureAwait(true)))}.",
        };
        yield return new Operation
        {
            Module = module, Title = "Let a subcontract", ListTitle = "Subcontracts", Description = "A package let to a supplier on a contract, with the retention held on its certificates.",
            Fields = [Req("Contract", "Contract number"), Req("Supplier", "Subcontractor (supplier code)"), Req("Reference", "Subcontract reference"), Req("Description", "Package"), Num("Value", "Value"), Num("Retention", "Retention %", "10")],
            Run = async (c, i) => Check(await c.Get<ISubcontractService>().LetAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true), await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true),
                i.Text("Reference"), i.Text("Description"), i.Number("Value"), i.Number("Retention")).ConfigureAwait(true), $"Subcontract {i.Text("Reference").ToUpperInvariant()} let."),
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
                return $"Certified gross {M(cert.Gross)}, retention {M(cert.Retention)}, payable {M(cert.Payable)}.";
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
}

using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Documents;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Banking;
using One4All.Core.Payroll;
using One4All.Core.System;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>HR and attendance, banking, sales and schools operations, and the document printer.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> People()
    {
        const string module = "HR & Attendance";
        yield return new Operation
        {
            Module = module, Title = "Termination and final pay", ListTitle = "Clearance checklist",
            Description = "Preview the final pay (last month pro rata, notice, leave, gratuity, severance by the termination type; PAYE on ordinary pay, the terminal rate on benefits; loans "
                + "recovered as far as the pay allows), then terminate — the final pay posts in one journal and later runs leave the employee out. Clear checklist items by id; print the certificate.",
            Fields = [Pick("Step", "Step", ["Preview", "Terminate", "Clear item", "Certificate of service"]), Req("Number", "Employee number"), Pick("Type", "Type", Enum.GetNames<Application.Payroll.TerminationType>()),
                Today("LastDay", "Last day"), Opt("Reason", "Reason"), Num("NoticeMonths", "Notice (months)", "1"), Num("WorkingDays", "Working days a month", "22"), Num("GratuityWeeks", "Gratuity weeks a year", "2"),
                Num("SeveranceWeeks", "Severance weeks a year", "4"), Num("TerminalTax", "Terminal benefits tax %", "0"), Req("Salary", "Salary expense account", "6100"),
                Req("Benefits", "Terminal benefits account", "6100"), Req("Paye", "PAYE liability account", "2400"), Req("NetPay", "Net pay account", "2420"), OptNum("Item", "Checklist item id")],
            Run = async (c, i) =>
            {
                ITerminationService terminations = c.Get<ITerminationService>();
                switch (i.Text("Step"))
                {
                    case "Clear item":
                        _ = Check(await terminations.ClearItemAsync(c.CompanyId, (long)(i.OptionalNumber("Item") ?? 0), c.UserId, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true), string.Empty);
                        return "Item cleared.";
                    case "Certificate of service":
                        return Check(await terminations.CertificateOfServiceAsync(c.CompanyId, i.Text("Number")).ConfigureAwait(true));
                }

                TerminationRequest request = new(c.CompanyId, i.Text("Number"), Enum.Parse<Application.Payroll.TerminationType>(i.Text("Type")), i.Date("LastDay"), i.Optional("Reason") ?? string.Empty)
                {
                    Policy = new Application.Payroll.TerminalPolicy(i.Number("NoticeMonths"), i.Number("WorkingDays"), i.Number("GratuityWeeks"), i.Number("SeveranceWeeks"), i.Number("TerminalTax")),
                    SalaryExpenseAccount = i.Text("Salary"), TerminalBenefitsAccount = i.Text("Benefits"), PayeLiabilityAccount = i.Text("Paye"), NetPayAccount = i.Text("NetPay"), ApprovedByUserId = c.UserId,
                };
                Application.Payroll.FinalPayStatement statement = i.Text("Step") == "Preview"
                    ? Check(await terminations.PreviewAsync(request).ConfigureAwait(true))
                    : Check(await terminations.TerminateAsync(request).ConfigureAwait(true)).Statement;
                string earned = FormattableString.Invariant(
                    $"{statement.YearsOfService} year(s). Salary {statement.ProRataSalary:N2} + notice {statement.Notice:N2} + leave ({statement.LeaveDays:0.##} days) {statement.Leave:N2} + gratuity {statement.Gratuity:N2} + severance {statement.Severance:N2} ");
                string less = FormattableString.Invariant($"− PAYE {statement.OrdinaryTax:N2} − terminal tax {statement.TerminalTax:N2} − loans {statement.Recoveries:N2} = net {statement.Net:N2}");
                string owed = statement.UnrecoveredLoans > 0m ? FormattableString.Invariant($"; loans still owed {statement.UnrecoveredLoans:N2}.") : ".";
                string working = earned + less + owed;
                return i.Text("Step") == "Preview" ? "Preview: " + working : "Terminated. " + working;
            },
            List = async c => Table(await c.Db.Set<Termination>().AsNoTracking().Where(t => t.CompanyId == c.CompanyId).OrderByDescending(t => t.Id).Take(50)
                    .SelectMany(t => t.Clearance.Select(item => new { t.EmployeeId, t.LastDay, item.Id, item.Description, item.IsCleared })).ToListAsync().ConfigureAwait(true),
                ("Item", x => x.Id), ("Employee id", x => x.EmployeeId), ("Left", x => D(x.LastDay)), ("To clear", x => x.Description), ("Cleared", x => x.IsCleared)),
        };
        yield return new Operation
        {
            Module = module, Title = "Employee record", ListTitle = "Employees",
            Description = "Engage an employee, or update one (blank fields stay as they are). Bank details are set at engagement; later changes go through 'Bank detail change' "
                + "and a second person's approval.",
            Fields = [Pick("Step", "Step", ["Engage", "Update"]), Req("Number", "Employee number"), Opt("Name", "Full name"), OptNum("Basic", "Basic salary"), Today("Engaged", "Engagement date"),
                Pick("Type", "Employment", Enum.GetNames<EmploymentType>()), Opt("Department", "Department code (3)"), Opt("CostCentre", "Cost centre code (3)"), Opt("PayGroup", "Pay group"), Opt("Grade", "Grade"),
                Opt("NationalId", "National ID"), Opt("TaxNumber", "Tax number"), Opt("ContractEnd", "Contract end (yyyy-MM-dd)"), Pick("Method", "Paid by", Enum.GetNames<PayMethod>()),
                Opt("Bank", "Bank (engage only)"), Opt("Branch", "Branch (engage only)"), Opt("Account", "Account number (engage only)"), Opt("Mobile", "Mobile money (engage only)"), Opt("Email", "Email")],
            Run = async (c, i) =>
            {
                IPayrollSetupService setup = c.Get<IPayrollSetupService>();
                DateOnly? contractEnd = i.Optional("ContractEnd") is string end ? DateOnly.ParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
                EmploymentType type = Enum.Parse<EmploymentType>(i.Text("Type"));
                PayMethod method = Enum.Parse<PayMethod>(i.Text("Method"));
                if (i.Text("Step") == "Update")
                {
                    _ = Check(await setup.UpdateEmployeeAsync(c.CompanyId, i.Text("Number"), new EmployeeUpdate
                    {
                        FullName = i.Optional("Name"), BasicSalary = i.OptionalNumber("Basic"), EmploymentType = type, Department = i.Optional("Department"), CostCentre = i.Optional("CostCentre"),
                        PayGroup = i.Optional("PayGroup"), Grade = i.Optional("Grade"), NationalIdNumber = i.Optional("NationalId"), TaxNumber = i.Optional("TaxNumber"), ContractEndDate = contractEnd,
                        PaymentMethod = method, Email = i.Optional("Email"),
                    }).ConfigureAwait(true), string.Empty);
                    return $"Employee {i.Text("Number").ToUpperInvariant()} updated.";
                }

                long id = Check(await setup.CreateEmployeeAsync(c.CompanyId, new NewEmployee(i.Text("Number"), i.Optional("Name") ?? string.Empty, i.OptionalNumber("Basic") ?? 0m, i.Date("Engaged"))
                {
                    EmploymentType = type, Department = i.Optional("Department"), CostCentre = i.Optional("CostCentre"), PayGroup = i.Optional("PayGroup"), Grade = i.Optional("Grade"),
                    NationalIdNumber = i.Optional("NationalId"), TaxNumber = i.Optional("TaxNumber"), ContractEndDate = contractEnd, PaymentMethod = method, BankName = i.Optional("Bank"),
                    BankBranch = i.Optional("Branch"), BankAccountNumber = i.Optional("Account"), MobileMoneyNumber = i.Optional("Mobile"), Email = i.Optional("Email"),
                }).ConfigureAwait(true));
                return $"Employee {i.Text("Number").ToUpperInvariant()} engaged (id {id}).";
            },
            List = async c => Table(await c.Db.Employees.AsNoTracking().Where(e => e.CompanyId == c.CompanyId).OrderBy(e => e.EmployeeNumber).Take(500).ToListAsync().ConfigureAwait(true),
                ("Number", e => e.EmployeeNumber), ("Name", e => e.FullName), ("Department", e => e.Department ?? string.Empty), ("Grade", e => e.Grade ?? string.Empty),
                ("Type", e => e.EmploymentType), ("Status", e => e.Status), ("Engaged", e => D(e.EngagementDate))),
        };
        yield return new Operation
        {
            Module = module, Title = "Pay elements and tax tables", ListTitle = "Pay elements",
            Description = "Create a pay element, assign one to (or end one for) an employee, create a tax table — bands as 'from-to:rate' separated by ';', the last open-ended, e.g. "
                + "'0-100:0; 100-300:20; 300-:30' — or add the standard elements (BASIC, OVERTIME, HOUSING, TRANSPORT, PAYE on the tax table).",
            Fields = [Pick("Step", "Step", ["Create element", "Assign to employee", "End for employee", "Create tax table", "Add standard elements"]), Opt("Code", "Element / table code"),
                Opt("Name", "Element name"), Pick("Type", "Element type", Enum.GetNames<PayElementType>()), Pick("Basis", "Calculation", Enum.GetNames<CalculationBasis>()), OptNum("Sequence", "Sequence"),
                Opt("Account", "Account (salaries expense for the standard set)"), Opt("Liability", "PAYE liability account (standard set)"), OptNum("Amount", "Fixed / override amount"),
                OptNum("Rate", "Rate / override rate"), OptNum("Units", "Units"), Opt("Formula", "Formula"), Tick("Taxable", "Taxable"), Opt("TaxTable", "Tax table code"),
                Opt("Employee", "Employee number"), Today("From", "Tax table effective from"), Opt("Bands", "Tax bands")],
            Run = async (c, i) =>
            {
                IPayrollSetupService setup = c.Get<IPayrollSetupService>();
                switch (i.Text("Step"))
                {
                    case "Assign to employee":
                        _ = Check(await setup.AssignElementAsync(c.CompanyId, i.Optional("Employee") ?? string.Empty, i.Optional("Code") ?? string.Empty, i.OptionalNumber("Amount"), i.OptionalNumber("Rate"),
                            i.OptionalNumber("Units")).ConfigureAwait(true), string.Empty);
                        return "Element assigned.";
                    case "End for employee":
                        _ = Check(await setup.EndElementAsync(c.CompanyId, i.Optional("Employee") ?? string.Empty, i.Optional("Code") ?? string.Empty).ConfigureAwait(true), string.Empty);
                        return "Element ended.";
                    case "Create tax table":
                        List<TaxBandInput> bands = [];
                        foreach (string band in (i.Optional("Bands") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] parts = band.Split(':', StringSplitOptions.TrimEntries);
                            string[] range = parts[0].Split('-', StringSplitOptions.TrimEntries);
                            if (parts.Length != 2 || range.Length != 2)
                            {
                                throw new OperationException($"'{band}' is not from-to:rate.");
                            }

                            bands.Add(new TaxBandInput(decimal.Parse(range[0], CultureInfo.InvariantCulture), range[1].Length == 0 ? null : decimal.Parse(range[1], CultureInfo.InvariantCulture),
                                decimal.Parse(parts[1], CultureInfo.InvariantCulture)));
                        }

                        long table = Check(await setup.CreateTaxTableAsync(c.CompanyId, i.Optional("Code") ?? string.Empty, i.Date("From"), null, bands).ConfigureAwait(true));
                        return $"Tax table created (id {table}).";
                    case "Add standard elements":
                        IReadOnlyList<string> added = Check(await setup.SeedStandardElementsAsync(c.CompanyId, i.Optional("Account") ?? string.Empty, i.Optional("Liability") ?? string.Empty,
                            i.Optional("TaxTable") ?? "PAYE").ConfigureAwait(true));
                        return added.Count == 0 ? "The standard elements are all there already." : $"Added {string.Join(", ", added)}.";
                    default:
                        long element = Check(await setup.CreatePayElementAsync(c.CompanyId, new NewPayElement(i.Optional("Code") ?? string.Empty, i.Optional("Name") ?? string.Empty,
                            Enum.Parse<PayElementType>(i.Text("Type")), Enum.Parse<CalculationBasis>(i.Text("Basis")), (int)(i.OptionalNumber("Sequence") ?? 10), i.Optional("Account") ?? string.Empty)
                        {
                            FixedAmount = i.OptionalNumber("Amount") ?? 0m, Rate = i.OptionalNumber("Rate") ?? 0m, Formula = i.Optional("Formula"), Taxable = i.Flag("Taxable"), TaxTableCode = i.Optional("TaxTable"),
                        }).ConfigureAwait(true));
                        return $"Pay element created (id {element}).";
                }
            },
            List = async c => Table(await c.Db.PayElements.AsNoTracking().Where(e => e.CompanyId == c.CompanyId).OrderBy(e => e.Sequence).ThenBy(e => e.Code).ToListAsync().ConfigureAwait(true),
                ("Code", e => e.Code), ("Name", e => e.Name), ("Type", e => e.Type), ("Basis", e => e.Basis), ("Sequence", e => e.Sequence), ("Account", e => e.AccountCode),
                ("Taxable", e => e.Taxable), ("Formula / table", e => e.Formula ?? e.TaxTableCode ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Bank detail change", ListTitle = "Bank changes awaiting approval",
            Description = "Ask for a change of an employee's bank or mobile-money details; a different person approves or rejects it before it takes effect (blank fields are left as they are).",
            Fields = [Pick("Step", "Step", ["Request", "Approve", "Reject"]), Opt("Number", "Employee number"), Opt("Bank", "Bank"), Opt("Branch", "Branch"), Opt("Account", "Account number"),
                Opt("Mobile", "Mobile money number"), OptNum("Id", "Request id (approve / reject)"), Opt("Note", "Note")],
            Run = async (c, i) =>
            {
                IPayrollSetupService setup = c.Get<IPayrollSetupService>();
                if (i.Text("Step") != "Request")
                {
                    _ = Check(await setup.DecideBankChangeAsync(c.CompanyId, (long)(i.OptionalNumber("Id") ?? 0), i.Text("Step") == "Approve", c.UserId, i.Optional("Note")).ConfigureAwait(true), string.Empty);
                    return i.Text("Step") == "Approve" ? "Change approved and applied." : "Change rejected.";
                }

                Dictionary<string, string> changes = [];
                foreach ((string field, string input) in new[] { ("BankName", "Bank"), ("BankBranch", "Branch"), ("BankAccountNumber", "Account"), ("MobileMoneyNumber", "Mobile") })
                {
                    if (i.Optional(input) is string value)
                    {
                        changes[field] = value;
                    }
                }

                IReadOnlyList<long> ids = Check(await setup.RequestBankChangeAsync(c.CompanyId, i.Optional("Number") ?? string.Empty, changes, c.UserId).ConfigureAwait(true));
                return $"Change requested ({string.Join(", ", ids)}); someone else must approve it.";
            },
            List = async c => Table(await c.Get<IPayrollSetupService>().PendingBankChangesAsync(c.CompanyId).ConfigureAwait(true),
                ("Id", r => r.RequestId), ("Employee", r => r.EmployeeNumber), ("Field", r => r.Field), ("From", r => r.OldValue ?? string.Empty), ("To", r => r.NewValue)),
        };
        yield return new Operation
        {
            Module = module, Title = "Record an attendance day", ListTitle = "Overtime awaiting approval", Description = "One record per employee per day: status, clock times, lateness and overtime.",
            Fields = [Req("Employee", "Employee number"), Today(), Pick("Status", "Status", Enum.GetNames<AttendanceStatus>()), Opt("In", "Clock in (HH:mm)"), Opt("Out", "Clock out (HH:mm)"),
                Num("Late", "Minutes late", "0"), Num("Overtime", "Overtime hours", "0"), Num("Multiplier", "Overtime multiplier", "1.5")],
            Run = async (c, i) => Check(await c.Get<IAttendanceService>().RecordDayAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Date("Date"),
                Enum.Parse<AttendanceStatus>(i.Text("Status")), Time(i, "In"), Time(i, "Out"), i.Whole("Late"), i.Number("Overtime"), i.Number("Multiplier")).ConfigureAwait(true), id => $"Day recorded (attendance {id})."),
            List = PendingOvertimeAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Approve overtime", ListTitle = "Overtime awaiting approval", Description = "Overtime only reaches payroll once approved.",
            Fields = [Num("Attendance", "Attendance id (from the list)")],
            Run = async (c, i) => Check(await c.Get<IAttendanceService>().ApproveOvertimeAsync(c.CompanyId, (long)i.Number("Attendance"), c.UserId).ConfigureAwait(true), "Overtime approved."),
            List = PendingOvertimeAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Accrue leave", ListTitle = "Leave balances", Description = "Adds days to an employee's leave balance.",
            Fields = [Req("Employee", "Employee number"), Req("Type", "Leave type", "ANNUAL"), Num("Days", "Days")],
            Run = async (c, i) => $"Balance now {Q(Check(await c.Get<ILeaveService>().AccrueAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Type"), i.Number("Days")).ConfigureAwait(true)))} days.",
            List = LeaveBalancesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Take leave", ListTitle = "Leave balances", Description = "Records leave taken over a period (never more than the balance).",
            Fields = [Req("Employee", "Employee number"), Req("Type", "Leave type", "ANNUAL"), Today("From", "From"), Today("To", "To"), Num("Days", "Days")],
            Run = async (c, i) => Check(await c.Get<IAttendanceService>().TakeLeaveAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Type"), i.Date("From"), i.Date("To"),
                i.Number("Days")).ConfigureAwait(true), "Leave recorded."),
            List = LeaveBalancesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Encash leave", ListTitle = "Leave balances", Description = "Pays out leave days at a daily rate.",
            Fields = [Req("Employee", "Employee number"), Req("Type", "Leave type", "ANNUAL"), Today(), Num("Days", "Days"), Num("Rate", "Daily rate")],
            Run = async (c, i) => Check(await c.Get<IAttendanceService>().EncashLeaveAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Type"), i.Date("Date"),
                i.Number("Days"), i.Number("Rate")).ConfigureAwait(true), $"Encashed {M(i.Number("Days") * i.Number("Rate"))}."),
            List = LeaveBalancesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Set the establishment", Description = "Approved posts and annual cost per post for a department and grade.",
            Fields = [Req("Department", "Department"), Req("Grade", "Grade"), Num("Posts", "Approved posts"), Num("Cost", "Annual cost per post")],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().SetEstablishmentAsync(c.CompanyId, i.Text("Department"), i.Text("Grade"), i.Whole("Posts"), i.Number("Cost")).ConfigureAwait(true), "Establishment saved."),
        };
        yield return new Operation
        {
            Module = module, Title = "File an employee document", ListTitle = "Documents expiring in 90 days", Description = "A contract, permit, licence or certificate on the employee's file.",
            Fields = [Req("Employee", "Employee number"), Req("Type", "Document type"), Opt("Reference", "Reference"), new("Expires", "Expires on", FieldKind.Date, Required: false)],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().FileDocumentAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Type"), i.Optional("Reference"),
                i.OptionalDate("Expires")).ConfigureAwait(true), "Document filed."),
            List = ExpiringDocumentsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Record a disciplinary action", Description = "The offence and the sanction issued.",
            Fields = [Req("Employee", "Employee number"), Today(), Req("Offence", "Offence"), Pick("Sanction", "Sanction", Enum.GetNames<DisciplinarySanction>())],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().RecordDisciplinaryAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Date("Date"), i.Text("Offence"),
                Enum.Parse<DisciplinarySanction>(i.Text("Sanction")), c.UserId).ConfigureAwait(true), "Recorded."),
        };
        yield return new Operation
        {
            Module = module, Title = "Record training", Description = "A completed course, its cost and when it must be renewed.",
            Fields = [Req("Employee", "Employee number"), Req("Course", "Course"), Today("Completed", "Completed on"), new("Expires", "Expires on", FieldKind.Date, Required: false), Opt("Provider", "Provider"), Num("Cost", "Cost", "0")],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().RecordTrainingAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Course"), i.Date("Completed"),
                i.OptionalDate("Expires"), i.Optional("Provider"), i.Number("Cost")).ConfigureAwait(true), "Training recorded."),
        };
        yield return new Operation
        {
            Module = module, Title = "Record an appraisal", Description = "Rating 1–5 and the next appraisal date.",
            Fields = [Req("Employee", "Employee number"), Today(), Num("Rating", "Rating (1–5)", "3"), Today("Next", "Next due")],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().RecordAppraisalAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Date("Date"), i.Whole("Rating"),
                i.Date("Next")).ConfigureAwait(true), "Appraisal recorded."),
        };
        yield return new Operation
        {
            Module = module, Title = "Register a garnishee order", Description = "A court order deducted from pay through a deduction element until the total is settled.",
            Fields = [Req("Employee", "Employee number"), Req("Reference", "Order reference"), Req("Creditor", "Creditor"), Num("Priority", "Priority", "1"), Num("Instalment", "Instalment per period"),
                Num("Total", "Total amount"), Req("Element", "Deduction element code"), Today("Issued", "Issued on")],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().RegisterGarnisheeAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Reference"), i.Text("Creditor"),
                i.Whole("Priority"), i.Number("Instalment"), i.Number("Total"), i.Text("Element"), i.Date("Issued")).ConfigureAwait(true), "Garnishee registered."),
        };
        yield return new Operation
        {
            Module = module, Title = "Supplementary payroll run", ListTitle = "Payroll runs", Description = "Corrects a committed period with extra (or negative) element amounts — a visible run with its own payslips and journal.",
            Fields = [Req("Period", "Committed period"), Today(), Req("Document", "Run document number"), Req("Reason", "Reason"), Req("NetPay", "Net pay control account", "2420"),
                Req("Lines", "Lines: EMPLOYEE:ELEMENT:amount; …")],
            Run = async (c, i) =>
            {
                List<SupplementaryLine> lines = [];
                foreach (string part in i.Text("Lines").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                    if (bits.Length != 3 || !decimal.TryParse(bits[2], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
                    {
                        throw new OperationException($"'{part}' should look like EMPLOYEE:ELEMENT:amount.");
                    }

                    lines.Add(new SupplementaryLine(await EmployeeAsync(c, bits[0]).ConfigureAwait(true), bits[1], amount));
                }

                PayrollRunResult run = Check(await c.Get<IPayrollAdjustmentService>().RunSupplementaryAsync(new SupplementaryRunRequest
                {
                    CompanyId = c.CompanyId, PeriodLabel = i.Text("Period"), PayDate = i.Date("Date"), NetPayAccountCode = i.Text("NetPay"), DocumentNumber = i.Text("Document"), Reason = i.Text("Reason"), Lines = lines,
                }).ConfigureAwait(true));
                return $"{run.PayslipCount} payslip(s), net {M(run.TotalNet)}, journal {run.JournalNumber}.";
            },
            List = PayrollRunsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Reverse a payroll run", ListTitle = "Payroll runs", Description = "A formally approved reversal: reversing journal and negative payslips. The approver cannot be whoever ran the payroll.",
            Fields = [Req("Run", "Run document or journal number"), Today(), Req("Reason", "Reason"), Req("Approver", "Approved by (user name)")],
            Run = async (c, i) =>
            {
                PayrollRunResult run = Check(await c.Get<IPayrollAdjustmentService>().ReverseRunAsync(c.CompanyId, i.Text("Run"), i.Date("Date"), i.Text("Reason"),
                    await UserAsync(c, i.Text("Approver")).ConfigureAwait(true)).ConfigureAwait(true));
                return $"Reversed {run.PayslipCount} payslip(s), net {M(run.TotalNet)}, journal {run.JournalNumber}.";
            },
            List = PayrollRunsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Backpay for a salary change", ListTitle = "Payroll runs",
            Description = "Recalculates every period paid since the effective date at the new basic salary and pays the difference. Untick 'Pay now' to preview.",
            Fields = [Req("Employee", "Employee number"), Num("Basic", "New basic salary"), Today("From", "Effective from"), Tick("Pay", "Pay now"), Req("Period", "Pay in period"), Today("PayDate", "Pay date"),
                Opt("Document", "Run document number"), Opt("Reason", "Reason"), Req("NetPay", "Net pay control account", "2420")],
            Run = async (c, i) =>
            {
                IPayrollAdjustmentService service = c.Get<IPayrollAdjustmentService>();
                long employee = await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true);
                IReadOnlyList<BackpayPreviewLine> lines = Check(await service.CalculateBackpayAsync(c.CompanyId, employee, i.Number("Basic"), i.Date("From")).ConfigureAwait(true));
                string preview = lines.Count == 0 ? "Nothing owed." : string.Join(Environment.NewLine, lines.Select(l => $"{l.ForPeriod,-10} {l.ElementCode,-12} paid {M(l.Paid),12}  due {M(l.Due),12}  diff {M(l.Difference),12}"));
                if (!i.Flag("Pay"))
                {
                    return preview;
                }

                PayrollRunResult run = Check(await service.PayBackpayAsync(new BackpayRequest
                {
                    CompanyId = c.CompanyId, EmployeeId = employee, NewBasicSalary = i.Number("Basic"), EffectiveFrom = i.Date("From"), PeriodLabel = i.Text("Period"), PayDate = i.Date("PayDate"),
                    NetPayAccountCode = i.Text("NetPay"), DocumentNumber = i.Text("Document"), Reason = i.Text("Reason"),
                }).ConfigureAwait(true));
                return $"{preview}{Environment.NewLine}{Environment.NewLine}Backpay net {M(run.TotalNet)} paid, journal {run.JournalNumber}; basic salary updated.";
            },
            List = PayrollRunsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Run payroll", ListTitle = "Payroll runs",
            Description = "Calculates and commits a pay group's payroll for the period (blank group = the default payroll): payslips and one balanced journal. A period is paid once per group.",
            Fields = [Opt("Group", "Pay group (blank = default)"), Req("Period", "Period", DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture)), Today(), Req("Document", "Run document number"),
                Req("NetPay", "Net pay control account", "2420")],
            Run = async (c, i) =>
            {
                PayrollRunResult run = Check(await c.Get<IPayrollService>().RunGroupAsync(c.CompanyId, i.Optional("Group"), i.Text("Period"), i.Date("Date"), i.Text("NetPay"), i.Text("Document")).ConfigureAwait(true));
                return $"{run.PayslipCount} payslip(s): gross {M(run.TotalGross)}, deductions {M(run.TotalDeductions)}, net {M(run.TotalNet)}; journal {run.JournalNumber}.";
            },
            List = PayrollRunsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Key a pay variable", Description = "Overtime hours (units on a rate-per-unit element) or a once-off amount for one employee and period.",
            Fields = [Req("Employee", "Employee number"), Req("Element", "Element code"), Req("Period", "Period", DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture)), OptNum("Units", "Units"),
                OptNum("Amount", "Amount")],
            Run = async (c, i) => Check(await c.Get<IPayrollVariableService>().SetAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Element"), i.Text("Period"),
                i.OptionalNumber("Units"), i.OptionalNumber("Amount")).ConfigureAwait(true), id => $"Variable {id} saved for {i.Text("Period")}."),
        };
        yield return new Operation
        {
            Module = module, Title = "Import pay variables", Description = "Lines 'employee, element, units, amount' separated by ';'. Untick 'Import' to preview the errors first; any error stops the import.",
            Fields = [Req("Period", "Period", DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture)), Req("Lines", "Lines: EMP,ELEMENT,units,amount; …"), Tick("Import", "Import (otherwise preview)")],
            Run = async (c, i) =>
            {
                IPayrollVariableService service = c.Get<IPayrollVariableService>();
                string csv = i.Text("Lines").Replace(';', '\n');
                if (i.Flag("Import"))
                {
                    return $"{Check(await service.ImportAsync(c.CompanyId, i.Text("Period"), csv).ConfigureAwait(true))} variable(s) imported for {i.Text("Period")}.";
                }

                VariableImportPreview preview = Check(await service.PreviewImportAsync(c.CompanyId, i.Text("Period"), csv).ConfigureAwait(true));
                return $"{preview.Lines.Count} line(s), {preview.ErrorCount} with errors:" + Environment.NewLine + string.Join(Environment.NewLine, preview.Lines.Select(l =>
                    $"{l.Line,4}  {l.EmployeeNumber,-10} {l.EmployeeName ?? "?",-24} {l.ElementCode,-10} {(l.Units is decimal u ? Q(u) : "-"),8} {(l.Amount is decimal a ? M(a) : "-"),12}  {l.Error ?? "OK"}"));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Calculate payroll for approval", ListTitle = "Payroll drafts",
            Description = "Calculates a pay group's payroll without posting it, for someone else to approve. Recalculating replaces the earlier draft.",
            Fields = [Opt("Group", "Pay group (blank = default)"), Req("Period", "Period", DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture)), Today(), Req("Document", "Run document number"),
                Req("NetPay", "Net pay control account", "2420")],
            Run = async (c, i) => DescribeDraft(Check(await c.Get<IPayrollApprovalService>().CalculateRunAsync(new PayrollDraftRequest
            {
                CompanyId = c.CompanyId, PayGroup = i.Optional("Group"), PeriodLabel = i.Text("Period"), PayDate = i.Date("Date"), NetPayAccountCode = i.Text("NetPay"), DocumentNumber = i.Text("Document"),
                UserId = c.UserId,
            }).ConfigureAwait(true))),
            List = PayrollDraftsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Approve payroll", ListTitle = "Payroll drafts", Description = "Approves a calculated payroll (not your own; a large movement needs two different approvers).",
            Fields = [Num("Draft", "Draft id (from the list)")],
            Run = async (c, i) => DescribeDraft(Check(await c.Get<IPayrollApprovalService>().ApproveRunAsync(c.CompanyId, (long)i.Number("Draft"), c.UserId).ConfigureAwait(true))),
            List = PayrollDraftsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Commit approved payroll", ListTitle = "Payroll drafts", Description = "Saves the payslips and posts the journal — refused if anything changed since approval.",
            Fields = [Num("Draft", "Draft id (from the list)")],
            Run = async (c, i) =>
            {
                PayrollRunResult run = Check(await c.Get<IPayrollApprovalService>().CommitRunAsync(c.CompanyId, (long)i.Number("Draft"), c.UserId).ConfigureAwait(true));
                return $"Committed {run.PayslipCount} payslip(s), net {M(run.TotalNet)}, journal {run.JournalNumber}.";
            },
            List = PayrollDraftsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Raise third-party invoices", ListTitle = "Payroll runs",
            Description = "Raises a run's AP invoices for the tax authority, schemes and garnishee creditors that have a supplier — clearing the payroll liabilities into payables.",
            Fields = [Req("Run", "Run document or journal number")],
            Run = async (c, i) =>
            {
                IReadOnlyList<RemittanceRaised> raised = Check(await c.Get<IPayrollRemittanceService>().RaiseRemittancesAsync(c.CompanyId, i.Text("Run")).ConfigureAwait(true));
                return string.Join(Environment.NewLine, raised.Select(r => $"{r.DocumentNumber,-24} {r.Payee,-12} Dr {r.AccountCode,-8} {M(r.Amount),12}"));
            },
            List = PayrollRunsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Decide self-service requests", ListTitle = "Requests awaiting HR",
            Description = "Leave applications and payment-detail changes employees made through self-service. Approving takes the leave or applies the change; nobody decides their own.",
            Fields = [Pick("Kind", "Kind", "Leave", "Detail"), Num("Id", "Request id (from the list)"), Pick("Decision", "Decision", "Approve", "Reject"), Opt("Note", "Note")],
            Run = async (c, i) =>
            {
                IEmployeeSelfService ess = c.Get<IEmployeeSelfService>();
                bool approve = i.Text("Decision") == "Approve";
                SelfServiceStatus status = Check(i.Text("Kind") == "Leave"
                    ? await ess.DecideLeaveAsync(c.CompanyId, (long)i.Number("Id"), approve, c.UserId, i.Optional("Note")).ConfigureAwait(true)
                    : await ess.DecideDetailChangeAsync(c.CompanyId, (long)i.Number("Id"), approve, c.UserId, i.Optional("Note")).ConfigureAwait(true));
                return $"{i.Text("Kind")} request {(long)i.Number("Id")}: {status}.";
            },
            List = async c => Table(await c.Get<IEmployeeSelfService>().PendingAsync(c.CompanyId).ConfigureAwait(true),
                ("Kind", r => r.Kind), ("Id", r => r.Id), ("Employee", r => $"{r.EmployeeNumber} {r.FullName}"), ("Request", r => r.Summary)),
        };
        yield return new Operation
        {
            Module = module, Title = "Accrue unpaid salaries", Description = "Period end: accrues basic salary for everyone employed but not yet paid for the month, reversed on the 1st of next month.",
            Fields = [Today("AsAt", "Period end"), Req("Expense", "Salary expense account", "6100"), Req("Accrual", "Accrued salaries account"), Req("Document", "Document number")],
            Run = async (c, i) =>
            {
                SalaryAccrual accrual = Check(await c.Get<IPayrollAccountingService>().AccrueSalariesAsync(c.CompanyId, i.Date("AsAt"), i.Text("Expense"), i.Text("Accrual"), i.Text("Document")).ConfigureAwait(true));
                return $"Accrued {M(accrual.Amount)} for {accrual.Employees} employee(s): {accrual.JournalNumber}, reversed by {accrual.ReversalJournalNumber}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Capitalise timesheet labour to WIP", Description = "Moves a contract's timesheet labour up to a date into its WIP account, each timesheet once.",
            Fields = [Req("Contract", "Contract number"), Today("UpTo", "Up to"), Req("Wip", "Contract WIP account"), Req("Recovery", "Payroll cost account credited", "6100"), Req("Document", "Document number")],
            Run = async (c, i) =>
            {
                LabourCapitalised done = Check(await c.Get<IPayrollAccountingService>().CapitaliseTimesheetsAsync(c.CompanyId, await ContractAsync(c, i.Text("Contract")).ConfigureAwait(true), i.Date("UpTo"),
                    i.Text("Wip"), i.Text("Recovery"), i.Text("Document")).ConfigureAwait(true));
                return $"{done.Timesheets} timesheet(s), {Q(done.Hours)} h, {M(done.Amount)} capitalised: {done.JournalNumber}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Set an employee's pay group", Description = "Moves an employee to a pay group (MONTHLY, WEEKLY, CASUAL…); blank returns them to the default payroll.",
            Fields = [Req("Employee", "Employee number"), Opt("Group", "Pay group")],
            Run = async (c, i) =>
            {
                long id = await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true);
                Employee employee = await c.Db.Employees.FirstAsync(e => e.Id == id).ConfigureAwait(true);
                employee.PayGroup = i.Optional("Group")?.ToUpperInvariant();
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"{employee.EmployeeNumber} is now on the {employee.PayGroup ?? "default"} payroll.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Set net-pay protection", ListTitle = "Deductions and their suspension priority",
            Description = "The minimum take-home (an amount and / or % of gross) and, optionally, one deduction's suspension priority (0 = never suspended; highest is suspended first).",
            Fields = [OptNum("Amount", "Minimum take-home amount"), OptNum("Percent", "Minimum take-home % of gross"), Opt("Element", "Deduction code"), OptNum("Priority", "Its suspension priority")],
            Run = async (c, i) =>
            {
                Company company = await c.Db.Companies.FirstAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                company.MinimumTakeHomeAmount = i.OptionalNumber("Amount");
                company.MinimumTakeHomePercent = i.OptionalNumber("Percent");
                if (i.Optional("Element") is string code)
                {
                    PayElement element = await c.Db.PayElements.FirstOrDefaultAsync(e => e.CompanyId == c.CompanyId && e.Code.ToUpper() == code.ToUpper() && e.Type == PayElementType.Deduction).ConfigureAwait(true)
                        ?? throw new OperationException($"No deduction '{code}'.");
                    element.SuspensionPriority = i.Whole("Priority");
                }

                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return company.MinimumTakeHomeAmount is null && company.MinimumTakeHomePercent is null ? "Net-pay protection is off." : "Net-pay protection saved; see PR71 after the next run.";
            },
            List = async c => Table(await c.Db.PayElements.AsNoTracking().Where(e => e.CompanyId == c.CompanyId && e.Type == PayElementType.Deduction).OrderBy(e => e.Sequence).ToListAsync().ConfigureAwait(true),
                ("Deduction", e => e.Code), ("Name", e => e.Name), ("Sequence", e => e.Sequence), ("Suspension priority", e => e.SuspensionPriority == 0 ? "never" : e.SuspensionPriority.ToString(CultureInfo.InvariantCulture))),
        };
        yield return new Operation
        {
            Module = module, Title = "Import legacy payroll (parallel run)", Description = "The old system's figures for a period, compared line by line in report PR8. Replaces an earlier import of the period.",
            Fields = [Req("Period", "Period"), Req("Lines", "Lines: EMPLOYEE,ELEMENT,amount; … (NET for net pay)")],
            Run = async (c, i) => $"{Check(await c.Get<IPayrollAdjustmentService>().ImportLegacyPayrollAsync(c.CompanyId, i.Text("Period"), i.Text("Lines").Replace(';', '\n')).ConfigureAwait(true))} line(s) imported — run report PR8.",
        };
    }

    private static string DescribeDraft(PayrollDraftResult d)
    {
        string movement = d.MovementPercent is decimal m ? $" ({m.ToString("+0.0;-0.0", CultureInfo.InvariantCulture)}% on the last run)" : string.Empty;
        string head = $"Draft {d.DraftId}: {d.Status}. {d.Employees} payslip(s), gross {M(d.Gross)}, deductions {M(d.Deductions)}, net {M(d.Net)}{movement}."
            + (d.RequiresSecondApproval ? " Needs two approvers." : string.Empty);
        return d.Exceptions.Count == 0 ? head + Environment.NewLine + "No exceptions." : head + Environment.NewLine + "Exceptions:" + Environment.NewLine + string.Join(Environment.NewLine, d.Exceptions);
    }

    private static async Task<DataTable> PayrollDraftsAsync(OperationContext c)
    {
        Dictionary<long, string> users = await c.Db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username).ConfigureAwait(true);
        string User(long? id) => id is long u ? users.GetValueOrDefault(u, $"user {u}") : string.Empty;
        return Table(await c.Db.PayrollDrafts.AsNoTracking().Where(d => d.CompanyId == c.CompanyId).OrderByDescending(d => d.Id).Take(30).ToListAsync().ConfigureAwait(true),
            ("Draft", d => d.Id), ("Group", d => d.PayGroup.Length == 0 ? "Default" : d.PayGroup), ("Period", d => d.PeriodLabel), ("Status", d => d.Status), ("Net", d => M(d.Net)),
            ("Calculated by", d => User(d.CalculatedByUserId)), ("Approved by", d => string.Join(" + ", new[] { User(d.FirstApproverUserId), User(d.SecondApproverUserId) }.Where(x => x.Length > 0))),
            ("Journal", d => d.JournalNumber ?? string.Empty));
    }

    private static async Task<DataTable> PayrollRunsAsync(OperationContext c)
        => Table(await c.Db.Set<PayrollRunRecord>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderByDescending(r => r.Id).Take(50).ToListAsync().ConfigureAwait(true),
            ("Run", r => r.DocumentNumber.Length > 0 ? r.DocumentNumber : r.JournalNumber), ("Group", r => r.PayGroup.Length == 0 ? "Default" : r.PayGroup), ("Period", r => r.PeriodLabel), ("Kind", r => r.Kind), ("Date", r => D(r.PayDate)), ("Payslips", r => r.Employees),
            ("Net", r => M(r.Net)), ("Journal", r => r.JournalNumber), ("Reversed", r => r.ReversedBy ?? string.Empty));

    private static TimeOnly? Time(OperationInput i, string name)
        => i.Optional(name) is not string text ? null
            : TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out TimeOnly t) ? t : throw new OperationException($"'{text}' is not a time (HH:mm).");

    private static async Task<Dictionary<long, string>> EmployeeNamesAsync(OperationContext c)
        => await c.Db.Employees.Where(e => e.CompanyId == c.CompanyId).ToDictionaryAsync(e => e.Id, e => $"{e.EmployeeNumber} {e.FullName}").ConfigureAwait(true);

    private static async Task<DataTable> PendingOvertimeAsync(OperationContext c)
    {
        Dictionary<long, string> names = await EmployeeNamesAsync(c).ConfigureAwait(true);
        return Table(await c.Db.AttendanceRecords.AsNoTracking().Where(a => a.CompanyId == c.CompanyId && a.OvertimeHours > 0 && a.OvertimeApprovedByUserId == null).OrderBy(a => a.Date).ToListAsync().ConfigureAwait(true),
            ("Attendance", a => a.Id), ("Employee", a => names.GetValueOrDefault(a.EmployeeId)), ("Date", a => D(a.Date)), ("Status", a => a.Status), ("Overtime hours", a => Q(a.OvertimeHours)),
            ("Multiplier", a => Q(a.OvertimeMultiplier)));
    }

    private static DateOnly? OptionalDate(OperationInput i, string name)
        => i.Optional(name) is string text ? DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    private static async Task<DataTable> RecurringBankAsync(OperationContext c)
    {
        var orders = (await c.Db.Set<StandingOrder>().AsNoTracking().Where(o => o.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true))
            .Select(o => new { o.Reference, Kind = "Standing order", o.Frequency, Amount = M(o.Amount), o.StartDate, o.LastRunOn, o.IsActive });
        var mandates = (await c.Db.Set<DirectDebitMandate>().AsNoTracking().Where(m => m.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true))
            .Select(m => new { m.Reference, Kind = "Direct debit", m.Frequency, Amount = m.FixedAmount is decimal f ? M(f) : "owed", m.StartDate, m.LastRunOn, m.IsActive });
        return Table(orders.Concat(mandates).OrderBy(r => r.Reference),
            ("Reference", r => r.Reference), ("Kind", r => r.Kind), ("Frequency", r => r.Frequency), ("Amount", r => r.Amount), ("Starts", r => D(r.StartDate)),
            ("Last run", r => r.LastRunOn is DateOnly d ? D(d) : null), ("Active", r => r.IsActive));
    }

    private static async Task<DataTable> LeaveBalancesAsync(OperationContext c)
    {
        Dictionary<long, string> names = await EmployeeNamesAsync(c).ConfigureAwait(true);
        return Table((await c.Db.EmployeeLeaveBalances.AsNoTracking().Where(b => b.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true)).OrderBy(b => names.GetValueOrDefault(b.EmployeeId)).ThenBy(b => b.LeaveType),
            ("Employee", b => names.GetValueOrDefault(b.EmployeeId)), ("Leave type", b => b.LeaveType), ("Accrued", b => Q(b.AccruedDays)), ("Taken", b => Q(b.TakenDays)), ("Balance", b => Q(b.BalanceDays)));
    }

    private static async Task<DataTable> ExpiringDocumentsAsync(OperationContext c)
    {
        Dictionary<long, string> names = await EmployeeNamesAsync(c).ConfigureAwait(true);
        DateOnly horizon = DateOnly.FromDateTime(DateTime.Today).AddDays(90);
        return Table(await c.Db.EmployeeDocuments.AsNoTracking().Where(d => d.CompanyId == c.CompanyId && d.ExpiresOn != null && d.ExpiresOn <= horizon).OrderBy(d => d.ExpiresOn).ToListAsync().ConfigureAwait(true),
            ("Employee", d => names.GetValueOrDefault(d.EmployeeId)), ("Document", d => d.DocumentType), ("Reference", d => d.Reference), ("Expires", d => D(d.ExpiresOn!.Value)));
    }

    private static IEnumerable<Operation> Banking()
    {
        const string module = "Banking";
        yield return new Operation
        {
            Module = module, Title = "Set up a standing order", ListTitle = "Standing orders and direct debits",
            Description = "A fixed payment out of the bank on a schedule: to a supplier (purchase ledger) or to a GL account. Give a supplier or an account, not both.",
            Fields = [Req("Reference", "Reference"), Req("Bank", "Bank account", "1100"), Num("Amount", "Amount"), Pick("Frequency", "Frequency", Enum.GetNames<RecurrenceFrequency>()),
                Today("Start", "First payment"), Opt("End", "Last date (yyyy-MM-dd, optional)"), Opt("Supplier", "Supplier code"), Opt("Account", "GL account"), Opt("Narrative", "What it is for")],
            Run = async (c, i) => Check(await c.Get<IStandingOrderService>().CreateStandingOrderAsync(new StandingOrderRequest
            {
                CompanyId = c.CompanyId, Reference = i.Text("Reference"), BankAccountCode = i.Text("Bank"), Amount = i.Number("Amount"), Frequency = Enum.Parse<RecurrenceFrequency>(i.Text("Frequency")),
                StartDate = i.Date("Start"), EndDate = OptionalDate(i, "End"), SupplierId = i.Optional("Supplier") is string s ? await SupplierAsync(c, s).ConfigureAwait(true) : null,
                DebitAccountCode = i.Optional("Account"), Narrative = i.Optional("Narrative") ?? string.Empty,
            }).ConfigureAwait(true), id => $"Standing order {i.Text("Reference").ToUpperInvariant()} set up."),
            List = RecurringBankAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Set up a direct debit mandate", ListTitle = "Standing orders and direct debits",
            Description = "The customer's authority to collect on a schedule: a fixed amount, or what they owe (optionally capped).",
            Fields = [Req("Reference", "Mandate reference"), Req("Customer", "Customer code"), Req("CustomerBank", "Customer's bank account"), Req("Bank", "Collect into bank account", "1100"),
                Pick("Frequency", "Frequency", Enum.GetNames<RecurrenceFrequency>()), Today("Start", "First collection"), Opt("End", "Last date (yyyy-MM-dd, optional)"),
                OptNum("Fixed", "Fixed amount (blank: what is owed)"), OptNum("Max", "Most per collection")],
            Run = async (c, i) => Check(await c.Get<IStandingOrderService>().CreateMandateAsync(new DirectDebitMandateRequest
            {
                CompanyId = c.CompanyId, Reference = i.Text("Reference"), CustomerId = await CustomerAsync(c, i.Text("Customer")).ConfigureAwait(true), CustomerBankAccount = i.Text("CustomerBank"),
                BankAccountCode = i.Text("Bank"), Frequency = Enum.Parse<RecurrenceFrequency>(i.Text("Frequency")), StartDate = i.Date("Start"), EndDate = OptionalDate(i, "End"),
                FixedAmount = i.OptionalNumber("Fixed"), MaximumAmount = i.OptionalNumber("Max"),
            }).ConfigureAwait(true), id => $"Mandate {i.Text("Reference").ToUpperInvariant()} set up."),
            List = RecurringBankAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Run standing orders and direct debits", ListTitle = "Standing orders and direct debits",
            Description = "Pays every standing order and collects every direct debit due up to the date; nothing is ever run twice.",
            Fields = [Today("Date", "Run up to")],
            Run = async (c, i) =>
            {
                RecurringBankRun run = Check(await c.Get<IStandingOrderService>().RunDueAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true));
                return $"{run.Lines.Count(l => !l.IsCollection)} payment(s) {M(run.Lines.Where(l => !l.IsCollection).Sum(l => l.Amount))}, "
                    + $"{run.Lines.Count(l => l.IsCollection)} collection(s) {M(run.Lines.Where(l => l.IsCollection).Sum(l => l.Amount))}, {run.NothingOwed} with nothing owed.";
            },
            List = RecurringBankAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Stop a standing order or mandate", ListTitle = "Standing orders and direct debits", Description = "Stops it running from now on.",
            Fields = [Req("Reference", "Reference")],
            Run = async (c, i) => Check(await c.Get<IStandingOrderService>().CancelAsync(c.CompanyId, i.Text("Reference")).ConfigureAwait(true), "Stopped."),
            List = RecurringBankAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Issue a cheque", ListTitle = "Cheque register (latest 100)", Description = "Records a cheque leaf written out of the cheque book (numbers are unique per bank account).",
            Fields = [Req("Bank", "Bank account code"), Req("Number", "Cheque number"), Today(), Req("Payee", "Payee"), Num("Amount", "Amount"), Opt("Document", "Payment document number")],
            Run = async (c, i) => Check(await c.Get<IChequeService>().IssueAsync(c.CompanyId, await BankAsync(c, i.Text("Bank")).ConfigureAwait(true), i.Text("Number"), i.Date("Date"), i.Text("Payee"),
                i.Number("Amount"), i.Optional("Document")).ConfigureAwait(true), $"Cheque {i.Text("Number")} issued."),
            List = ChequesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Void a cheque leaf", ListTitle = "Cheque register (latest 100)", Description = "A spoilt or lost leaf that was never issued — kept in the register so the number sequence has no gaps.",
            Fields = [Req("Bank", "Bank account code"), Req("Number", "Cheque number"), Today(), Req("Reason", "Reason")],
            Run = async (c, i) => Check(await c.Get<IChequeService>().VoidLeafAsync(c.CompanyId, await BankAsync(c, i.Text("Bank")).ConfigureAwait(true), i.Text("Number"), i.Date("Date"), i.Text("Reason"))
                .ConfigureAwait(true), $"Leaf {i.Text("Number")} voided."),
            List = ChequesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Mark a cheque presented / cancelled", ListTitle = "Cheque register (latest 100)", Description = "Presented when it clears the bank; cancelled (with a reason) when stopped.",
            Fields = [Req("Bank", "Bank account code"), Req("Number", "Cheque number"), Pick("Action", "Action", "Presented", "Cancelled"), Today(), Opt("Reason", "Reason (cancel)")],
            Run = async (c, i) =>
            {
                long bank = await BankAsync(c, i.Text("Bank")).ConfigureAwait(true);
                long cheque = await FindAsync(c.Db.Cheques, x => x.CompanyId == c.CompanyId && x.BankAccountId == bank && x.ChequeNumber == i.Text("Number"), x => x.Id, "cheque", i.Text("Number")).ConfigureAwait(true);
                IChequeService service = c.Get<IChequeService>();
                return i.Text("Action") == "Presented"
                    ? Check(await service.PresentAsync(c.CompanyId, cheque, i.Date("Date")).ConfigureAwait(true), "Cheque presented.")
                    : Check(await service.CancelAsync(c.CompanyId, cheque, i.Date("Date"), i.Text("Reason")).ConfigureAwait(true), "Cheque cancelled.");
            },
            List = ChequesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Save a bank file layout", ListTitle = "Bank file layouts",
            Description = "A bank's EFT format as templates: {Field[,width][:format]} — header/trailer: Date, Originator, Currency, Count, Total, Hash; detail: Seq, Date, Reference, Name, Bank, Branch, Account, Currency, Amount (format 'cents' for whole cents; width 010 zero-pads).",
            Fields = [Req("Code", "Layout code"), Req("Name", "Bank / format name"), Opt("Header", "Header template"), Req("Detail", "Detail template", "{Reference}|{Name}|{Bank}|{Account}|{Amount}"), Opt("Trailer", "Trailer template")],
            Run = async (c, i) => Check(await c.Get<IBankFileService>().SaveLayoutAsync(new BankFileLayoutRequest(c.CompanyId, i.Text("Code"), i.Text("Name"), i.Optional("Header"), i.Text("Detail"), i.Optional("Trailer")))
                .ConfigureAwait(true), id => $"Layout {i.Text("Code").ToUpperInvariant()} saved."),
            List = async c => Table(await c.Db.BankFileLayouts.AsNoTracking().Where(l => l.CompanyId == c.CompanyId).OrderBy(l => l.Code).ToListAsync().ConfigureAwait(true),
                ("Layout", l => l.Code), ("Name", l => l.Name), ("Header", l => l.HeaderTemplate), ("Detail", l => l.DetailTemplate), ("Trailer", l => l.TrailerTemplate)),
        };
        yield return new Operation
        {
            Module = module, Title = "Write payroll bank files", IsDocument = true,
            Description = "A payroll run's net pay in a bank layout, one file per beneficiary bank, with control total and hash (split pay honoured, held salaries left out).",
            Fields = [Req("Run", "Run document or journal number"), Req("Layout", "Layout code")],
            Run = async (c, i) => string.Join(Environment.NewLine, Check(await c.Get<IBankFileService>().PayrollFilesAsync(c.CompanyId, i.Text("Run"), i.Text("Layout")).ConfigureAwait(true))
                .Select(f => $"=== {f.FileName}: {f.Count} payment(s), total {M(f.Total)}, hash {f.Hash} ==={Environment.NewLine}{f.Content}")),
        };
        yield return new Operation
        {
            Module = module, Title = "Capture a supplier statement", Description = "The supplier's closing balance and lines, for reconciliation against the ledger (report AP statement reconciliation).",
            Fields = [Req("Supplier", "Supplier code"), Today(), Num("Closing", "Closing balance"), Req("Lines", "Lines: DOC:amount:yyyy-MM-dd; …")],
            Run = async (c, i) =>
            {
                List<StatementLineInput> lines = [.. i.Pairs("Lines").Select(p => new StatementLineInput(p.Code, p.Date ?? i.Date("Date"), p.Quantity))];
                long id = Check(await c.Get<IChequeService>().CaptureSupplierStatementAsync(c.CompanyId, await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true), i.Date("Date"), i.Number("Closing"), lines)
                    .ConfigureAwait(true));
                return $"Statement {id} captured with {lines.Count} line(s).";
            },
        };
    }

    private static Task<long> BankAsync(OperationContext c, string code)
        => FindAsync(c.Db.BankAccounts, b => b.CompanyId == c.CompanyId && b.Code.ToUpper() == code.ToUpper(), b => b.Id, "bank account", code);

    private static async Task<DataTable> ChequesAsync(OperationContext c)
    {
        Dictionary<long, string> banks = await c.Db.BankAccounts.Where(b => b.CompanyId == c.CompanyId).ToDictionaryAsync(b => b.Id, b => b.Code).ConfigureAwait(true);
        return Table(await c.Db.Cheques.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderByDescending(x => x.Id).Take(100).ToListAsync().ConfigureAwait(true),
            ("Bank", x => banks.GetValueOrDefault(x.BankAccountId)), ("Cheque", x => x.ChequeNumber), ("Issued", x => D(x.IssuedOn)), ("Payee", x => x.Payee), ("Status", x => x.Status),
            ("Status date", x => D(x.StatusDate)), ("Reason", x => x.Reason), ("Amount", x => x.Status == ChequeStatus.Voided ? string.Empty : M(x.Amount)));
    }

    private static IEnumerable<Operation> SalesAndSchools()
    {
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Set a sibling discount", ListTitle = "Sibling discounts",
            Description = "A family's children (sharing a guardian, in admission order) get this percentage off from the given child on, after any bursary. 0 removes the rule.",
            Fields = [Num("From", "From child (2 = second)", "2"), Num("Percent", "Discount %")],
            Run = async (c, i) => Check(await c.Get<ISchoolBillingService>().SetSiblingDiscountAsync(c.CompanyId, (int)i.Number("From"), i.Number("Percent")).ConfigureAwait(true), "Sibling discount updated."),
            List = async c => Table(await c.Db.Set<Core.Schools.SiblingDiscount>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderBy(r => r.FromChild).ToListAsync().ConfigureAwait(true),
                ("From child", r => r.FromChild), ("Discount %", r => Q(r.Percent))),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Confirm a delivery (proof of delivery)", ListTitle = "Deliveries not yet confirmed",
            Description = "Records who received a despatched delivery, when, and the signed POD reference.",
            Fields = [Req("Delivery", "Delivery reference"), Today(), Req("ReceivedBy", "Received by"), Opt("Pod", "POD reference")],
            Run = async (c, i) => Check(await c.Get<ISalesOrderService>().ConfirmDeliveryAsync(c.CompanyId, i.Text("Delivery"), i.Date("Date"), i.Text("ReceivedBy"), i.Optional("Pod")).ConfigureAwait(true),
                "Delivery confirmed."),
            List = async c => Table(await c.Db.SalesDeliveries.AsNoTracking().Where(d => d.CompanyId == c.CompanyId && d.DeliveredOn == null).OrderBy(d => d.DespatchedOn).ToListAsync().ConfigureAwait(true),
                ("Delivery", d => d.Reference), ("Despatched", d => D(d.DespatchedOn)), ("Planned", d => d.PlannedFor is DateOnly p ? D(p) : string.Empty), ("Vehicle", d => d.Vehicle), ("Route", d => d.Route),
                ("Driver", d => d.Driver)),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Create a customer contract", Description = "Agreed quantities at agreed prices for a period, drawn down by call-off orders.",
            Fields = [Req("Customer", "Customer code"), Req("Number", "Contract number"), Today("From", "From"), Today("To", "To"), Req("Lines", "Lines: ITEM:quantity:price; …")],
            Run = async (c, i) =>
            {
                List<ContractLineRequest> lines = [];
                foreach (string part in i.Text("Lines").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                    if (bits.Length != 3 || !decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity)
                        || !decimal.TryParse(bits[2], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price))
                    {
                        throw new OperationException($"'{part}' should look like ITEM:quantity:price.");
                    }

                    lines.Add(new ContractLineRequest(await ItemAsync(c, bits[0]).ConfigureAwait(true), quantity, price));
                }

                return Check(await c.Get<ICustomerContractService>().CreateAsync(c.CompanyId, await CustomerAsync(c, i.Text("Customer")).ConfigureAwait(true), i.Text("Number"), i.Date("From"), i.Date("To"), lines)
                    .ConfigureAwait(true), id => $"Contract {i.Text("Number").ToUpperInvariant()} created.");
            },
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Call off against a contract", Description = "Raises a sales order at the contract prices; never past what remains on the contract.",
            Fields = [Req("Contract", "Contract number"), Opt("Order", "Order number (blank = automatic)"), Today(), Req("Lines", "Lines: ITEM:quantity; …")],
            Run = async (c, i) =>
            {
                ICustomerContractService service = c.Get<ICustomerContractService>();
                List<CallOffLine> lines = [];
                foreach ((string code, decimal quantity, DateOnly? _) in i.Pairs("Lines"))
                {
                    lines.Add(new CallOffLine(await ItemAsync(c, code).ConfigureAwait(true), quantity));
                }

                SalesOrderResult order = Check(await service.CallOffAsync(c.CompanyId, i.Text("Contract"), i.Optional("Order") ?? string.Empty, i.Date("Date"), lines).ConfigureAwait(true));
                IReadOnlyList<ContractPosition> position = Check(await service.PositionAsync(c.CompanyId, i.Text("Contract")).ConfigureAwait(true));
                return $"Order {order.OrderNumber} raised.{Environment.NewLine}" + string.Join(Environment.NewLine, position.Select(p => $"{p.ItemCode,-12} agreed {Q(p.Agreed),10}  called off {Q(p.CalledOff),10}  remaining {Q(p.Remaining),10}"));
            },
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Drop-ship order lines", ListTitle = "Drop-shipments",
            Description = "The supplier sends sales-order items straight to the customer. Untick 'Confirm' to place it; tick it (with the supplier's invoice) once delivered — cost of sales is charged and the lines become invoiceable, no stock moves.",
            Fields = [Req("Reference", "Drop-shipment reference"), Tick("Confirm", "Confirm delivered"), Opt("Order", "Sales order number (placing)"), Opt("Supplier", "Supplier code (placing)"),
                Opt("Lines", "Lines: ITEM:quantity:unit cost; … (placing)"), Today(), Opt("Invoice", "Supplier invoice (confirming)"), Req("Cogs", "Cost of sales account", "5000")],
            Run = async (c, i) =>
            {
                IDropShipService service = c.Get<IDropShipService>();
                if (i.Flag("Confirm"))
                {
                    DropShipDelivered done = Check(await service.ConfirmDeliveredAsync(c.CompanyId, i.Text("Reference"), i.Date("Date"), i.Text("Invoice"), i.Text("Cogs")).ConfigureAwait(true));
                    return $"Delivered: {M(done.Cost)} to cost of sales ({done.SupplierInvoiceJournal}); the order lines can now be invoiced.";
                }

                var order = await c.Db.SalesOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.CompanyId == c.CompanyId && o.OrderNumber == i.Text("Order").ToUpper()).ConfigureAwait(true)
                    ?? throw new OperationException($"No sales order '{i.Text("Order")}'.");
                List<DropShipLine> lines = [];
                foreach (string part in i.Text("Lines").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                    if (bits.Length != 3 || !decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity)
                        || !decimal.TryParse(bits[2], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal cost))
                    {
                        throw new OperationException($"'{part}' should look like ITEM:quantity:unit cost.");
                    }

                    long item = await ItemAsync(c, bits[0]).ConfigureAwait(true);
                    long line = order.Lines.FirstOrDefault(l => l.ItemId == item)?.Id ?? throw new OperationException($"{bits[0]} is not on order {order.OrderNumber}.");
                    lines.Add(new DropShipLine(line, quantity, cost));
                }

                return Check(await service.OrderAsync(c.CompanyId, order.Id, await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true), i.Text("Reference"), i.Date("Date"), lines).ConfigureAwait(true),
                    id => $"Drop-shipment {i.Text("Reference").ToUpperInvariant()} placed.");
            },
            List = async c => Table(await c.Db.DropShipments.AsNoTracking().Include(d => d.Lines).Where(d => d.CompanyId == c.CompanyId).OrderByDescending(d => d.Id).Take(50).ToListAsync().ConfigureAwait(true),
                ("Reference", d => d.Reference), ("Ordered", d => D(d.OrderedOn)), ("Delivered", d => d.DeliveredOn is DateOnly on ? D(on) : "pending"), ("Supplier invoice", d => d.SupplierInvoiceNumber),
                ("Cost", d => M(d.Cost))),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Save a delivery zone rate", ListTitle = "Delivery zone rates",
            Description = "A zone's charge per delivery plus per kg and per m³ of goods sent (from item weights and volumes), free above a goods value.",
            Fields = [Req("Zone", "Zone"), Num("Base", "Charge per delivery", "0"), Num("PerKg", "Per kg", "0"), Num("PerM3", "Per m³", "0"), OptNum("Free", "Free above goods value")],
            Run = async (c, i) => Check(await c.Get<IDeliveryChargeService>().SaveRateAsync(c.CompanyId, i.Text("Zone"), i.Number("Base"), i.Number("PerKg"), i.Number("PerM3"), i.OptionalNumber("Free"))
                .ConfigureAwait(true), id => $"Zone {i.Text("Zone").ToUpperInvariant()} saved."),
            List = async c => Table(await c.Db.DeliveryZoneRates.AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderBy(r => r.Zone).ToListAsync().ConfigureAwait(true),
                ("Zone", r => r.Zone), ("Per delivery", r => M(r.BaseCharge)), ("Per kg", r => Q(r.PerKg)), ("Per m3", r => Q(r.PerCubicMetre)), ("Free above", r => r.FreeAboveValue is decimal f ? M(f) : "-")),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Charge for a delivery", Description = "Works out a delivery's charge from the customer's zone; tick 'Invoice' to bill it to the customer (once per delivery).",
            Fields = [Req("Delivery", "Delivery reference"), Tick("Invoice", "Invoice it"), Opt("Number", "Invoice number"), Today(), Req("Revenue", "Delivery income account", "4000"), Opt("TaxCode", "Tax code"),
                Opt("TaxAccount", "Output tax account")],
            Run = async (c, i) =>
            {
                IDeliveryChargeService service = c.Get<IDeliveryChargeService>();
                DeliveryChargeQuote quote = Check(i.Flag("Invoice")
                    ? await service.InvoiceAsync(c.CompanyId, i.Text("Delivery"), i.Text("Number"), i.Date("Date"), i.Text("Revenue"), i.Optional("TaxCode"), i.Optional("TaxAccount")).ConfigureAwait(true)
                    : await service.QuoteAsync(c.CompanyId, i.Text("Delivery")).ConfigureAwait(true));
                return $"Zone {quote.Zone}: {Q(quote.WeightKg)} kg, {Q(quote.VolumeM3)} m³, goods {M(quote.GoodsValue)} → charge {M(quote.Charge)}{(i.Flag("Invoice") ? $", invoiced on {i.Text("Number")}" : string.Empty)}.";
            },
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Top up / spend a student wallet", ListTitle = "Wallet balances",
            Description = "A top-up is paid into the bank; a spend (tuck shop) is limited to the balance and the student's daily limit.",
            Fields = [Req("Student", "Admission number"), Pick("Action", "Action", "Top up", "Spend"), Today(), Num("Amount", "Amount"), Req("Wallet", "Wallet liability account"),
                Opt("Bank", "Bank account (top up)"), Opt("Sales", "Sales account (spend)"), Opt("Description", "What was bought", "Tuck shop")],
            Run = async (c, i) =>
            {
                long student = await FindAsync(c.Db.Students, s => s.CompanyId == c.CompanyId && s.AdmissionNumber.ToUpper() == i.Text("Student").ToUpper(), s => s.Id, "student", i.Text("Student")).ConfigureAwait(true);
                IStudentWalletService wallet = c.Get<IStudentWalletService>();
                decimal balance = i.Text("Action") == "Top up"
                    ? Check(await wallet.TopUpAsync(c.CompanyId, student, i.Date("Date"), i.Number("Amount"), i.Text("Bank"), i.Text("Wallet")).ConfigureAwait(true))
                    : Check(await wallet.SpendAsync(c.CompanyId, student, i.Date("Date"), i.Number("Amount"), i.Text("Description"), i.Text("Wallet"), i.Text("Sales")).ConfigureAwait(true));
                return $"Wallet balance now {M(balance)}.";
            },
            List = async c =>
            {
                var balances = await c.Db.StudentWalletEntries.Where(w => w.CompanyId == c.CompanyId).GroupBy(w => w.StudentId).Select(g => new { g.Key, Balance = g.Sum(w => w.Amount) }).ToListAsync().ConfigureAwait(true);
                Dictionary<long, (string, string)> students = await c.Db.Students.Where(s => s.CompanyId == c.CompanyId).ToDictionaryAsync(s => s.Id, s => (s.AdmissionNumber, s.FullName)).ConfigureAwait(true);
                return Table(balances.OrderBy(b => students.GetValueOrDefault(b.Key).Item1), ("Student", b => students.GetValueOrDefault(b.Key).Item1), ("Name", b => students.GetValueOrDefault(b.Key).Item2),
                    ("Balance", b => M(b.Balance)));
            },
        };
    }

    private static IEnumerable<Operation> Documents()
    {
        const string module = "Documents";
        Operation Doc(string title, string label, Func<IDocumentLayoutService, long, string, Task<One4All.Core.Common.Result<string>>> print) => new()
        {
            Module = module, Title = title, Description = $"Prints the {title.ToLowerInvariant()} in the standard business document layout.", IsDocument = true,
            Fields = [Req("Number", label)],
            Run = async (c, i) => Check(await print(c.Get<IDocumentLayoutService>(), c.CompanyId, i.Text("Number")).ConfigureAwait(true)),
        };

        yield return Doc("Quotation", "Quote number", (s, co, n) => s.QuotationAsync(co, n));
        yield return Doc("Order confirmation", "Sales order number", (s, co, n) => s.OrderConfirmationAsync(co, n));
        yield return Doc("Delivery note", "Delivery reference", (s, co, n) => s.DeliveryNoteAsync(co, n));
        yield return Doc("Packing slip", "Delivery reference", (s, co, n) => s.DeliveryNoteAsync(co, n, packing: true));
        yield return Doc("Purchase requisition", "Requisition number", (s, co, n) => s.RequisitionAsync(co, n));
        yield return Doc("Goods received note", "GRN number", (s, co, n) => s.GoodsReceivedNoteAsync(co, n));
        yield return Doc("Supplier debit note", "Debit note number", (s, co, n) => s.SupplierDebitNoteAsync(co, n));
        yield return Doc("Fee invoice", "Invoice number", (s, co, n) => s.FeeInvoiceAsync(co, n));
        yield return Doc("Payment certificate", "Certificate number", (s, co, n) => s.PaymentCertificateAsync(co, n));
        yield return new Operation
        {
            Module = module, Title = "Municipal bill", Description = "Prints a stand's monthly municipal bill.", IsDocument = true,
            Fields = [Req("Stand", "Stand number"), Today("Month", "Any day in the month")],
            Run = async (c, i) => Check(await c.Get<IDocumentLayoutService>().MunicipalBillAsync(c.CompanyId, i.Text("Stand"), i.Date("Month")).ConfigureAwait(true)),
        };
        yield return new Operation
        {
            Module = module, Title = "Shelf labels", Description = "Prints shelf labels at a price list's prices (all items, or a comma-separated list).", IsDocument = true,
            Fields = [Req("PriceList", "Price list code"), Opt("Items", "Item codes (comma-separated)"), Num("PerRow", "Labels per row", "2")],
            Run = async (c, i) => Check(await c.Get<IDocumentLayoutService>().ShelfLabelsAsync(c.CompanyId, i.Text("PriceList"),
                i.Optional("Items")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), i.Whole("PerRow")).ConfigureAwait(true)),
        };
    }
}

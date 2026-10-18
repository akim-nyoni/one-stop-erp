using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Documents;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Banking;
using One4All.Core.Accounting;
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
                + "recovered as far as the pay allows), then terminate - the final pay posts in one journal and later runs leave the employee out. Clear checklist items by id; once all are "
                + "cleared, release the final pay from the bank; print the certificate.",
            Fields = [Pick("Step", "Step", ["Preview", "Terminate", "Clear item", "Release final pay", "Certificate of service"]), Req("Number", "Employee number"), Pick("Type", "Type", Enum.GetNames<Application.Payroll.TerminationType>()),
                Today("LastDay", "Last day"), Opt("Reason", "Reason"), Num("NoticeMonths", "Notice (months)", "1"), Num("WorkingDays", "Working days a month", "22"), Num("GratuityWeeks", "Gratuity weeks a year", "2"),
                Num("SeveranceWeeks", "Severance weeks a year", "4"), Num("TerminalTax", "Terminal benefits tax %", "0"), Req("Salary", "Salary expense account", "6100"),
                Req("Benefits", "Terminal benefits account", "6100"), Req("Paye", "PAYE liability account", "2400"), Req("NetPay", "Net pay account", "2420"), OptNum("Item", "Checklist item id"),
                Opt("Bank", "Bank account paying (release)"), OptNum("Bonus", "Year's bonus (paid pro rata)"), OptNum("Overpayment", "Overpayment to recover"),
                Opt("Recoveries", "Recoveries account (property, overpayments)")],
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
                    case "Release final pay":
                        string? paid = Check(await terminations.ReleaseFinalPayAsync(c.CompanyId, i.Text("Number"), i.Date("LastDay"),
                            i.Optional("Bank") ?? throw new OperationException("Give the bank account paying.")).ConfigureAwait(true));
                        return paid is null ? "Released (nothing was due)." : $"Final pay released (journal {paid}).";
                }

                TerminationRequest request = new(c.CompanyId, i.Text("Number"), Enum.Parse<Application.Payroll.TerminationType>(i.Text("Type")), i.Date("LastDay"), i.Optional("Reason") ?? string.Empty)
                {
                    Policy = new Application.Payroll.TerminalPolicy(i.Number("NoticeMonths"), i.Number("WorkingDays"), i.Number("GratuityWeeks"), i.Number("SeveranceWeeks"), i.Number("TerminalTax")),
                    SalaryExpenseAccount = i.Text("Salary"), TerminalBenefitsAccount = i.Text("Benefits"), PayeLiabilityAccount = i.Text("Paye"), NetPayAccount = i.Text("NetPay"), ApprovedByUserId = c.UserId,
                    AnnualBonus = i.OptionalNumber("Bonus") ?? 0m, Overpayment = i.OptionalNumber("Overpayment") ?? 0m, RecoveriesAccount = i.Optional("Recoveries"),
                };
                Application.Payroll.FinalPayStatement statement = i.Text("Step") == "Preview"
                    ? Check(await terminations.PreviewAsync(request).ConfigureAwait(true))
                    : Check(await terminations.TerminateAsync(request).ConfigureAwait(true)).Statement;
                string earned = FormattableString.Invariant(
                    $"{statement.YearsOfService} year(s). Salary {statement.ProRataSalary:N2} + notice {statement.Notice:N2} + leave ({statement.LeaveDays:0.##} days) {statement.Leave:N2} + gratuity {statement.Gratuity:N2} + severance {statement.Severance:N2} ");
                string less = FormattableString.Invariant($"− PAYE {statement.OrdinaryTax:N2} − terminal tax {statement.TerminalTax:N2} − loans {statement.Recoveries:N2} = net {statement.Net:N2}");
                string owed = (statement.ProRataBonus > 0m ? FormattableString.Invariant($" (incl. bonus {statement.ProRataBonus:N2})") : string.Empty)
                    + (statement.OtherRecoveries > 0m ? FormattableString.Invariant($"; property and overpayments recovered {statement.OtherRecoveries:N2}") : string.Empty)
                    + (statement.UnrecoveredLoans + statement.UnrecoveredOther > 0m ? FormattableString.Invariant($"; still owed {statement.UnrecoveredLoans + statement.UnrecoveredOther:N2}.") : ".");
                string working = earned + less + owed;
                return i.Text("Step") == "Preview" ? "Preview: " + working : "Terminated. " + working;
            },
            List = async c => Table(await c.Db.Set<Termination>().AsNoTracking().Where(t => t.CompanyId == c.CompanyId).OrderByDescending(t => t.Id).Page(c, 50)
                    .SelectMany(t => t.Clearance.Select(item => new { t.EmployeeId, t.LastDay, item.Id, item.Description, item.IsCleared })).ToListAsync().ConfigureAwait(true),
                ("Item", x => x.Id), ("Employee id", x => x.EmployeeId), ("Left", x => D(x.LastDay)), ("To clear", x => x.Description), ("Cleared", x => x.IsCleared)),
        };
        yield return new Operation
        {
            Module = module, Title = "Leave provision (IAS 19)",
            Description = "Trues the provision for leave owed to staff up (or down) to its value at the date: each employee's untaken days at their own daily rate of basic "
                + "(annual ÷ 260), or one rate for everyone; only the movement posts. The scheduled task payroll.leave-provision does this at each month end.",
            Fields = [Today(), OptNum("Rate", "One daily rate for everyone (blank: each employee's own)"), Req("Expense", "Leave expense account", "6600"),
                Req("Provision", "Leave provision account", "2460"), Opt("Document", "Document number")],
            Run = async (c, i) =>
            {
                decimal provision = Check(await c.Get<ILeaveService>().RaiseLeaveProvisionAsync(c.CompanyId, i.Date("Date"), i.OptionalNumber("Rate") ?? 0m, i.Text("Expense"), i.Text("Provision"),
                    i.Optional("Document") ?? $"LEAVE-PROV-{i.Date("Date"):yyyyMMdd}").ConfigureAwait(true));
                return $"Leave provision at {D(i.Date("Date"))} is {M(provision)}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "HR expiry alerts", ListTitle = "Expiring within 60 days",
            Description = "What lapses for active employees - work permits, contract ends, probation, filed documents and training certificates - within the days given; "
                + "Notify sends HR (users whose roles cover employee records) one notification listing them. The scheduled task hr.expiry-alerts does this daily for 30 days ahead.",
            Fields = [Pick("Step", "Step", ["List", "Notify HR"]), Num("Days", "Within (days)", "60"), Today()],
            Run = async (c, i) =>
            {
                IHrAlertService alerts = c.Get<IHrAlertService>();
                if (i.Text("Step") == "Notify HR")
                {
                    return $"{Check(await alerts.NotifyAsync(c.CompanyId, i.Date("Date"), i.Whole("Days")).ConfigureAwait(true))} HR user(s) notified.";
                }

                IReadOnlyList<HrExpiry> due = await alerts.ExpiringAsync(c.CompanyId, i.Date("Date"), i.Whole("Days")).ConfigureAwait(true);
                return due.Count == 0 ? "Nothing expires in that time." : string.Join(Environment.NewLine, due.Select(d => $"{D(d.ExpiresOn)} {d.EmployeeNumber,-10} {d.EmployeeName,-28} {d.What}"));
            },
            List = async c => Table(await c.Get<IHrAlertService>().ExpiringAsync(c.CompanyId, DateOnly.FromDateTime(DateTime.Today), 60).ConfigureAwait(true),
                ("Expires", d => D(d.ExpiresOn)), ("Days", d => d.DaysLeft), ("Employee", d => d.EmployeeNumber), ("Name", d => d.EmployeeName), ("What", d => d.What)),
        };
        yield return new Operation
        {
            Module = module, Title = "Grievances and property issued", ListTitle = "Open grievances and property out",
            Description = "Lodge a grievance and follow it (investigating, resolved or withdrawn with the outcome); issue company property to an employee and record its return - "
                + "anything still out is listed on the leaver's clearance. Medical records are kept under Medical records (restricted).",
            Fields = [Pick("Step", "Step", ["Lodge grievance", "Move grievance", "Issue property", "Return property"]), Opt("Number", "Employee number"), Opt("Subject", "Subject / item"),
                Opt("Details", "Details"), Opt("Against", "Against (grievance)"), Pick("Status", "Grievance is now", ["Investigating", "Resolved", "Withdrawn"]), Opt("Outcome", "Outcome / return condition"),
                Opt("Serial", "Serial or asset number"), Num("Value", "Value", "0"), OptNum("Id", "Grievance or issue id"), Today()],
            Run = async (c, i) =>
            {
                IHrCasesService cases = c.Get<IHrCasesService>();
                long id = (long)(i.OptionalNumber("Id") ?? 0m);
                return i.Text("Step") switch
                {
                    "Move grievance" => Check(await cases.MoveGrievanceAsync(c.CompanyId, id, Enum.Parse<GrievanceStatus>(i.Text("Status")), i.Date("Date"), i.Optional("Outcome")).ConfigureAwait(true),
                        $"Grievance {id} is now {i.Text("Status").ToLowerInvariant()}."),
                    "Issue property" => $"Issued (id {Check(await cases.IssueAssetAsync(c.CompanyId, i.Optional("Number") ?? string.Empty, i.Optional("Subject") ?? string.Empty, i.Optional("Serial"),
                        i.Number("Value"), i.Date("Date")).ConfigureAwait(true))}).",
                    "Return property" => Check(await cases.ReturnAssetAsync(c.CompanyId, id, i.Date("Date"), i.Optional("Outcome")).ConfigureAwait(true), "Returned."),
                    _ => $"Grievance lodged (id {Check(await cases.LodgeGrievanceAsync(c.CompanyId, i.Optional("Number") ?? string.Empty, i.Date("Date"), i.Optional("Subject") ?? string.Empty,
                        i.Optional("Details") ?? string.Empty, i.Optional("Against")).ConfigureAwait(true))}).",
                };
            },
            List = async c =>
            {
                var grievances = await (from g in c.Db.Set<EmployeeGrievance>().AsNoTracking()
                                        join e in c.Db.Employees.AsNoTracking() on g.EmployeeId equals e.Id
                                        where g.CompanyId == c.CompanyId && (g.Status == GrievanceStatus.Open || g.Status == GrievanceStatus.Investigating)
                                        select new { Kind = "Grievance", g.Id, e.EmployeeNumber, What = g.Subject, Since = g.LodgedOn, State = g.Status.ToString() }).ToListAsync().ConfigureAwait(true);
                var issued = await (from a in c.Db.Set<EmployeeAssetIssue>().AsNoTracking()
                                    join e in c.Db.Employees.AsNoTracking() on a.EmployeeId equals e.Id
                                    where a.CompanyId == c.CompanyId && a.ReturnedOn == null
                                    select new { Kind = "Property", a.Id, e.EmployeeNumber, What = a.Description, Since = a.IssuedOn, State = "Out" }).ToListAsync().ConfigureAwait(true);
                return Table([.. grievances.Concat(issued).OrderBy(x => x.Since)], ("Kind", x => x.Kind), ("Id", x => x.Id), ("Employee", x => x.EmployeeNumber), ("What", x => x.What),
                    ("Since", x => D(x.Since)), ("State", x => x.State));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Medical records (restricted)",
            Description = "An employee's medical records - fitness assessments, illness, injury, disability - seen and kept only by users with the Hr.Medical permission; "
                + "notes are encrypted at rest.",
            Fields = [Pick("Step", "Step", ["Show", "Record"]), Req("Number", "Employee number"), Pick("Kind", "Record is", Enum.GetNames<MedicalRecordKind>()), Opt("Note", "Note"),
                Pick("Fit", "Fit for work", ["Not assessed", "Fit", "Not fit"]), Opt("Review", "Review on (yyyy-MM-dd)"), Today()],
            Run = async (c, i) =>
            {
                IHrCasesService cases = c.Get<IHrCasesService>();
                if (i.Text("Step") == "Record")
                {
                    bool? fit = i.Text("Fit") switch { "Fit" => true, "Not fit" => false, _ => null };
                    DateOnly? review = i.Optional("Review") is string r ? DateOnly.ParseExact(r, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
                    return $"Recorded (id {Check(await cases.RecordMedicalAsync(c.CompanyId, i.Text("Number"), i.Date("Date"), Enum.Parse<MedicalRecordKind>(i.Text("Kind")), i.Optional("Note") ?? string.Empty,
                        fit, review, c.UserId).ConfigureAwait(true))}).";
                }

                IReadOnlyList<EmployeeMedicalRecord> records = Check(await cases.MedicalRecordsAsync(c.CompanyId, i.Text("Number"), c.UserId).ConfigureAwait(true));
                return records.Count == 0 ? "No medical records." : string.Join(Environment.NewLine, records.Select(m =>
                    $"{D(m.Date)} {m.Kind,-10} {(m.FitForWork is bool f ? (f ? "fit" : "NOT fit") : string.Empty),-8} {m.Note}{(m.ReviewOn is DateOnly rv ? $" (review {D(rv)})" : string.Empty)}"));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Employee contacts, photo and dependants", ListTitle = "Beneficiaries and medical-aid dependants",
            Description = "The rest of the employee master: addresses, phone, job title, notice period and workers' compensation class (blank stays, a space clears); "
                + "the photograph (a JPEG, PNG or BMP file; blank removes it); and beneficiaries (with their share, all shares at most 100%) and medical-aid dependants.",
            Fields = [Pick("Step", "Step", ["Details", "Photo", "Add dependant", "Remove dependant"]), Opt("Number", "Employee number"), Opt("Physical", "Physical address"),
                Opt("Postal", "Postal address"), Opt("Phone", "Phone"), Opt("JobTitle", "Job title / position"), OptNum("Notice", "Notice period (days)"), Opt("Wcif", "Workers' compensation class"),
                Opt("PhotoFile", "Photograph file"), Pick("Kind", "Dependant is", Enum.GetNames<DependantKind>()), Opt("DependantName", "Dependant's name"), Opt("Relationship", "Relationship"),
                Opt("Born", "Born (yyyy-MM-dd)"), Opt("IdNumber", "ID number"), OptNum("Share", "Beneficiary share %"), Opt("MedicalAid", "Medical-aid number"), OptNum("Dependant", "Dependant id")],
            Run = async (c, i) =>
            {
                IEmployeeProfileService profile = c.Get<IEmployeeProfileService>();
                string number = i.Optional("Number") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Photo":
                        if (i.Optional("PhotoFile") is not string file)
                        {
                            _ = Check(await profile.SetPhotoAsync(c.CompanyId, number, null, null).ConfigureAwait(true), string.Empty);
                            return "Photograph removed.";
                        }

                        string type = System.IO.Path.GetExtension(file).ToLowerInvariant() switch { ".png" => "image/png", ".bmp" => "image/bmp", _ => "image/jpeg" };
                        _ = Check(await profile.SetPhotoAsync(c.CompanyId, number, await System.IO.File.ReadAllBytesAsync(file).ConfigureAwait(true), type).ConfigureAwait(true), string.Empty);
                        return "Photograph saved.";
                    case "Add dependant":
                        long id = Check(await profile.AddDependantAsync(c.CompanyId, number, new NewDependant(Enum.Parse<DependantKind>(i.Text("Kind")), i.Optional("DependantName") ?? string.Empty,
                            i.Optional("Relationship") ?? string.Empty)
                        {
                            DateOfBirth = i.Optional("Born") is string born ? DateOnly.ParseExact(born, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null, IdNumber = i.Optional("IdNumber"),
                            SharePercent = i.OptionalNumber("Share"), MedicalAidNumber = i.Optional("MedicalAid"),
                        }).ConfigureAwait(true));
                        return $"Added (id {id}).";
                    case "Remove dependant":
                        _ = Check(await profile.RemoveDependantAsync(c.CompanyId, (long)(i.OptionalNumber("Dependant") ?? 0m)).ConfigureAwait(true), string.Empty);
                        return "Removed.";
                    default:
                        _ = Check(await profile.SetDetailsAsync(c.CompanyId, number, new EmployeeProfileUpdate
                        {
                            PhysicalAddress = i.Optional("Physical"), PostalAddress = i.Optional("Postal"), Phone = i.Optional("Phone"), JobTitle = i.Optional("JobTitle"),
                            NoticePeriodDays = i.OptionalNumber("Notice") is decimal notice ? (int)notice : null, WorkersCompensationClass = i.Optional("Wcif"),
                        }).ConfigureAwait(true), string.Empty);
                        return $"Employee {number.ToUpperInvariant()} updated.";
                }
            },
            List = async c => Table(await c.Db.EmployeeDependants.AsNoTracking().Where(d => d.CompanyId == c.CompanyId)
                    .Join(c.Db.Employees, d => d.EmployeeId, e => e.Id, (d, e) => new { d, e.EmployeeNumber }).OrderBy(x => x.EmployeeNumber).ThenBy(x => x.d.Kind).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Id", x => x.d.Id), ("Employee", x => x.EmployeeNumber), ("Name", x => x.d.Name), ("Is", x => x.d.Kind), ("Relationship", x => x.d.Relationship),
                ("Share %", x => x.d.SharePercent?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty), ("Medical aid", x => x.d.MedicalAidNumber ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Employee record", ListTitle = "Employees",
            Description = "Engage an employee, or update one (blank fields stay as they are). Bank details are set at engagement; later changes go through 'Bank detail change' "
                + "and a second person's approval.",
            Fields = [Pick("Step", "Step", ["Engage", "Update"]), Req("Number", "Employee number"), Opt("Name", "Full name"), OptNum("Basic", "Basic salary"), Today("Engaged", "Engagement date"),
                Pick("Type", "Employment", Enum.GetNames<EmploymentType>()), Opt("Department", "Department code (3)"), Opt("CostCentre", "Cost centre code (3)"), Opt("PayGroup", "Pay group"), Opt("Grade", "Grade"),
                Opt("NationalId", "National ID"), Opt("TaxNumber", "Tax number"), Opt("ContractEnd", "Contract end (yyyy-MM-dd)"), Pick("Method", "Paid by", Enum.GetNames<PayMethod>()),
                Opt("Bank", "Bank (engage only)"), Opt("Branch", "Bank branch (engage only)"), Opt("Account", "Account number (engage only)"), Opt("Mobile", "Mobile money (engage only)"), Opt("Email", "Email"),
                Pick("Frequency", "Paid", Enum.GetNames<PayFrequency>()), Opt("WorkBranch", "Works at branch (code)"), Opt("Project", "Charged to project"), Opt("Fund", "Charged to fund"),
                Opt("ReportsTo", "Reports to (employee number)"), Pick("Marital", "Marital status", Enum.GetNames<MaritalStatus>()), Opt("Nationality", "Nationality"),
                Opt("SocialSecurity", "Social security number"), Opt("Union", "Trade union"), Opt("KinName", "Next of kin"), Opt("KinPhone", "Next of kin's phone"),
                Opt("KinRelationship", "Next of kin is (relationship)"), Opt("Tenancy", "Housing tenancy number (update; rent recovered from pay; '-' clears)")],
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
                        PaymentMethod = method, Email = i.Optional("Email"), PayFrequency = Enum.Parse<PayFrequency>(i.Text("Frequency")), Branch = i.Optional("WorkBranch"),
                        Project = i.Optional("Project"), Fund = i.Optional("Fund"), ReportsTo = i.Optional("ReportsTo"), MaritalStatus = Enum.Parse<MaritalStatus>(i.Text("Marital")),
                        Nationality = i.Optional("Nationality"), SocialSecurityNumber = i.Optional("SocialSecurity"), UnionMembership = i.Optional("Union"), NextOfKinName = i.Optional("KinName"),
                        NextOfKinPhone = i.Optional("KinPhone"), NextOfKinRelationship = i.Optional("KinRelationship"),
                        HousingTenancyNumber = i.Optional("Tenancy") is string tenancy ? (tenancy.Trim() == "-" ? string.Empty : tenancy) : null,
                    }).ConfigureAwait(true), string.Empty);
                    return $"Employee {i.Text("Number").ToUpperInvariant()} updated.";
                }

                long id = Check(await setup.CreateEmployeeAsync(c.CompanyId, new NewEmployee(i.Text("Number"), i.Optional("Name") ?? string.Empty, i.OptionalNumber("Basic") ?? 0m, i.Date("Engaged"))
                {
                    EmploymentType = type, Department = i.Optional("Department"), CostCentre = i.Optional("CostCentre"), PayGroup = i.Optional("PayGroup"), Grade = i.Optional("Grade"),
                    NationalIdNumber = i.Optional("NationalId"), TaxNumber = i.Optional("TaxNumber"), ContractEndDate = contractEnd, PaymentMethod = method, BankName = i.Optional("Bank"),
                    BankBranch = i.Optional("Branch"), BankAccountNumber = i.Optional("Account"), MobileMoneyNumber = i.Optional("Mobile"), Email = i.Optional("Email"),
                    PayFrequency = Enum.Parse<PayFrequency>(i.Text("Frequency")), Branch = i.Optional("WorkBranch"), Project = i.Optional("Project"), Fund = i.Optional("Fund"),
                    ReportsTo = i.Optional("ReportsTo"), MaritalStatus = Enum.Parse<MaritalStatus>(i.Text("Marital")), Nationality = i.Optional("Nationality"),
                    SocialSecurityNumber = i.Optional("SocialSecurity"), UnionMembership = i.Optional("Union"), NextOfKinName = i.Optional("KinName"), NextOfKinPhone = i.Optional("KinPhone"),
                    NextOfKinRelationship = i.Optional("KinRelationship"),
                }).ConfigureAwait(true));
                return $"Employee {i.Text("Number").ToUpperInvariant()} engaged (id {id}).";
            },
            List = async c => Table(await c.Db.Employees.AsNoTracking().Where(e => e.CompanyId == c.CompanyId).OrderBy(e => e.EmployeeNumber).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Number", e => e.EmployeeNumber), ("Name", e => e.FullName), ("Department", e => e.Department ?? string.Empty), ("Grade", e => e.Grade ?? string.Empty),
                ("Type", e => e.EmploymentType), ("Job title", e => e.JobTitle ?? string.Empty), ("Phone", e => e.Phone ?? string.Empty), ("Status", e => e.Status),
                ("Engaged", e => D(e.EngagementDate))),
        };
        yield return new Operation
        {
            Module = module, Title = "Pay elements and tax tables", ListTitle = "Pay elements",
            Description = "Create a pay element or edit one (every field but its code and type, entered in full as for a new one), assign one to (or end one for) an employee, "
                + "create a tax table - bands as 'from-to:rate' separated by ';', the last open-ended, e.g. "
                + "'0-100:0; 100-300:20; 300-:30' - or add the standard elements (BASIC, OVERTIME, HOUSING, TRANSPORT, PAYE on the tax table).",
            Fields = [Pick("Step", "Step", ["Create element", "Edit element", "Set element options", "Assign to employee", "End for employee", "Create tax table", "Add standard elements", "Preview formula"]),
                Opt("Code", "Element / table code"),
                Opt("Name", "Element name"), Pick("Type", "Element type", Enum.GetNames<PayElementType>()), Pick("Basis", "Calculation", Enum.GetNames<CalculationBasis>()), OptNum("Sequence", "Sequence"),
                Opt("Account", "Account (salaries expense for the standard set)"), Opt("Liability", "PAYE liability account (standard set)"), OptNum("Amount", "Fixed / override amount"),
                OptNum("Rate", "Rate / override rate"), OptNum("Units", "Units"), Opt("Formula", "Formula"), Tick("Taxable", "Taxable"), Opt("TaxTable", "Tax table code"),
                EmployeePick("Employee", "Employee", required: false), Today("From", "Tax table effective from"), Opt("Bands", "Tax bands"), Tick("Pensionable", "Pensionable"),
                Tick("SocialSecurity", "Subject to social security"), Tick("OvertimeBase", "In the overtime base"), Tick("LeaveBase", "In the leave-pay base"), Opt("Group", "Adds to group"),
                Tick("ProRated", "Pro-rated for a part period"), Tick("NoArrears", "Never goes into arrears (deduction)"), Tick("Terminal", "Counts towards terminal benefits"), Tick("Union", "Subject to union dues"),
                OptNum("Balance", "Balance to recover (assign; balance-driven element)"),
                Pick("Recovers", "Recovers (deduction)", Enum.GetNames<PayRecovery>()), Opt("BenefitCategory", "Taxable-benefit category (PR34), e.g. Housing"), Opt("PaidIn", "Paid in currency (blank: base)"),
                Tick("CumulativeTax", "Cumulative tax (year to date)"), Tick("SplitTax", "Tax per currency (split-currency pay)"), Tick("Annual", "Annual payment (bonus tax spread)"),
                Opt("TableCurrency", "Tax table currency (blank: base; one table per currency for split-currency tax)"),
                Opt("BasisGroup", "Percentage of group (percent-of-group)"), OptNum("Floor", "Floor"), OptNum("Ceiling", "Ceiling"), OptNum("RoundTo", "Round to (unit)"),
                Pick("Rounding", "Rounding", Enum.GetNames<PayRounding>()), Opt("Expense", "Expense account (contribution / provision)"), Opt("DeptAccounts", "Accounts by department: DEPT=ACCOUNT; …"),
                Opt("LookupVariable", "Lookup variable (table lookup; blank: BASIC)"), Opt("LookupBands", "Lookup bands from-to:amount or rate%; … (table lookup)"),
                Opt("Values", "Preview values NAME=value; …")],
            Run = async (c, i) =>
            {
                IPayrollSetupService setup = c.Get<IPayrollSetupService>();
                switch (i.Text("Step"))
                {
                    case "Assign to employee":
                        _ = Check(await setup.AssignElementAsync(c.CompanyId, i.Optional("Employee") ?? string.Empty, i.Optional("Code") ?? string.Empty, i.OptionalNumber("Amount"), i.OptionalNumber("Rate"),
                            i.OptionalNumber("Units"), i.OptionalNumber("Balance")).ConfigureAwait(true), string.Empty);
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

                        CurrencyCode? tableCurrency = i.Optional("TableCurrency") is string tc && tc.Trim().Length > 0
                            ? CurrencyCode.TryParse(tc.Trim(), out CurrencyCode parsedCurrency) ? parsedCurrency : throw new OperationException($"{tc} is not a currency code.")
                            : null;
                        long table = Check(await setup.CreateTaxTableAsync(c.CompanyId, i.Optional("Code") ?? string.Empty, i.Date("From"), tableCurrency, bands).ConfigureAwait(true));
                        return $"Tax table created (id {table}){(tableCurrency is null ? string.Empty : $" in {tableCurrency}")}.";
                    case "Set element options":
                        _ = Check(await setup.SetPayElementOptionsAsync(c.CompanyId, i.Optional("Code") ?? string.Empty, new PayElementOptions(Enum.Parse<PayRecovery>(i.Text("Recovers")),
                            i.Optional("BenefitCategory"), i.Optional("PaidIn"), i.Flag("CumulativeTax"), i.Flag("SplitTax"), i.Flag("Annual"))).ConfigureAwait(true), string.Empty);
                        return $"Options set on {i.Optional("Code")?.ToUpperInvariant()}.";
                    case "Preview formula":
                        Dictionary<string, decimal> values = new(StringComparer.OrdinalIgnoreCase);
                        foreach (string pair in (i.Optional("Values") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] bits = pair.Split('=', StringSplitOptions.TrimEntries);
                            values[bits[0]] = bits.Length == 2 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v)
                                ? v : throw new OperationException($"Write each value as NAME=number (\"{pair}\").");
                        }

                        decimal previewed = Check(await setup.PreviewFormulaAsync(c.CompanyId, i.Optional("Formula") ?? string.Empty, i.Optional("Employee"), values).ConfigureAwait(true));
                        return $"{i.Optional("Formula")} = {M(previewed)}";
                    case "Add standard elements":
                        IReadOnlyList<string> added = Check(await setup.SeedStandardElementsAsync(c.CompanyId, i.Optional("Account") ?? string.Empty, i.Optional("Liability") ?? string.Empty,
                            i.Optional("TaxTable") ?? "PAYE").ConfigureAwait(true));
                        return added.Count == 0 ? "The standard elements are all there already." : $"Added {string.Join(", ", added)}.";
                    case "Edit element":
                        _ = Check(await setup.EditPayElementAsync(c.CompanyId, ElementFrom(i)).ConfigureAwait(true), string.Empty);
                        return $"Pay element {i.Optional("Code")?.ToUpperInvariant()} changed.";
                    default:
                        long element = Check(await setup.CreatePayElementAsync(c.CompanyId, ElementFrom(i)).ConfigureAwait(true));
                        return $"Pay element created (id {element}).";
                }
            },
            List = async c => Table(await c.Db.PayElements.AsNoTracking().Where(e => e.CompanyId == c.CompanyId).OrderBy(e => e.Sequence).ThenBy(e => e.Code).ToListAsync().ConfigureAwait(true),
                ("Code", e => e.Code), ("Name", e => e.Name), ("Type", e => e.Type), ("Basis", e => e.Basis), ("Sequence", e => e.Sequence), ("Account", e => e.AccountCode),
                ("Taxable", e => e.Taxable), ("Formula / table", e => e.Formula ?? e.TaxTableCode ?? string.Empty),
                ("Options", e => string.Join(", ", new[] { e.Recovers == PayRecovery.None ? null : $"recovers {e.Recovers}", e.BenefitCategory is null ? null : $"benefit {e.BenefitCategory}",
                    e.PaidInCurrency is null ? null : $"paid in {e.PaidInCurrency}", e.CumulativeTax ? "cumulative tax" : null, e.SplitCurrencyTax ? "tax per currency" : null, e.IsAnnualPayment ? "annual" : null }
                    .Where(o => o is not null)))),
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
            Fields = [EmployeePick("Employee", "Employee"), Today(), Pick("Status", "Status", Enum.GetNames<AttendanceStatus>()), Opt("In", "Clock in (HH:mm)"), Opt("Out", "Clock out (HH:mm)"),
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
            Module = module, Title = "Shift rosters", ListTitle = "Roster (next 14 days)",
            Description = "Shift: a code, times (an end before the start runs past midnight), grace minutes and allowance per shift. Pattern: a cycle like \"D,D,N,N,OFF,OFF\". "
                + "Assign puts an employee on a pattern from an anchor date. Generate writes the roster for a period (days changed by hand are kept); Set day changes one day by hand. "
                + "Allowances sets each employee's shift allowance for a pay period from the rostered shifts they worked. Attendance lateness is measured against the rostered start.",
            Fields = [Pick("Step", "Step", ["Shift", "Pattern", "Assign", "Generate", "Set day", "Allowances"]), Opt("Code", "Shift / pattern code (or OFF)"), Opt("Name", "Shift name"),
                Opt("Start", "Start (HH:mm)"), Opt("End", "End (HH:mm)"), Num("Grace", "Grace minutes", "0"), Num("Allowance", "Allowance per shift", "0"), Opt("Steps", "Pattern steps"),
                EmployeePick("Employee", "Employee", required: false), Today("From", "From / day"), Today("To", "To"), Opt("Period", "Pay period (allowances)"), Opt("Element", "Allowance element")],
            Run = async (c, i) =>
            {
                IRosterService roster = c.Get<IRosterService>();
                static TimeOnly Time(string? text) => TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out TimeOnly t) ? t : throw new OperationException($"'{text}' is not a time (HH:mm).");
                switch (i.Text("Step"))
                {
                    case "Shift":
                        return Check(await roster.SaveShiftAsync(c.CompanyId, new ShiftRequest(i.Text("Code"), i.Optional("Name") ?? i.Text("Code"), Time(i.Optional("Start")), Time(i.Optional("End")),
                            (int)i.Number("Grace"), i.Number("Allowance"))).ConfigureAwait(true), "Shift saved.");
                    case "Pattern":
                        return Check(await roster.SavePatternAsync(c.CompanyId, i.Text("Code"), i.Optional("Steps") ?? string.Empty).ConfigureAwait(true), "Pattern saved.");
                    case "Assign":
                        return Check(await roster.AssignAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Code"), i.Date("From")).ConfigureAwait(true),
                            "Employee put on the pattern.");
                    case "Generate":
                        return $"{Check(await roster.GenerateAsync(c.CompanyId, i.Date("From"), i.Date("To")).ConfigureAwait(true))} rostered day(s) written.";
                    case "Set day":
                        return Check(await roster.SetDayAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Date("From"), i.Text("Code")).ConfigureAwait(true),
                            "Day changed.");
                    default:
                        IReadOnlyList<ShiftAllowanceLine> lines = Check(await roster.ApplyShiftAllowancesAsync(c.CompanyId, i.Text("Period"), i.Date("From"), i.Date("To"), i.Text("Element"))
                            .ConfigureAwait(true));
                        return $"{lines.Count} employee(s): {lines.Sum(l => l.ShiftsWorked)} shift(s), {M(lines.Sum(l => l.Amount))} in allowances.";
                }
            },
            List = async c => Table(await c.Get<IRosterService>().RosterAsync(c.CompanyId, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today).AddDays(13)).ConfigureAwait(true),
                ("Employee", d => d.EmployeeNumber), ("Date", d => D(d.Date)), ("Shift", d => d.Shift), ("By hand", d => d.IsManual), ("Attendance", d => d.Attendance ?? "-")),
        };
        yield return new Operation
        {
            Module = module, Title = "Leave policies", ListTitle = "Leave policies",
            Description = "Per leave type: yearly days (service bands \"5:24;10:30\" raise them after so many completed years), earned monthly or granted at the leave-year start, "
                + "joiners pro rata, and a carry-over cap applied as the leave year turns (the excess is forfeited). A balance ceiling stops accrual; carried days can lapse so many months "
                + "into the year; leave can be taken in advance down to so many days; sick tiers \"11:50;21:0\" pay so much from the nth sick day of the year. "
                + "Run month accrues for a month (the scheduled task does the month just ended); Apply sick pay sets the period's deductions. Advance leave pay pays the "
                + "leave starting in the month ahead into the pay period (LEAVEPAY) and recovers it in the recovery period (LEAVEADV).",
            Fields = [Pick("Step", "Step", ["Save policy", "Run month", "Apply sick pay", "Advance leave pay", "Credit time off in lieu"]), Req("Type", "Leave type", "ANNUAL"), OptNum("Days", "Days a year"), Tick("Monthly", "Earned monthly", true),
                Num("Start", "Leave year starts (month)", "1"), OptNum("Cap", "Carry-over cap (days, blank: none)"), Tick("ProRata", "Joiners pro rata", true), Opt("Bands", "Service bands (years:days;...)"),
                OptNum("Ceiling", "Balance ceiling (days, blank: none)"), OptNum("Lapse", "Carried days lapse after (months, blank: never)"), Num("Advance", "May go negative by (days)", "0"),
                Opt("Tiers", "Sick tiers (from day:pay %;...)"), Today("Month", "Month to run"), Opt("Period", "Pay period (sick pay)"), Opt("Element", "Deduction element (sick pay)"),
                Num("WorkDays", "Working days a month (sick pay)", "21.67"), Opt("Recovery", "Recovery period (advance)"), Opt("GradeDays", "Days by grade GRADE=days; … (policy)"),
                OptNum("MaxAway", "Most of a department away at once (policy)"), Opt("Employee", "Employee number (time off in lieu)"), OptNum("Hours", "Overtime hours (time off in lieu)"),
                Num("HoursPerDay", "Hours in a working day", "8"), Num("Ratio", "Hours off per hour worked", "1")],
            Run = async (c, i) =>
            {
                ILeavePolicyService policies = c.Get<ILeavePolicyService>();
                if (i.Text("Step") == "Run month")
                {
                    LeaveAccrualRun run = Check(await policies.RunMonthAsync(c.CompanyId, i.Date("Month")).ConfigureAwait(true));
                    return $"{run.Period}: {run.Accrued} balance(s) earned {Q(run.DaysAccrued)} day(s); {Q(run.DaysForfeited)} forfeited; {run.AlreadyRun} already run.";
                }

                if (i.Text("Step") == "Credit time off in lieu")
                {
                    decimal credited = Check(await policies.CreditTimeOffAsync(c.CompanyId, i.Optional("Employee") ?? string.Empty, i.OptionalNumber("Hours") ?? 0m, i.Number("HoursPerDay"),
                        i.Number("Ratio"), i.Text("Type")).ConfigureAwait(true));
                    return $"{Q(credited)} day(s) of {i.Text("Type").ToUpperInvariant()} credited.";
                }

                if (i.Text("Step") == "Advance leave pay")
                {
                    DateOnly month = i.Date("Month"), from = new(month.Year, month.Month, 1);
                    IReadOnlyList<LeaveAdvanceLine> advanced = Check(await policies.AdvanceLeavePayAsync(c.CompanyId, i.Text("Type"), from, from.AddMonths(1).AddDays(-1), i.Optional("Period") ?? string.Empty,
                        i.Optional("Recovery") ?? string.Empty, i.Optional("Element") ?? "LEAVEPAY", "LEAVEADV", i.Number("WorkDays")).ConfigureAwait(true));
                    return $"{advanced.Count} employee(s) going on leave; {Q(advanced.Sum(a => a.LeaveDays))} day(s), {M(advanced.Sum(a => a.Amount))} paid in advance.";
                }

                if (i.Text("Step") == "Apply sick pay")
                {
                    DateOnly month = i.Date("Month"), from = new(month.Year, month.Month, 1);
                    IReadOnlyList<SickPayLine> sick = Check(await policies.ApplySickPayAsync(c.CompanyId, i.Text("Type"), i.Text("Period"), from, from.AddMonths(1).AddDays(-1), i.Text("Element"),
                        i.Number("WorkDays")).ConfigureAwait(true));
                    return $"{sick.Count} employee(s) off sick; {Q(sick.Sum(s => s.UnpaidDays))} unpaid day(s), {M(sick.Sum(s => s.Deduction))} deducted.";
                }

                List<(int, decimal)> tiers = [];
                foreach (string tier in (i.Optional("Tiers") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = tier.Split(':', StringSplitOptions.TrimEntries);
                    tiers.Add(bits.Length == 2 && int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)
                        && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal pct) ? (day, pct) : throw new OperationException($"Write each tier as day:pay % (\"{tier}\")."));
                }

                List<(int, decimal)> bands = [];
                foreach (string band in (i.Optional("Bands") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = band.Split(':', StringSplitOptions.TrimEntries);
                    bands.Add(bits.Length == 2 && int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int years)
                        && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal days) ? (years, days) : throw new OperationException($"Write each band as years:days (\"{band}\")."));
                }

                return Check(await policies.SavePolicyAsync(new LeavePolicyRequest(c.CompanyId, i.Text("Type"), i.OptionalNumber("Days") ?? 0m, i.Flag("Monthly"), i.Whole("Start"), i.OptionalNumber("Cap"),
                    i.Flag("ProRata"), bands, i.OptionalNumber("Ceiling"), i.OptionalNumber("Lapse") is decimal lapse ? (int)lapse : null, i.Number("Advance"), tiers)
                {
                    GradeDays = i.Optional("GradeDays"), MaxAwayPerDepartment = i.OptionalNumber("MaxAway") is decimal away ? (int)away : null,
                }).ConfigureAwait(true),
                    "Leave policy saved.");
            },
            List = async c => Table(await c.Db.Set<Core.Payroll.LeavePolicy>().AsNoTracking().Include(p => p.Bands).Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.LeaveType).ToListAsync().ConfigureAwait(true),
                ("Type", p => p.LeaveType), ("Days", p => Q(p.AnnualDays)), ("Monthly", p => p.AccruesMonthly), ("Year starts", p => p.LeaveYearStartMonth), ("Cap", p => p.CarryOverCapDays),
                ("Bands", p => string.Join("; ", p.Bands.Select(b => $"{b.FromYears}y:{Q(b.AnnualDays)}")))),
        };
        yield return new Operation
        {
            Module = module, Title = "Accrue leave", ListTitle = "Leave balances", Description = "Adds days to an employee's leave balance.",
            Fields = [EmployeePick("Employee", "Employee"), Req("Type", "Leave type", "ANNUAL"), Num("Days", "Days")],
            Run = async (c, i) => $"Balance now {Q(Check(await c.Get<ILeaveService>().AccrueAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Type"), i.Number("Days")).ConfigureAwait(true)))} days.",
            List = LeaveBalancesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Tax credits", ListTitle = "Tax credits",
            Description = "Credits taken off the tax the table works out, never below nil: a fixed amount a month (say an elderly credit from 65) or a percentage of a pay element "
                + "(say half the medical-aid deduction), for everyone or only the employees given it.",
            Fields = [Pick("Step", "Step", ["Save credit", "Give to employee", "Take from employee"]), Req("Code", "Credit code"), Opt("Name", "Name"),
                Pick("Basis", "Basis", Enum.GetNames<TaxCreditBasis>()), Num("Amount", "Monthly amount (fixed)", "0"), Num("Percent", "Percent (of an element)", "0"), Opt("Element", "Pay element"),
                OptNum("Age", "From age"), Tick("All", "Applies to every employee"), Opt("Employee", "Employee number (give / take)")],
            Run = async (c, i) =>
            {
                IPayrollSetupService setup = c.Get<IPayrollSetupService>();
                return i.Text("Step") switch
                {
                    "Save credit" => Check(await setup.SaveTaxCreditAsync(c.CompanyId, new TaxCreditInput(i.Text("Code"), i.Optional("Name") ?? i.Text("Code"), Enum.Parse<TaxCreditBasis>(i.Text("Basis")),
                        i.Number("Amount"), i.Number("Percent"), i.Optional("Element"), (int?)i.OptionalNumber("Age"), i.Flag("All"))).ConfigureAwait(true), "Tax credit saved."),
                    var step => Check(await setup.SetEmployeeTaxCreditAsync(c.CompanyId, i.Optional("Employee") ?? string.Empty, i.Text("Code"), step == "Give to employee").ConfigureAwait(true),
                        step == "Give to employee" ? "Credit given." : "Credit taken away."),
                };
            },
            List = async c => Table(await c.Db.Set<TaxCredit>().AsNoTracking().Where(t => t.CompanyId == c.CompanyId).OrderBy(t => t.Code).ToListAsync().ConfigureAwait(true),
                ("Code", t => t.Code), ("Name", t => t.Name), ("Basis", t => t.Basis), ("Amount", t => M(t.Amount)), ("Percent", t => Q(t.Percent)), ("Element", t => t.ElementCode),
                ("From age", t => t.MinimumAge), ("Everyone", t => t.AppliesToAll), ("Active", t => t.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Take leave", ListTitle = "Leave balances", Description = "Records leave taken over a period (never more than the balance).",
            Fields = [EmployeePick("Employee", "Employee"), Req("Type", "Leave type", "ANNUAL"), Today("From", "From"), Today("To", "To"), Num("Days", "Days")],
            Run = async (c, i) => Check(await c.Get<IAttendanceService>().TakeLeaveAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Type"), i.Date("From"), i.Date("To"),
                i.Number("Days")).ConfigureAwait(true), "Leave recorded."),
            List = LeaveBalancesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Pay and close a payroll run", ListTitle = "Payroll runs",
            Description = "After commit: Pay posts the run's net pay from the account for each pay method (bank transfers, cash, mobile money, cheques) - earnings paid in another "
                + "currency go out of that currency's account at the pay date's rate - and clears the net pay control; "
                + "Close closes the run once its net pay control is clear; Stage shows where a run stands.",
            Fields = [Pick("Step", "Step", ["Pay", "Close", "Stage"]), Req("Run", "Run document number"), Opt("Bank", "Bank account for transfers", "1100"), Opt("Cash", "Cash account"),
                Opt("Mobile", "Mobile-money float account"), Opt("Cheque", "Bank account cheques are drawn on"), Opt("Foreign", "Foreign-currency accounts (EUR:1120; ZAR:1130)"), Today()],
            Run = async (c, i) =>
            {
                IPayrollSettlementService settlement = c.Get<IPayrollSettlementService>();
                switch (i.Text("Step"))
                {
                    case "Pay":
                        Dictionary<Core.Payroll.PayMethod, string> accounts = [];
                        void Use(Core.Payroll.PayMethod method, string field)
                        {
                            if (i.Optional(field) is string account)
                            {
                                accounts[method] = account;
                            }
                        }

                        Use(Core.Payroll.PayMethod.BankTransfer, "Bank");
                        Use(Core.Payroll.PayMethod.Cash, "Cash");
                        Use(Core.Payroll.PayMethod.MobileMoney, "Mobile");
                        Use(Core.Payroll.PayMethod.Cheque, "Cheque");
                        Dictionary<Core.Accounting.CurrencyCode, string> foreign = [];
                        foreach (string part in (i.Optional("Foreign") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                            foreign[bits.Length == 2 ? Core.Accounting.CurrencyCode.Parse(bits[0]) : throw new OperationException($"Write each as CUR:account (\"{part}\").")] = bits[1];
                        }

                        PayrollPayment paid = Check(await settlement.PayAsync(c.CompanyId, i.Text("Run"), i.Date("Date"), accounts, foreign).ConfigureAwait(true));
                        return $"Paid in journal {paid.JournalNumber}: " + string.Join("; ", paid.ByMethod.Select(m => $"{m.Method} {M(m.Amount)} from {m.FundingAccountCode}")
                            .Concat(paid.ByCurrency.Select(f => $"{f.Currency} {M(f.Amount)} ({M(f.BaseAmount)} base) from {f.FundingAccountCode}")));
                    case "Close":
                        PayrollRunStage closed = Check(await settlement.CloseAsync(c.CompanyId, i.Text("Run"), i.Date("Date")).ConfigureAwait(true));
                        return $"Run {closed.DocumentNumber} closed on {D(closed.ClosedOn!.Value)}.";
                    default:
                        PayrollRunStage stage = Check(await settlement.StageAsync(c.CompanyId, i.Text("Run")).ConfigureAwait(true));
                        return $"Run {stage.DocumentNumber}: net {M(stage.Net)}; paid {(stage.PaidOn is DateOnly p ? D(p) : "not yet")}; closed {(stage.ClosedOn is DateOnly cl ? D(cl) : "not yet")}; "
                            + $"{M(stage.NetPayOutstanding)} still on the net pay control.";
                }
            },
            List = async c => Table(await c.Db.Set<Core.Payroll.PayrollRunRecord>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderByDescending(r => r.PayDate).Page(c, 200)
                    .ToListAsync().ConfigureAwait(true),
                ("Run", r => r.DocumentNumber), ("Period", r => r.PeriodLabel), ("Group", r => r.PayGroup), ("Kind", r => r.Kind), ("Net", r => M(r.Net)),
                ("Paid", r => r.PaidOn is DateOnly p ? D(p) : "-"), ("Closed", r => r.ClosedOn is DateOnly cl ? D(cl) : "-"), ("Reversed", r => r.IsReversed ? "yes" : "")),
        };
        yield return new Operation
        {
            Module = module, Title = "Import time-clock punches", ListTitle = "Leave balances",
            Description = "Reads the clock's export (badge,date time per line; CSV or Excel): each employee's first punch in and last punch out make the day, late against the "
                + "rostered shift, with the hours beyond the shift as overtime awaiting approval. Days already recorded are left alone.",
            Fields = [Req("File", "Export file (CSV or .xlsx)"), Num("Rate", "Overtime rate", "1.5")],
            Run = async (c, i) =>
            {
                byte[] file = await System.IO.File.ReadAllBytesAsync(i.Text("File")).ConfigureAwait(true);
                string text = Application.Onboarding.XlsxReader.IsWorkbook(file) ? Application.Onboarding.XlsxReader.ToCsv(file) : System.Text.Encoding.UTF8.GetString(file);
                ClockImport imported = Check(await c.Get<IAttendanceService>().ImportClockAsync(c.CompanyId, text, i.Number("Rate")).ConfigureAwait(true));
                return string.Join(Environment.NewLine, new[] { $"{imported.DaysRecorded} day(s) recorded; {imported.AlreadyRecorded} already on file." }
                    .Concat(imported.UnknownBadges.Count == 0 ? [] : [$"Unknown badges: {string.Join(", ", imported.UnknownBadges)}."])
                    .Concat(imported.Problems));
            },
            List = LeaveBalancesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Leave calendar and recall", ListTitle = "Leave this month",
            Description = "Shows who is on leave between two dates (optionally one department); cancels leave not yet begun, or cuts it short when someone is recalled - the days "
                + "not taken go back to the balance (pro rata unless given).",
            Fields = [Pick("Step", "Step", ["Calendar", "Cancel or recall"]), Today("From", "From"), Today("To", "To"), Opt("Department", "Department (calendar)"),
                OptNum("Leave", "Leave record id (cancel)"), Opt("Returned", "Back at work on (yyyy-mm-dd; blank: cancel outright)"), OptNum("DaysBack", "Days given back (blank: pro rata)")],
            Run = async (c, i) =>
            {
                IAttendanceService attendance = c.Get<IAttendanceService>();
                if (i.Text("Step") == "Calendar")
                {
                    IReadOnlyList<LeaveCalendarEntry> calendar = await attendance.LeaveCalendarAsync(c.CompanyId, i.Date("From"), i.Date("To"), i.Optional("Department")).ConfigureAwait(true);
                    return calendar.Count == 0 ? "Nobody is on leave then." : string.Join(Environment.NewLine, calendar.Select(e =>
                        $"#{e.LeaveRecordId,-6} {e.EmployeeNumber,-10} {e.Name,-24} {e.LeaveType,-10} {D(e.From)} – {D(e.To)}  {Q(e.Days)} day(s)  {e.Department}"));
                }

                DateOnly? returned = i.Optional("Returned") is string r ? DateOnly.Parse(r, System.Globalization.CultureInfo.InvariantCulture) : null;
                decimal back = Check(await attendance.CancelLeaveAsync(c.CompanyId, (long)(i.OptionalNumber("Leave") ?? 0m), returned, i.OptionalNumber("DaysBack")).ConfigureAwait(true));
                return $"{Q(back)} day(s) back to the balance.";
            },
            List = async c =>
            {
                DateOnly today = DateOnly.FromDateTime(DateTime.Today), first = new(today.Year, today.Month, 1);
                return Table(await c.Get<IAttendanceService>().LeaveCalendarAsync(c.CompanyId, first, first.AddMonths(1).AddDays(-1)).ConfigureAwait(true),
                    ("Id", e => e.LeaveRecordId), ("Employee", e => e.EmployeeNumber), ("Name", e => e.Name), ("Type", e => e.LeaveType), ("From", e => D(e.From)), ("To", e => D(e.To)),
                    ("Days", e => Q(e.Days)), ("Department", e => e.Department));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Bonus and gratuity provisions", ListTitle = "Leave balances",
            Description = "Trues up the 13th-cheque bonus provision (earned through the bonus year), the gratuity provision (days' pay per year of service, discounted to "
                + "retirement when a discount rate is given) or the long-service award provision (milestones YEARS:months' pay; …, earned towards the next) to what active "
                + "employees have earned to the date, posting the movement between the expense and the provision.",
            Fields = [Pick("Kind", "Provision", Enum.GetNames<PayrollProvisionKind>()), Today(), Today("YearStart", "Bonus year starts"), Num("Months", "Months' pay a year (bonus)", "1"),
                Num("DaysPerYear", "Days' pay per year of service (gratuity)", "15"), Num("WorkingDays", "Working days a month (gratuity)", "26"), Req("Expense", "Expense account"),
                Req("Provision", "Provision account"), Req("Document", "Document number"), Opt("Awards", "Long-service milestones YEARS:months' pay; … (e.g. 10:1; 20:2)"),
                Num("Discount", "Discount rate % (gratuity; 0: none)", "0"), Num("Growth", "Salary growth % (gratuity)", "0"), Num("Retire", "Retirement age (gratuity)", "65")],
            Run = async (c, i) => $"Provision now {M(Check(await c.Get<ILeaveService>().RaisePayrollProvisionAsync(new PayrollProvisionRequest
            {
                CompanyId = c.CompanyId, Kind = Enum.Parse<PayrollProvisionKind>(i.Text("Kind")), Date = i.Date("Date"), YearStart = i.Date("YearStart"), MonthsOfPay = i.Number("Months"),
                DaysPerYear = i.Number("DaysPerYear"), WorkingDaysPerMonth = i.Number("WorkingDays"), ExpenseAccountCode = i.Text("Expense"), ProvisionAccountCode = i.Text("Provision"),
                DocumentNumber = i.Text("Document"), DiscountRatePercent = i.Number("Discount"), SalaryGrowthPercent = i.Number("Growth"), RetirementAge = i.Whole("Retire"),
                LongServiceAwards = [.. (i.Optional("Awards") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(a => a.Split(':', StringSplitOptions.TrimEntries))
                    .Select(a => a.Length == 2 && int.TryParse(a[0], out int years) && decimal.TryParse(a[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal months)
                        ? (years, months) : throw new OperationException("Write each milestone as YEARS:months' pay."))],
            }).ConfigureAwait(true)))}.",
            List = LeaveBalancesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Encash leave", ListTitle = "Leave balances", Description = "Pays out leave days at a daily rate.",
            Fields = [EmployeePick("Employee", "Employee"), Req("Type", "Leave type", "ANNUAL"), Today(), Num("Days", "Days"), Num("Rate", "Daily rate")],
            Run = async (c, i) => Check(await c.Get<IAttendanceService>().EncashLeaveAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Type"), i.Date("Date"),
                i.Number("Days"), i.Number("Rate")).ConfigureAwait(true), $"Encashed {M(i.Number("Days") * i.Number("Rate"))}."),
            List = LeaveBalancesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Set the establishment", Description = "Approved posts and annual cost per post for a department and grade.",
            Fields = [Req("Department", "Department"), Req("Grade", "Grade"), Num("Posts", "Approved posts"), Num("Cost", "Annual cost per post")],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().SetEstablishmentAsync(c.CompanyId, i.Text("Department"), i.Text("Grade"), i.Whole("Posts"), i.Number("Cost")).ConfigureAwait(true), "Establishment saved."),
            List = async c => Table(await c.Get<IEstablishmentService>().VacanciesAsync(c.CompanyId).ConfigureAwait(true),
                ("Department", v => v.Department), ("Grade", v => v.Grade), ("Approved", v => v.Approved), ("Filled", v => v.Filled), ("Vacant", v => v.Vacant)),
            ListTitle = "Posts filled and vacant",
        };
        yield return new Operation
        {
            Module = module, Title = "Set a salary scale notch", ListTitle = "Salary scales",
            Description = "A grade's monthly salary at a notch, from a date. Once a company keeps posts, staff can only be engaged or moved into a vacant approved post.",
            Fields = [Req("Grade", "Grade"), Num("Notch", "Notch (1 is the bottom)"), Num("Amount", "Monthly salary"), new("From", "Effective from", FieldKind.Date)],
            Run = async (c, i) => Check(await c.Get<IEstablishmentService>().SetScaleAsync(c.CompanyId, i.Text("Grade"), i.Whole("Notch"), i.Number("Amount"), i.Date("From")).ConfigureAwait(true),
                "Scale notch saved."),
            List = async c => Table(await c.Db.SalaryScales.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).OrderBy(s => s.Grade).ThenByDescending(s => s.EffectiveFrom).ThenBy(s => s.Notch)
                .ToListAsync().ConfigureAwait(true), ("Grade", s => s.Grade), ("Notch", s => s.Notch), ("Monthly", s => M(s.MonthlyAmount)), ("From", s => D(s.EffectiveFrom))),
        };
        yield return new Operation
        {
            Module = module, Title = "Run the annual increment", ListTitle = "Staff on scales",
            Description = "Moves every active employee on a scaled grade up one notch, to that notch's salary, unless at the top or incremented within the year.",
            Fields = [Today()],
            Run = async (c, i) => Check(await c.Get<IEstablishmentService>().RunIncrementsAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true),
                r => $"{r.Moved.Count} employee(s) moved up a notch; {r.AtTop.Count} at the top of their scale."),
            List = async c => Table(await c.Db.Employees.AsNoTracking().Where(e => e.CompanyId == c.CompanyId && e.Grade != null && e.Notch != null).OrderBy(e => e.EmployeeNumber).ToListAsync()
                .ConfigureAwait(true), ("Number", e => e.EmployeeNumber), ("Name", e => e.FullName), ("Grade", e => e.Grade), ("Notch", e => e.Notch), ("Basic salary", e => M(e.BasicSalary)),
                ("Last increment", e => e.LastIncrementOn is DateOnly d ? D(d) : "-")),
        };
        yield return new Operation
        {
            Module = module, Title = "Appoint to act", ListTitle = "Acting appointments",
            Description = "An employee acting in a higher grade for a time is paid ACTINGALLOWANCE (that grade's scale less their basic) through an element whose formula is ACTINGALLOWANCE.",
            Fields = [EmployeePick("Employee", "Employee"), Req("Grade", "Acting grade"), Num("Notch", "Notch"), new("From", "From", FieldKind.Date), new("To", "To", FieldKind.Date, Required: false)],
            Run = async (c, i) => Check(await c.Get<IEstablishmentService>().AppointActingAsync(c.CompanyId, i.Text("Employee"), i.Text("Grade"), i.Whole("Notch"), i.Date("From"),
                i.OptionalDate("To")).ConfigureAwait(true), "Acting appointment recorded."),
            List = async c => Table(await c.Db.ActingAppointments.AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.From)
                .Join(c.Db.Employees.AsNoTracking(), a => a.EmployeeId, e => e.Id, (a, e) => new { e.EmployeeNumber, e.FullName, a.Grade, a.Notch, a.From, a.To }).Page(c, 200).ToListAsync()
                .ConfigureAwait(true), ("Employee", a => a.EmployeeNumber), ("Name", a => a.FullName), ("Grade", a => a.Grade), ("Notch", a => a.Notch), ("From", a => D(a.From)),
                ("To", a => a.To is DateOnly t ? D(t) : "open")),
        };
        yield return new Operation
        {
            Module = module, Title = "File an employee document", ListTitle = "Documents expiring in 90 days", Description = "A contract, permit, licence or certificate on the employee's file.",
            Fields = [EmployeePick("Employee", "Employee"), Req("Type", "Document type"), Opt("Reference", "Reference"), new("Expires", "Expires on", FieldKind.Date, Required: false)],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().FileDocumentAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Type"), i.Optional("Reference"),
                i.OptionalDate("Expires")).ConfigureAwait(true), "Document filed."),
            List = ExpiringDocumentsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Record a disciplinary action", Description = "The offence and the sanction issued.",
            Fields = [EmployeePick("Employee", "Employee"), Today(), Req("Offence", "Offence"), Pick("Sanction", "Sanction", Enum.GetNames<DisciplinarySanction>())],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().RecordDisciplinaryAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Date("Date"), i.Text("Offence"),
                Enum.Parse<DisciplinarySanction>(i.Text("Sanction")), c.UserId).ConfigureAwait(true), "Recorded."),
        };
        yield return new Operation
        {
            Module = module, Title = "Record training", Description = "A completed course, its cost and when it must be renewed.",
            Fields = [EmployeePick("Employee", "Employee"), Req("Course", "Course"), Today("Completed", "Completed on"), new("Expires", "Expires on", FieldKind.Date, Required: false), Opt("Provider", "Provider"), Num("Cost", "Cost", "0")],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().RecordTrainingAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Course"), i.Date("Completed"),
                i.OptionalDate("Expires"), i.Optional("Provider"), i.Number("Cost")).ConfigureAwait(true), "Training recorded."),
        };
        yield return new Operation
        {
            Module = module, Title = "Record an appraisal", Description = "Rating 1–5 and the next appraisal date.",
            Fields = [EmployeePick("Employee", "Employee"), Today(), Num("Rating", "Rating (1–5)", "3"), Today("Next", "Next due")],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().RecordAppraisalAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Date("Date"), i.Whole("Rating"),
                i.Date("Next")).ConfigureAwait(true), "Appraisal recorded."),
        };
        yield return new Operation
        {
            Module = module, Title = "Register a garnishee order", Description = "A court order deducted from pay through a deduction element until the total is settled.",
            Fields = [EmployeePick("Employee", "Employee"), Req("Reference", "Order reference"), Req("Creditor", "Creditor"), Num("Priority", "Priority", "1"), Num("Instalment", "Instalment per period"),
                Num("Total", "Total amount"), Req("Element", "Deduction element code"), Today("Issued", "Issued on")],
            Run = async (c, i) => Check(await c.Get<IHrRecordsService>().RegisterGarnisheeAsync(c.CompanyId, await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true), i.Text("Reference"), i.Text("Creditor"),
                i.Whole("Priority"), i.Number("Instalment"), i.Number("Total"), i.Text("Element"), i.Date("Issued")).ConfigureAwait(true), "Garnishee registered."),
        };
        yield return new Operation
        {
            Module = module, Title = "Supplementary payroll run", ListTitle = "Payroll runs", Description = "Corrects a committed period with extra (or negative) element amounts - a visible run with its own payslips and journal.",
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
            Fields = [EmployeePick("Employee", "Employee"), Num("Basic", "New basic salary"), Today("From", "Effective from"), Tick("Pay", "Pay now"), Req("Period", "Pay in period"), Today("PayDate", "Pay date"),
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
            Fields = [EmployeePick("Employee", "Employee"), Req("Element", "Element code"), Req("Period", "Period", DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture)), OptNum("Units", "Units"),
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
            Module = module, Title = "Commit approved payroll", ListTitle = "Payroll drafts", Description = "Saves the payslips and posts the journal - refused if anything changed since approval.",
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
            Description = "Raises a run's AP invoices for the tax authority, schemes and garnishee creditors that have a supplier - clearing the payroll liabilities into payables.",
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
            Module = module, Title = "Split net pay", ListTitle = "Pay splits",
            Description = "Sends part of an employee's net pay to another bank account or wallet - a fixed amount, or a percentage (e.g. 40% to mobile money); the rest goes to their main account. "
                + "Remove takes a split off.",
            Fields = [EmployeePick("Employee", "Employee"), Pick("Method", "Paid by", Enum.GetNames<PayMethod>()), Req("Destination", "Account or wallet number"), OptNum("Amount", "Fixed amount"),
                OptNum("Percent", "Percent of net pay"), Opt("Bank", "Bank of that account (blank: the employee's bank)"), Opt("Branch", "Branch of that account"), Tick("Remove", "Remove this split")],
            Run = async (c, i) =>
            {
                long id = await EmployeeAsync(c, i.Text("Employee")).ConfigureAwait(true);
                PayMethod method = Enum.Parse<PayMethod>(i.Text("Method"));
                string destination = i.Text("Destination").Trim();
                List<EmployeePaymentSplit> existing = await c.Db.PaymentSplits.Where(s => s.EmployeeId == id).ToListAsync().ConfigureAwait(true);
                EmployeePaymentSplit? same = existing.FirstOrDefault(s => s.Method == method && s.Destination == destination);
                if (same is not null)
                {
                    c.Db.PaymentSplits.Remove(same);
                }

                if (i.Flag("Remove"))
                {
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return same is null ? "No such split." : "Split removed.";
                }

                if (EmployeePaymentSplit.Problem(i.OptionalNumber("Amount"), i.OptionalNumber("Percent"), existing.Where(s => s != same).Sum(s => s.Percent ?? 0m)) is string problem)
                {
                    throw new OperationException(problem);
                }

                c.Db.PaymentSplits.Add(new EmployeePaymentSplit(c.CompanyId, id, method, destination, i.OptionalNumber("Amount") ?? 0m, i.OptionalNumber("Percent"))
                {
                    BankName = i.Optional("Bank")?.Trim(), BankBranch = i.Optional("Branch")?.Trim(),
                });
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return "Split saved.";
            },
            List = async c => Table(await (from s in c.Db.PaymentSplits.AsNoTracking()
                                           join e in c.Db.Employees.AsNoTracking() on s.EmployeeId equals e.Id
                                           where s.CompanyId == c.CompanyId
                                           orderby e.EmployeeNumber
                                           select new { e.EmployeeNumber, e.FullName, s.Method, s.Destination, s.Amount, s.Percent }).ToListAsync().ConfigureAwait(true),
                ("Employee", s => s.EmployeeNumber), ("Name", s => s.FullName), ("Method", s => s.Method), ("To", s => s.Destination),
                ("Share", s => s.Percent is decimal p ? $"{p:0.##}%" : M(s.Amount))),
        };
        yield return new Operation
        {
            Module = module, Title = "Set an employee's pay group", Description = "Moves an employee to a pay group (MONTHLY, WEEKLY, CASUAL…); blank returns them to the default payroll.",
            Fields = [EmployeePick("Employee", "Employee"), Opt("Group", "Pay group")],
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
            Module = module, Title = "Set the tax year", ListTitle = "Deductions and their suspension priority",
            Description = "The month the tax year starts in (1 = January; 3 for a March–February year). Year-to-date taxable pay, cumulative tax and the YTD openings follow it.",
            Fields = [Num("Month", "First month of the tax year (1–12)", "1")],
            Run = async (c, i) =>
            {
                int month = i.Whole("Month");
                if (month is < 1 or > 12)
                {
                    throw new OperationException("The month is 1 to 12.");
                }

                Company company = await c.Db.Companies.FirstAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                company.TaxYearStartMonth = month;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"The tax year starts on 1 {new DateOnly(2000, month, 1):MMMM}.";
            },
            List = async c => Table(await c.Db.PayElements.AsNoTracking().Where(e => e.CompanyId == c.CompanyId && e.Type == PayElementType.Deduction).OrderBy(e => e.Sequence).ToListAsync().ConfigureAwait(true),
                ("Deduction", e => e.Code), ("Name", e => e.Name)),
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
            Run = async (c, i) => $"{Check(await c.Get<IPayrollAdjustmentService>().ImportLegacyPayrollAsync(c.CompanyId, i.Text("Period"), i.Text("Lines").Replace(';', '\n')).ConfigureAwait(true))} line(s) imported - run report PR8.",
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
            Module = module, Title = "Cash-flow forecast", ListTitle = "Bank accounts",
            Description = "Week by week from the bank balances: receivables and payables by due date, saved payment runs on their dates, open sales and purchase orders on "
                + "delivery plus terms, standing orders, payroll (repeating the last regular run) and flows keyed in by hand (once or monthly). Weeks where cash would fall "
                + "below the minimum are marked.",
            Fields = [Pick("Step", "Step", ["Forecast", "Add a flow", "Remove a flow"]), Today("From", "From"), Num("Weeks", "Weeks", "13"), Num("Minimum", "Minimum cash to hold", "0"),
                Tick("Detail", "Show every flow"), Num("Amount", "Flow amount (+ in, − out)", "0"), Opt("What", "Flow description"), Opt("Until", "Repeat monthly until (yyyy-mm-dd)"),
                OptNum("Entry", "Flow id (remove)")],
            Run = async (c, i) =>
            {
                Application.Abstractions.Banking.ICashForecastService forecast = c.Get<Application.Abstractions.Banking.ICashForecastService>();
                switch (i.Text("Step"))
                {
                    case "Add a flow":
                        return Check(await forecast.AddEntryAsync(c.CompanyId, i.Date("From"), i.Number("Amount"), i.Optional("What") ?? string.Empty,
                            i.Optional("Until") is string until ? DateOnly.Parse(until, System.Globalization.CultureInfo.InvariantCulture) : null).ConfigureAwait(true), id => $"Flow {id} added to the forecast.");
                    case "Remove a flow":
                        return Check(await forecast.RemoveEntryAsync(c.CompanyId, (long)(i.OptionalNumber("Entry") ?? 0m)).ConfigureAwait(true), "Flow removed.");
                }

                Application.Banking.CashForecast f = await c.Get<Application.Abstractions.Banking.ICashForecastService>().ForecastAsync(c.CompanyId, i.Date("From"), i.Whole("Weeks"), i.Number("Minimum"))
                    .ConfigureAwait(true);
                List<string> text =
                [
                    $"Opening {M(f.OpeningBalance)}; lowest {M(f.LowestClosing)}" + (f.FirstShortfall is DateOnly shortfall ? $"; below {M(f.Minimum)} from the week of {D(shortfall)}." : "."),
                    .. f.Weeks.Select(w => $"{D(w.WeekStart)}  in {M(w.Inflows),14}  out {M(w.Outflows),14}  closing {M(w.Closing),14}{(w.BelowMinimum ? "  ← below minimum" : string.Empty)}"),
                ];
                if (i.Flag("Detail"))
                {
                    text.AddRange(f.Lines.Select(l => $"  {D(l.Date)}  {l.Source,-15} {M(l.Amount),14}  {l.Reference}"));
                }

                return string.Join(Environment.NewLine, text);
            },
            List = async c => Table(await c.Db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == c.CompanyId).OrderBy(b => b.Code).ToListAsync().ConfigureAwait(true),
                ("Bank", b => b.Code), ("Name", b => b.Name), ("Currency", b => b.Currency), ("Ledger account", b => b.GlAccountCode)),
        };
        yield return new Operation
        {
            Module = module, Title = "Bank statement import and sign-off", ListTitle = "Bank accounts",
            Description = "Import the bank's statement file (CSV, MT940, CAMT.053 or OFX) into the account's unreconciled lines, or sign the reconciliation off as at a date: "
                + "it must balance, and once signed the period's matched items are locked against change.",
            Fields = [Pick("Step", "Step", ["Import", "Sign off"]), Req("Bank", "Bank account code"), Opt("File", "Statement file (full path)"),
                Pick("Format", "File format", Enum.GetNames<Application.Banking.StatementFileFormat>()), Today("AsAt", "Sign off as at")],
            Run = async (c, i) =>
            {
                Application.Abstractions.Banking.IBankReconciliationService bank = c.Get<Application.Abstractions.Banking.IBankReconciliationService>();
                long account = await BankAsync(c, i.Text("Bank")).ConfigureAwait(true);
                if (i.Text("Step") == "Import")
                {
                    string path = i.Optional("File") ?? throw new OperationException("Give the statement file.");
                    if (!System.IO.File.Exists(path))
                    {
                        throw new OperationException($"There is no file {path}.");
                    }

                    int imported = Check(await bank.ImportStatementFileAsync(c.CompanyId, account, await System.IO.File.ReadAllTextAsync(path).ConfigureAwait(true),
                        Enum.Parse<Application.Banking.StatementFileFormat>(i.Text("Format")), new Application.Banking.BankCsvLayout()).ConfigureAwait(true));
                    return $"{imported} statement line(s) imported (lines already imported are skipped).";
                }

                Application.Abstractions.Banking.BankReconciliationReport report = Check(await bank.SignOffAsync(c.CompanyId, account, i.Date("AsAt"), c.UserId).ConfigureAwait(true));
                return $"Reconciliation of {report.BankAccountCode} signed off as at {D(report.AsAtDate)}: ledger {M(report.GlBalance)}, statement {M(report.StatementBalance)}, "
                    + $"{report.UnpresentedGlItems.Count} unpresented and {report.UnmatchedStatementItems.Count} unmatched item(s).";
            },
            List = async c => Table(await c.Db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == c.CompanyId).OrderBy(b => b.Code).ToListAsync().ConfigureAwait(true),
                ("Bank", b => b.Code), ("Name", b => b.Name), ("Currency", b => b.Currency), ("Ledger account", b => b.GlAccountCode)),
        };
        yield return new Operation
        {
            Module = module, Title = "Set up a standing order", ListTitle = "Standing orders and direct debits",
            Description = "A fixed payment out of the bank on a schedule: to a supplier (purchase ledger) or to a GL account. Give a supplier or an account, not both.",
            Fields = [Req("Reference", "Reference"), Req("Bank", "Bank account", "1100"), Num("Amount", "Amount"), Pick("Frequency", "Frequency", Enum.GetNames<RecurrenceFrequency>()),
                Today("Start", "First payment"), Opt("End", "Last date (yyyy-MM-dd, optional)"), SupplierPick("Supplier", "Supplier", required: false), Opt("Account", "GL account"), Opt("Narrative", "What it is for")],
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
            Fields = [Req("Reference", "Mandate reference"), CustomerPick("Customer", "Customer"), Req("CustomerBank", "Customer's bank account"), Req("Bank", "Collect into bank account", "1100"),
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
            Module = module, Title = "Void a cheque leaf", ListTitle = "Cheque register (latest 100)", Description = "A spoilt or lost leaf that was never issued - kept in the register so the number sequence has no gaps.",
            Fields = [Req("Bank", "Bank account code"), Req("Number", "Cheque number"), Today(), Req("Reason", "Reason")],
            Run = async (c, i) => Check(await c.Get<IChequeService>().VoidLeafAsync(c.CompanyId, await BankAsync(c, i.Text("Bank")).ConfigureAwait(true), i.Text("Number"), i.Date("Date"), i.Text("Reason"))
                .ConfigureAwait(true), $"Leaf {i.Text("Number")} voided."),
            List = ChequesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Print a cheque", ListTitle = "Cheque register (latest 100)",
            Description = "Lays a cheque out for its leaf: date, payee, the amount in figures and in words, and the payment it settles (voided or cancelled cheques are not printed).",
            Fields = [Req("Bank", "Bank account code"), Req("Number", "Cheque number")],
            Run = async (c, i) => Check(await c.Get<Application.Abstractions.Documents.IDocumentLayoutService>().ChequeAsync(c.CompanyId, i.Text("Bank"), i.Text("Number")).ConfigureAwait(true)),
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
            Description = "A bank's EFT format as templates: {Field[,width][:format]} - header/trailer: Date, Originator, Currency, Count, Total, Hash; detail: Seq, Date, Reference, Name, Bank, Branch, Account, Currency, Amount (format 'cents' for whole cents; width 010 zero-pads).",
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
            Fields = [SupplierPick("Supplier", "Supplier"), Today(), Num("Closing", "Closing balance"), Req("Lines", "Lines: DOC:amount:yyyy-MM-dd; …")],
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
            Module = "Sales & Schools", Title = "Fee structures and term billing", ListTitle = "Fee structures",
            Description = "A fee structure per grade and term with its items (compulsory ones billed to every student in the grade, optional ones only to those taking them); "
                + "Bill term invoices every enrolled student of the grade under the prefix (a rerun skips those already billed); Bill student invoices one.",
            Fields = [Pick("Step", "Step", ["Save structure", "Add item", "Bill term", "Bill student"]), Req("Structure", "Fee structure code"), Opt("Name", "Name / item description"),
                Opt("Grade", "Grade"), Opt("Term", "Term (e.g. 2026-T1)"), Opt("Revenue", "Revenue account"), Num("Amount", "Item amount", "0"), Tick("Compulsory", "Compulsory item", true),
                Opt("Prefix", "Invoice prefix (bill term) / number (bill student)"), Opt("Student", "Admission number (bill student)"), Today()],
            Run = async (c, i) =>
            {
                ISchoolSetupService setup = c.Get<ISchoolSetupService>();
                ISchoolBillingService billing = c.Get<ISchoolBillingService>();
                async Task<long> StructureAsync() => await FindAsync(c.Db.FeeStructures, f => f.CompanyId == c.CompanyId && f.Code == i.Text("Structure").ToUpper(), f => f.Id, "fee structure",
                    i.Text("Structure")).ConfigureAwait(true);
                switch (i.Text("Step"))
                {
                    case "Add item":
                        return $"Item {Check(await setup.AddFeeItemAsync(c.CompanyId, i.Text("Structure"), i.Optional("Name") ?? string.Empty, i.Number("Amount"), i.Flag("Compulsory"), i.Optional("Revenue"))
                            .ConfigureAwait(true))} added.";
                    case "Bill term":
                        TermBillResult term = Check(await billing.BillTermAsync(c.CompanyId, await StructureAsync().ConfigureAwait(true), i.Optional("Prefix") ?? string.Empty, i.Date("Date")).ConfigureAwait(true));
                        return $"{term.Bills.Count} student(s) billed {M(term.TotalNetBilled)}; {term.AlreadyBilled} already billed.";
                    case "Bill student":
                        long student = await FindAsync(c.Db.Students, s => s.CompanyId == c.CompanyId && s.AdmissionNumber == (i.Optional("Student") ?? string.Empty).ToUpper(), s => s.Id, "student",
                            i.Optional("Student") ?? string.Empty).ConfigureAwait(true);
                        StudentBillResult bill = Check(await billing.BillStudentAsync(c.CompanyId, student, await StructureAsync().ConfigureAwait(true), i.Optional("Prefix") ?? string.Empty, i.Date("Date"))
                            .ConfigureAwait(true));
                        return $"Billed {M(bill.NetFees)} (gross {M(bill.GrossFees)}, discounts {M(bill.Discount + bill.SiblingDiscount)}).";
                    default:
                        return $"Fee structure saved (id {Check(await setup.SaveFeeStructureAsync(c.CompanyId, i.Text("Structure"), i.Optional("Name") ?? i.Text("Structure"), i.Optional("Grade") ?? string.Empty,
                            i.Optional("Term") ?? string.Empty, i.Optional("Revenue") ?? string.Empty).ConfigureAwait(true))}).";
                }
            },
            List = async c => Table(await c.Db.FeeStructures.AsNoTracking().Include(f => f.Items).Where(f => f.CompanyId == c.CompanyId).OrderBy(f => f.Code).ToListAsync().ConfigureAwait(true),
                ("Structure", f => f.Code), ("Name", f => f.Name), ("Grade", f => f.Grade), ("Term", f => f.Term), ("Compulsory", f => M(f.CompulsoryTotal)), ("Items", f => f.Items.Count)),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Students, hostels and routes", ListTitle = "Students",
            Description = "Admit a student under their guardian's account (the customer billed); set up a hostel or transport route with its capacity and fee per term; place a "
                + "student in a hostel and on a route (blank takes them out) within capacity.",
            Fields = [Pick("Step", "Step", ["Admit", "Hostel", "Route", "Place", "Sponsor"]), Opt("Student", "Admission number"), Opt("Name", "Name"), Opt("Grade", "Grade"), Opt("Guardian", "Guardian (customer code)"),
                Opt("Sponsor", "Sponsor (customer code billed instead of the guardian; blank: none)"),
                Pick("Gender", "Gender", Enum.GetNames<Core.Payroll.Gender>()), Opt("Code", "Hostel / route code"), Num("Capacity", "Capacity", "0"), Num("Fee", "Fee per term", "0"), Opt("Revenue", "Revenue account"), Opt("HostelCode", "Hostel (place)"),
                Opt("RouteCode", "Route (place)"), Today()],
            Run = async (c, i) =>
            {
                ISchoolSetupService setup = c.Get<ISchoolSetupService>();
                return i.Text("Step") switch
                {
                    "Hostel" => $"Hostel saved (id {Check(await setup.SaveHostelAsync(c.CompanyId, i.Optional("Code") ?? string.Empty, i.Optional("Name") ?? string.Empty, i.Whole("Capacity"), i.Number("Fee"),
                        i.Optional("Revenue") ?? string.Empty).ConfigureAwait(true))}).",
                    "Route" => $"Route saved (id {Check(await setup.SaveRouteAsync(c.CompanyId, i.Optional("Code") ?? string.Empty, i.Optional("Name") ?? string.Empty, i.Whole("Capacity"), i.Number("Fee"),
                        i.Optional("Revenue") ?? string.Empty).ConfigureAwait(true))}).",
                    "Place" => Check(await setup.PlaceStudentAsync(c.CompanyId, i.Optional("Student") ?? string.Empty, i.Optional("HostelCode"), i.Optional("RouteCode")).ConfigureAwait(true), "Student placed."),
                    "Sponsor" => Check(await setup.SetSponsorAsync(c.CompanyId, i.Optional("Student") ?? string.Empty, i.Optional("Sponsor")).ConfigureAwait(true),
                        i.Optional("Sponsor") is null ? "Sponsor removed; the guardian is billed." : $"Sponsor {i.Optional("Sponsor")!.ToUpperInvariant()} is billed for the student."),
                    _ => $"Student admitted (id {Check(await setup.AdmitStudentAsync(c.CompanyId, i.Optional("Student") ?? string.Empty, i.Optional("Name") ?? string.Empty, i.Optional("Grade") ?? string.Empty,
                        i.Optional("Guardian") ?? string.Empty, i.Date("Date"), Enum.TryParse(i.Optional("Gender"), out Core.Payroll.Gender gender) ? gender : Core.Payroll.Gender.Unspecified).ConfigureAwait(true))}).",
                };
            },
            List = async c => Table(await c.Db.Students.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).OrderBy(s => s.AdmissionNumber).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Admission", s => s.AdmissionNumber), ("Name", s => s.ShownName(false)), ("Grade", s => s.Grade), ("Gender", s => s.Gender == Core.Payroll.Gender.Unspecified ? string.Empty : s.Gender.ToString()),
                ("Boarder", s => s.IsBoarder ? "yes" : string.Empty), ("Enrolled", s => s.IsEnrolled ? "yes" : "left")),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Term dates, optional fees and leavers", ListTitle = "Optional fee items",
            Description = "Term dates (with pro rata, a joiner pays from the admission date and a leaver is credited the unused part of the term invoice); a student taking or "
                + "dropping an optional item (lunches, clubs, trips - billed with the term, not discounted); a student's admission date; withdrawing a student.",
            Fields = [Pick("Step", "Step", ["Term dates", "Take option", "Drop option", "Admission date", "Withdraw"]), Opt("Structure", "Fee structure code"),
                Opt("Student", "Admission number"), OptNum("Item", "Optional fee item id (from the list)"), Opt("Start", "Term start / admission date (yyyy-MM-dd)"),
                Opt("End", "Term end (yyyy-MM-dd)"), Tick("ProRata", "Pro rata joiners and leavers", true), Opt("Prefix", "Term invoice prefix (withdraw)"), Today("Date", "Left on (withdraw)")],
            Run = async (c, i) =>
            {
                ISchoolBillingService billing = c.Get<ISchoolBillingService>();
                async Task<long> StructureAsync() => await FindAsync(c.Db.Set<Core.Schools.FeeStructure>(), f => f.CompanyId == c.CompanyId && f.Code == (i.Optional("Structure") ?? string.Empty).ToUpper(),
                    f => f.Id, "fee structure", i.Optional("Structure") ?? string.Empty).ConfigureAwait(true);
                async Task<long> StudentAsync() => await FindAsync(c.Db.Set<Core.Schools.Student>(), s => s.CompanyId == c.CompanyId && s.AdmissionNumber == (i.Optional("Student") ?? string.Empty).ToUpper(),
                    s => s.Id, "student", i.Optional("Student") ?? string.Empty).ConfigureAwait(true);
                switch (i.Text("Step"))
                {
                    case "Term dates":
                        return Check(await billing.SetTermAsync(c.CompanyId, await StructureAsync().ConfigureAwait(true), Day(i.Optional("Start") ?? string.Empty), Day(i.Optional("End") ?? string.Empty),
                            i.Flag("ProRata")).ConfigureAwait(true), "Term dates saved.");
                    case "Take option":
                    case "Drop option":
                        return Check(await billing.SetOptionAsync(c.CompanyId, await StudentAsync().ConfigureAwait(true), (long)i.Number("Item"), i.Text("Step") == "Take option").ConfigureAwait(true),
                            "Option saved.");
                    case "Admission date":
                        long id = await StudentAsync().ConfigureAwait(true);
                        Core.Schools.Student student = await c.Db.Set<Core.Schools.Student>().SingleAsync(s => s.Id == id).ConfigureAwait(true);
                        student.AdmittedOn = Day(i.Optional("Start") ?? string.Empty);
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return "Admission date saved.";
                    default:
                        WithdrawalResult left = Check(await billing.WithdrawAsync(c.CompanyId, await StudentAsync().ConfigureAwait(true), i.Date("Date"), await StructureAsync().ConfigureAwait(true),
                            i.Optional("Prefix") ?? string.Empty).ConfigureAwait(true));
                        return left.CreditNoteNumber is null ? "Withdrawn; nothing to credit." : $"Withdrawn; credit note {left.CreditNoteNumber} for {M(left.Credited)}.";
                }
            },
            List = async c => Table(await (from item in c.Db.Set<Core.Schools.FeeItem>().AsNoTracking()
                                           join structure in c.Db.Set<Core.Schools.FeeStructure>().AsNoTracking() on item.FeeStructureId equals structure.Id
                                           where structure.CompanyId == c.CompanyId && !item.IsCompulsory
                                           orderby structure.Code, item.Id
                                           select new { item.Id, structure.Code, item.Description, item.Amount, item.RevenueAccountCode }).ToListAsync().ConfigureAwait(true),
                ("Item", x => x.Id), ("Structure", x => x.Code), ("Description", x => x.Description), ("Amount", x => M(x.Amount)), ("Revenue", x => x.RevenueAccountCode)),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Bursaries", ListTitle = "Bursary requests",
            Description = "Request a bursary (percentage or value off compulsory fees) - it reduces fees only once approved by someone else with the bursary approval right, "
                + "or through the approval workflow when one is defined for bursaries. Decide approves or rejects a pending request by its reference. A bursary may expire: fees billed "
                + "after its last day get no bursary. Expiring lists bursaries ending within the given days, for renewal.",
            Fields = [Pick("Step", "Step", ["Request", "Approve", "Reject", "Expiring"]), Opt("Student", "Admission number (request)"), Pick("Type", "Type", ["Percentage", "Value"]),
                OptNum("Value", "Percentage or value (request)"), Opt("Reason", "Reason / decision note"), Opt("Reference", "Reference (decide)"), Opt("ValidUntil", "Last day it applies (yyyy-MM-dd)"),
                Num("Within", "Expiring within (days)", "60"), Today()],
            Run = async (c, i) =>
            {
                ISchoolBursaryService bursaries = c.Get<ISchoolBursaryService>();
                if (i.Text("Step") == "Request")
                {
                    long student = await FindAsync(c.Db.Students, s => s.CompanyId == c.CompanyId && s.AdmissionNumber == (i.Optional("Student") ?? string.Empty).ToUpper(), s => s.Id, "student",
                        i.Optional("Student") ?? string.Empty).ConfigureAwait(true);
                    DateOnly? validUntil = i.Optional("ValidUntil") is string last ? DateOnly.ParseExact(last, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
                    BursaryRequestResult asked = Check(await bursaries.RequestAsync(c.CompanyId, student, Enum.Parse<Core.Schools.BursaryType>(i.Text("Type")), i.OptionalNumber("Value") ?? 0m,
                        i.Optional("Reason") ?? string.Empty, c.UserId, i.Date("Date"), validUntil).ConfigureAwait(true));
                    return asked.InWorkflow ? $"{asked.Reference} is in its approval workflow." : $"{asked.Reference} awaits approval.";
                }

                if (i.Text("Step") == "Expiring")
                {
                    IReadOnlyList<ExpiringBursary> expiring = await bursaries.ExpiringAsync(c.CompanyId, i.Date("Date"), (int)i.Number("Within")).ConfigureAwait(true);
                    return expiring.Count == 0 ? "No bursaries expire in that time." : string.Join(Environment.NewLine, expiring.Select(b =>
                        $"{D(b.ValidUntil)}  {b.AdmissionNumber,-12} {b.StudentName,-30} {b.Type} {Q(b.Value)}"));
                }

                return Check(await bursaries.DecideAsync(c.CompanyId, i.Text("Reference"), i.Text("Step") == "Approve", c.UserId, i.Date("Date"), i.Optional("Reason")).ConfigureAwait(true),
                    $"Bursary {i.Text("Reference").ToUpperInvariant()} {(i.Text("Step") == "Approve" ? "approved and applied" : "rejected")}.");
            },
            List = async c => Table(await (from a in c.Db.BursaryAwards.AsNoTracking()
                                           join s in c.Db.Students.AsNoTracking() on a.StudentId equals s.Id
                                           where a.CompanyId == c.CompanyId
                                           orderby a.Id descending
                                           select new { a.Reference, s.AdmissionNumber, a.Type, a.Value, a.Reason, a.Status, a.RequestedOn }).Page(c, 100).ToListAsync().ConfigureAwait(true),
                ("Reference", a => a.Reference), ("Student", a => a.AdmissionNumber), ("Type", a => a.Type), ("Value", a => Q(a.Value)), ("Reason", a => a.Reason), ("Status", a => a.Status),
                ("Requested", a => D(a.RequestedOn))),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Staff children and restricted students",
            Description = "Staff discount: the percentage off compulsory fees for staff children while the parent is employed (the better of it and the sibling discount). "
                + "Staff parent links a student to the employee (blank removes). Do not disclose withholds the student's name from reports for anyone without the restricted-records permission.",
            Fields = [Pick("Step", "Step", ["Staff discount", "Staff parent", "Do not disclose", "Disclose"]), OptNum("Percent", "Staff discount %"), Opt("Student", "Admission number"),
                Opt("Employee", "Employee number (staff parent)"), Opt("Note", "Why withheld")],
            Run = async (c, i) =>
            {
                ISchoolBursaryService service = c.Get<ISchoolBursaryService>();
                if (i.Text("Step") == "Staff discount")
                {
                    return Check(await service.SetStaffChildDiscountAsync(c.CompanyId, i.OptionalNumber("Percent") ?? 0m).ConfigureAwait(true), "Staff-child discount saved.");
                }

                long student = await FindAsync(c.Db.Students, s => s.CompanyId == c.CompanyId && s.AdmissionNumber == (i.Optional("Student") ?? string.Empty).ToUpper(), s => s.Id, "student",
                    i.Optional("Student") ?? string.Empty).ConfigureAwait(true);
                if (i.Text("Step") == "Staff parent")
                {
                    long? employee = i.Optional("Employee") is string number ? await EmployeeAsync(c, number).ConfigureAwait(true) : null;
                    return Check(await service.SetStaffParentAsync(c.CompanyId, student, employee).ConfigureAwait(true), employee is null ? "Staff parent removed." : "Staff parent linked.");
                }

                bool withhold = i.Text("Step") == "Do not disclose";
                return Check(await service.SetDisclosureAsync(c.CompanyId, student, withhold, i.Optional("Note")).ConfigureAwait(true), withhold ? "Student's details withheld." : "Student's details disclosed.");
            },
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Academic years and terms", ListTitle = "Terms",
            Description = "Years and their terms (no overlaps). A term's code is what fee structures name as their term: saving it gives them its dates for pro-rata billing and deferred income.",
            Fields = [Pick("Step", "Step", ["Save year", "Save term", "Which term is it?"]), Req("Year", "Year code"), Opt("Term", "Term code (e.g. 2026-T1)"), Opt("Name", "Term name"),
                Num("Sequence", "Term number", "1"), Today("From", "First day"), Today("To", "Last day")],
            Run = async (c, i) =>
            {
                IAcademicStructureService academic = c.Get<IAcademicStructureService>();
                return i.Text("Step") switch
                {
                    "Save year" => Check(await academic.SaveYearAsync(c.CompanyId, i.Text("Year"), i.Date("From"), i.Date("To")).ConfigureAwait(true), "Year saved."),
                    "Save term" => Check(await academic.SaveTermAsync(c.CompanyId, i.Text("Year"), i.Optional("Term") ?? string.Empty, i.Optional("Name") ?? string.Empty, i.Whole("Sequence"),
                        i.Date("From"), i.Date("To")).ConfigureAwait(true), n => $"Term saved; {n} fee structure(s) took its dates."),
                    _ => Check(await academic.TermOnAsync(c.CompanyId, i.Date("From")).ConfigureAwait(true), t => $"{i.Date("From"):yyyy-MM-dd} is in {t.Name} ({t.Term}) of {t.Year}: {D(t.StartDate)} to {D(t.EndDate)}."),
                };
            },
            List = async c => Table(await (from t in c.Db.SchoolTerms.AsNoTracking()
                                           join y in c.Db.AcademicYears.AsNoTracking() on t.AcademicYearId equals y.Id
                                           where t.CompanyId == c.CompanyId
                                           orderby t.StartDate
                                           select new { Year = y.Code, t.Code, t.Name, t.StartDate, t.EndDate }).ToListAsync().ConfigureAwait(true),
                ("Year", t => t.Year), ("Term", t => t.Code), ("Name", t => t.Name), ("From", t => D(t.StartDate)), ("To", t => D(t.EndDate))),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Classes and streams", ListTitle = "Class rolls",
            Description = "Classes by grade and stream with capacity and the grade they move up to (blank: final year). Place a student within capacity; at year end, promote the school once.",
            Fields = [Pick("Step", "Step", ["Save class", "Place a student", "Promote the year"]), Opt("Grade", "Grade / form"), Opt("Stream", "Stream"), Opt("Name", "Class name"),
                OptNum("Capacity", "Capacity"), Opt("Next", "Next grade"), Opt("Student", "Admission number"), Opt("Year", "Year ending (promote)")],
            Run = async (c, i) =>
            {
                IAcademicStructureService academic = c.Get<IAcademicStructureService>();
                return i.Text("Step") switch
                {
                    "Save class" => Check(await academic.SaveClassAsync(c.CompanyId, i.Optional("Grade") ?? string.Empty, i.Optional("Stream"), i.Optional("Name") ?? string.Empty,
                        i.OptionalNumber("Capacity") is decimal cap ? (int)cap : null, i.Optional("Next")).ConfigureAwait(true), "Class saved."),
                    "Place a student" => Check(await academic.PlaceAsync(c.CompanyId, i.Optional("Student") ?? string.Empty, i.Optional("Grade") ?? string.Empty, i.Optional("Stream")).ConfigureAwait(true),
                        "Student placed."),
                    _ => Check(await academic.PromoteAsync(c.CompanyId, i.Optional("Year") ?? string.Empty).ConfigureAwait(true),
                        r => $"{r.Promoted} promoted, {r.Completed} in final-year classes" + (r.Unplaced.Count == 0 ? "." : $"; no class for: {string.Join(", ", r.Unplaced)}.")),
                };
            },
            List = async c => Table(await c.Get<IAcademicStructureService>().ClassRollsAsync(c.CompanyId).ConfigureAwait(true),
                ("Grade", r => r.Grade), ("Stream", r => r.Stream ?? "-"), ("Class", r => r.Name), ("Enrolled", r => r.Enrolled), ("Capacity", r => r.Capacity?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "-"),
                ("Moves to", r => r.NextGrade ?? "leaves")),
        };
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
            Module = "Sales & Schools", Title = "Send fee reminders",
            Description = "Emails every guardian or sponsor paying for an enrolled student who owes at least the minimum their statement; those without an email address are "
                + "counted for a printed letter.",
            Fields = [Today("Date", "As at"), Req("Template", "Email template", "FEES"), Num("Minimum", "Smallest balance", "1")],
            Run = async (c, i) =>
            {
                var sent = Check(await c.Get<ISchoolBillingService>().SendFeeRemindersAsync(c.CompanyId, i.Date("Date"), i.Text("Template"), Localization.UiText.Catalog.Culture.TwoLetterISOLanguageName,
                    i.Number("Minimum")).ConfigureAwait(true));
                return $"{sent.Queued} reminder(s) queued; {sent.NoEmail} to print (no email address); {sent.BelowMinimum} below the minimum.";
            },
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
            Module = "Sales & Schools", Title = "Sales discount limit", ListTitle = "Orders waiting for discount approval",
            Description = "The largest discount off the price list a sales order may carry without authority. Beyond it the order needs an authoriser who may override "
                + "discounts (Sales.Invoice), or the company's SalesDiscount workflow approves it (the order cannot be despatched until then). Blank: no limit.",
            Fields = [OptNum("Limit", "Largest discount % without authority (blank: no limit)")],
            Run = async (c, i) =>
            {
                Core.System.Company company = await c.Db.Companies.FirstAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                company.MaxSalesDiscountPercent = i.OptionalNumber("Limit") is decimal limit && limit >= 0m ? limit : null;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return company.MaxSalesDiscountPercent is decimal set ? $"Sales orders may be discounted up to {set:0.##}% without authority." : "Sales discounts are not limited.";
            },
            List = async c => Table(await c.Db.Set<Core.SalesOrders.SalesOrder>().AsNoTracking().Where(o => o.CompanyId == c.CompanyId && o.AwaitingDiscountApproval)
                    .OrderBy(o => o.OrderDate).ToListAsync().ConfigureAwait(true),
                ("Order", o => o.OrderNumber), ("Date", o => D(o.OrderDate)), ("Customer", o => o.CustomerId)),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Sales order processing", ListTitle = "Open sales orders",
            Description = "Take an order (lines ITEM:quantity[:price[:tax code]]; no price takes the customer's price book), despatch it from a warehouse (stock issued at cost; "
                + "lines ITEM:quantity, or blank for everything outstanding - a part despatch leaves the rest on back order), invoice what has been despatched (tax at each "
                + "line's tax code), take back goods returned from a despatch, or cancel what is left of the order.",
            Fields = [Pick("Step", "Step", ["Create", "Despatch", "Invoice", "Return", "Cancel"]), Req("Order", "Order number"), Today(), Opt("Customer", "Customer code (create)"),
                Opt("Lines", "Lines (create: ITEM:qty[:price[:tax]]; despatch/return: ITEM:qty)"), Opt("Revenue", "Revenue account (create; blank: the item's)"),
                Opt("Warehouse", "Warehouse code (despatch, return)"), Opt("Reference", "Delivery / invoice number"), Opt("Vehicle", "Vehicle (despatch)"), Opt("Driver", "Driver (despatch)"),
                Opt("TaxAccount", "Output tax account (invoice)"), Opt("Reason", "Reason (return, cancel)")],
            Run = async (c, i) =>
            {
                ISalesOrderService orders = c.Get<ISalesOrderService>();
                string number = i.Text("Order").Trim().ToUpperInvariant();
                DateOnly date = i.Date("Date");
                if (i.Text("Step") == "Create")
                {
                    long customerId = await CustomerAsync(c, i.Optional("Customer") ?? throw new OperationException("Give the customer.")).ConfigureAwait(true);
                    Core.Trading.Customer customer = await c.Db.Customers.AsNoTracking().FirstAsync(x => x.Id == customerId).ConfigureAwait(true);
                    List<SalesOrderLineRequest> lines = [];
                    foreach (string part in (i.Optional("Lines") ?? throw new OperationException("Give the order lines.")).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                        if (bits.Length < 2 || !decimal.TryParse(bits[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal quantity))
                        {
                            throw new OperationException($"'{part}' is not ITEM:quantity[:price[:tax code]].");
                        }

                        long itemId = await ItemAsync(c, bits[0]).ConfigureAwait(true);
                        Core.Inventory.Item item = await c.Db.Items.AsNoTracking().FirstAsync(x => x.Id == itemId).ConfigureAwait(true);
                        decimal? price = bits.Length > 2 && bits[2].Length > 0 ? decimal.Parse(bits[2], System.Globalization.CultureInfo.InvariantCulture) : null;
                        string revenue = i.Optional("Revenue") ?? item.RevenueAccountCode ?? throw new OperationException($"Item {item.Code} has no revenue account; give one.");
                        lines.Add(new SalesOrderLineRequest(itemId, item.Description, quantity, price, bits.Length > 3 && bits[3].Length > 0 ? bits[3].ToUpperInvariant() : null, revenue));
                    }

                    SalesOrderResult created = Check(await orders.CreateOrderAsync(new CreateSalesOrderRequest
                    {
                        CompanyId = c.CompanyId, CustomerId = customerId, OrderNumber = number, OrderDate = date, Currency = customer.Currency, Lines = lines, RaisedByUserId = c.UserId,
                    }).ConfigureAwait(true));
                    return $"Order {created.OrderNumber} taken ({lines.Count} line(s)).";
                }

                Core.SalesOrders.SalesOrder order = await c.Db.Set<Core.SalesOrders.SalesOrder>().AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.CompanyId == c.CompanyId && o.OrderNumber == number)
                    .ConfigureAwait(true) ?? throw new OperationException($"No sales order {number}.");
                Dictionary<long, string> codes = await c.Db.Items.AsNoTracking().Where(x => order.Lines.Select(l => l.ItemId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code.ToUpper()).ConfigureAwait(true);
                List<(Core.SalesOrders.SalesOrderLine Line, decimal Quantity)> Given(Func<Core.SalesOrders.SalesOrderLine, decimal> outstanding)
                {
                    if (i.Optional("Lines") is not string text)
                    {
                        return [.. order.Lines.Select(l => (l, outstanding(l))).Where(x => x.Item2 > 0m)];
                    }

                    List<(Core.SalesOrders.SalesOrderLine, decimal)> picked = [];
                    foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                        Core.SalesOrders.SalesOrderLine line = order.Lines.FirstOrDefault(l => codes.GetValueOrDefault(l.ItemId) == bits[0].ToUpperInvariant() && outstanding(l) > 0m)
                            ?? order.Lines.FirstOrDefault(l => codes.GetValueOrDefault(l.ItemId) == bits[0].ToUpperInvariant())
                            ?? throw new OperationException($"Order {number} has no line for {bits[0]}.");
                        picked.Add((line, bits.Length > 1 ? decimal.Parse(bits[1], System.Globalization.CultureInfo.InvariantCulture) : outstanding(line)));
                    }

                    return picked;
                }

                switch (i.Text("Step"))
                {
                    case "Despatch":
                    {
                        long warehouse = await WarehouseAsync(c, i.Optional("Warehouse") ?? throw new OperationException("Give the warehouse to despatch from.")).ConfigureAwait(true);
                        List<(Core.SalesOrders.SalesOrderLine Line, decimal Quantity)> lines = Given(l => l.OrderedQuantity - l.DespatchedQuantity);
                        if (lines.Count == 0)
                        {
                            throw new OperationException($"Nothing on order {number} is left to despatch.");
                        }

                        DespatchResult despatched = Check(await orders.DespatchAsync(new DespatchRequest
                        {
                            CompanyId = c.CompanyId, SalesOrderId = order.Id, WarehouseId = warehouse, Date = date, Reference = i.Optional("Reference") ?? $"DN-{number}-{date:yyyyMMdd}",
                            Lines = [.. lines.Select(l => new DespatchLine(l.Line.Id, l.Quantity))], Vehicle = i.Optional("Vehicle"), Driver = i.Optional("Driver"),
                        }).ConfigureAwait(true));
                        decimal backOrder = order.Lines.Sum(l => l.OrderedQuantity - l.DespatchedQuantity) - lines.Sum(l => l.Quantity);
                        return $"Despatched {Q(lines.Sum(l => l.Quantity))} (cost of sales {M(despatched.CostOfSales)})." + (backOrder > 0m ? $" {Q(backOrder)} remain on back order." : string.Empty);
                    }

                    case "Invoice":
                    {
                        List<(Core.SalesOrders.SalesOrderLine Line, decimal Quantity)> lines = Given(l => l.DespatchedQuantity - l.InvoicedQuantity);
                        if (lines.Count == 0)
                        {
                            throw new OperationException($"Nothing despatched on order {number} is waiting to be invoiced.");
                        }

                        List<string> taxCodes = [.. lines.Select(l => l.Line.TaxCode).OfType<string>().Distinct()];
                        List<Core.Accounting.TaxCode> taxes = await c.Db.Set<Core.Accounting.TaxCode>().AsNoTracking().Include(t => t.Rates).Where(t => t.CompanyId == c.CompanyId && taxCodes.Contains(t.Code))
                            .ToListAsync().ConfigureAwait(true);
                        decimal Tax(Core.SalesOrders.SalesOrderLine line, decimal quantity) => line.TaxCode is string code && taxes.FirstOrDefault(t => t.Code == code) is { } tax
                            ? decimal.Round(quantity * line.UnitPrice * tax.RateOn(date) / 100m, 2, MidpointRounding.AwayFromZero) : 0m;
                        InvoiceOrderResult invoiced = Check(await orders.InvoiceAsync(new InvoiceOrderRequest
                        {
                            CompanyId = c.CompanyId, SalesOrderId = order.Id, InvoiceNumber = i.Optional("Reference") ?? $"INV-{number}-{date:yyyyMMdd}", Date = date, TaxAccountCode = i.Optional("TaxAccount"),
                            Lines = [.. lines.Select(l => new InvoiceOrderLine(l.Line.Id, l.Quantity, Tax(l.Line, l.Quantity)))],
                        }).ConfigureAwait(true));
                        return $"Invoiced {M(invoiced.InvoiceValue)} (journal {invoiced.JournalNumber}).";
                    }

                    case "Return":
                    {
                        long warehouse = await WarehouseAsync(c, i.Optional("Warehouse") ?? throw new OperationException("Give the warehouse the goods come back to.")).ConfigureAwait(true);
                        if (i.Optional("Lines") is null)
                        {
                            throw new OperationException("Give the returned lines as ITEM:quantity.");
                        }

                        decimal cost = 0m;
                        foreach ((Core.SalesOrders.SalesOrderLine line, decimal quantity) in Given(l => l.DespatchedQuantity))
                        {
                            cost += Check(await orders.ReturnDespatchAsync(c.CompanyId, order.Id, line.Id, warehouse, date, quantity, i.Optional("Reason") ?? "Returned").ConfigureAwait(true));
                        }

                        return $"Goods returned to stock ({M(cost)} back from cost of sales).";
                    }

                    default:
                        return Check(await orders.CancelOrderAsync(c.CompanyId, order.Id, i.Optional("Reason") ?? throw new OperationException("Give the reason for cancelling.")).ConfigureAwait(true),
                            $"Order {number} cancelled.");
                }
            },
            List = async c => Table(await (
                    from o in c.Db.Set<Core.SalesOrders.SalesOrder>().AsNoTracking()
                    join cu in c.Db.Customers.AsNoTracking() on o.CustomerId equals cu.Id
                    where o.CompanyId == c.CompanyId && o.Lines.Any(l => l.OrderedQuantity > l.InvoicedQuantity)
                    orderby o.OrderDate descending
                    select new
                    {
                        o.OrderNumber, o.OrderDate, Customer = cu.Code, o.Status, Ordered = o.Lines.Sum(l => l.OrderedQuantity), Despatched = o.Lines.Sum(l => l.DespatchedQuantity),
                        Invoiced = o.Lines.Sum(l => l.InvoicedQuantity), Value = o.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice),
                    }).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Order", r => r.OrderNumber), ("Date", r => D(r.OrderDate)), ("Customer", r => r.Customer), ("Status", r => r.Status.ToString()), ("Ordered", r => Q(r.Ordered)),
                ("Despatched", r => Q(r.Despatched)), ("Invoiced", r => Q(r.Invoiced)), ("Back order", r => Q(r.Ordered - r.Despatched)), ("Value", r => M(r.Value))),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Reserve stock for a sales order", ListTitle = "Stock reserved for orders",
            Description = "Holds free stock in a warehouse for an order's lines still to despatch, first come first served; no other issue (a till sale, another order) can take it "
                + "until the order despatches it. Release gives it back; cancelling the order releases it too.",
            Fields = [Pick("Step", "Step", ["Reserve", "Release"]), Req("Order", "Order number"), Opt("Warehouse", "Warehouse code (reserve)")],
            Run = async (c, i) =>
            {
                string number = i.Text("Order").Trim().ToUpperInvariant();
                long order = await c.Db.Set<Core.SalesOrders.SalesOrder>().AsNoTracking().Where(o => o.CompanyId == c.CompanyId && o.OrderNumber == number).Select(o => (long?)o.Id)
                    .FirstOrDefaultAsync().ConfigureAwait(true) ?? throw new OperationException($"No sales order {number}.");
                ISalesOrderService orders = c.Get<ISalesOrderService>();
                if (i.Text("Step") == "Release")
                {
                    return Check(await orders.ReleaseReservationsAsync(c.CompanyId, order).ConfigureAwait(true), "Reservations released.");
                }

                long warehouse = await WarehouseAsync(c, i.Optional("Warehouse") ?? throw new OperationException("Give the warehouse to reserve in.")).ConfigureAwait(true);
                StockReservationResult held = Check(await orders.ReserveAsync(c.CompanyId, order, warehouse).ConfigureAwait(true));
                return $"Reserved {Q(held.Reserved)}." + (held.Short > 0m ? $" {Q(held.Short)} could not be reserved (not free in that warehouse)." : string.Empty);
            },
            List = async c => Table(await (
                    from o in c.Db.Set<Core.SalesOrders.SalesOrder>().AsNoTracking()
                    from l in o.Lines
                    join item in c.Db.Items.AsNoTracking() on l.ItemId equals item.Id
                    join w in c.Db.Warehouses.AsNoTracking() on l.ReservedWarehouseId equals w.Id
                    where o.CompanyId == c.CompanyId && l.ReservedQuantity > 0m
                    orderby o.OrderNumber
                    select new { o.OrderNumber, Item = item.Code, Warehouse = w.Code, l.ReservedQuantity, Outstanding = l.OrderedQuantity - l.DespatchedQuantity }).ToListAsync().ConfigureAwait(true),
                ("Order", r => r.OrderNumber), ("Item", r => r.Item), ("Warehouse", r => r.Warehouse), ("Reserved", r => Q(r.ReservedQuantity)), ("Still to despatch", r => Q(r.Outstanding))),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Create a customer contract", Description = "Agreed quantities at agreed prices for a period, drawn down by call-off orders.",
            Fields = [CustomerPick("Customer", "Customer"), Req("Number", "Contract number"), Today("From", "From"), Today("To", "To"), Req("Lines", "Lines: ITEM:quantity:price; …")],
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
            Description = "The supplier sends sales-order items straight to the customer. Untick 'Confirm' to place it; tick it (with the supplier's invoice) once delivered - cost of sales is charged and the lines become invoiceable, no stock moves.",
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
            List = async c => Table(await c.Db.DropShipments.AsNoTracking().Include(d => d.Lines).Where(d => d.CompanyId == c.CompanyId).OrderByDescending(d => d.Id).Page(c, 50).ToListAsync().ConfigureAwait(true),
                ("Reference", d => d.Reference), ("Ordered", d => D(d.OrderedOn)), ("Delivered", d => d.DeliveredOn is DateOnly on ? D(on) : "pending"), ("Supplier invoice", d => d.SupplierInvoiceNumber),
                ("Cost", d => M(d.Cost))),
        };
        yield return new Operation
        {
            Module = "Sales & Schools", Title = "Amend a sales order", ListTitle = "Sales order amendments",
            Description = "Changes ordered quantities and prices as a new version of the order, keeping the earlier one with the reason. Quantities cannot go below what has been despatched or invoiced; invoiced prices cannot change.",
            Fields = [Req("Order", "Sales order number"), Req("Lines", "Lines: ITEM:quantity:unit price; …"), Req("Reason", "Reason"), Today()],
            Run = async (c, i) =>
            {
                var order = await c.Db.SalesOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.CompanyId == c.CompanyId && o.OrderNumber == i.Text("Order").ToUpper()).ConfigureAwait(true)
                    ?? throw new OperationException($"No sales order '{i.Text("Order")}'.");
                List<SalesOrderLineChange> lines = [];
                foreach (string part in i.Text("Lines").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                    if (bits.Length != 3 || !decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity)
                        || !decimal.TryParse(bits[2], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price))
                    {
                        throw new OperationException($"'{part}' should look like ITEM:quantity:unit price.");
                    }

                    long item = await ItemAsync(c, bits[0]).ConfigureAwait(true);
                    long line = order.Lines.FirstOrDefault(l => l.ItemId == item)?.Id ?? throw new OperationException($"{bits[0]} is not on order {order.OrderNumber}.");
                    lines.Add(new SalesOrderLineChange(line, quantity, price));
                }

                SalesOrderAmendmentResult done = Check(await c.Get<ISalesOrderAmendmentService>().AmendAsync(c.CompanyId, order.Id,
                    new SalesOrderAmendment(i.Date("Date"), c.UserId, i.Text("Reason"), lines)).ConfigureAwait(true));
                return $"Order {order.OrderNumber} is now version {done.Version}: {M(done.TotalBefore)} → {M(done.TotalAfter)}.";
            },
            List = async c => Table(await (from r in c.Db.SalesOrderRevisions.AsNoTracking()
                                           join o in c.Db.SalesOrders.AsNoTracking() on r.SalesOrderId equals o.Id
                                           where r.CompanyId == c.CompanyId
                                           orderby r.Id descending
                                           select new { o.OrderNumber, r.Version, r.TotalNet, r.Reason, r.AmendedOn }).Page(c, 50).ToListAsync().ConfigureAwait(true),
                ("Order", r => r.OrderNumber), ("Version replaced", r => r.Version.ToString(CultureInfo.InvariantCulture)), ("Value then", r => M(r.TotalNet)), ("Reason", r => r.Reason), ("On", r => D(r.AmendedOn))),
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
            Module = "Sales & Schools", Title = "Delivery vehicles and loads", ListTitle = "Delivery vehicles",
            Description = "Register a vehicle (capacity in kg, optional m³ and drops a day, usual driver); put a delivery on a vehicle's run for a day at a stop (refused when it would "
                + "overload the vehicle by weight or exceed its drops); or show a vehicle's load for a day in run order.",
            Fields = [Pick("Step", "Step", "Assign", "Load", "Register vehicle"), Req("Vehicle", "Vehicle registration"), Opt("Delivery", "Delivery reference (assign)"), Today("Date", "Day"),
                Opt("Route", "Route"), Opt("Driver", "Driver"), OptNum("Stop", "Stop number (assign; blank: last)"), Opt("Description", "Description (register)"),
                OptNum("CapacityKg", "Capacity kg (register)"), OptNum("CapacityM3", "Capacity m³ (register)"), OptNum("MaxStops", "Most drops a day (register)")],
            Run = async (c, i) =>
            {
                IDeliveryPlanningService planning = c.Get<IDeliveryPlanningService>();
                if (i.Text("Step") == "Register vehicle")
                {
                    return Check(await planning.SaveVehicleAsync(c.CompanyId, new DeliveryVehicleRequest(i.Text("Vehicle"), i.Optional("Description") ?? i.Text("Vehicle"),
                        i.OptionalNumber("CapacityKg") ?? 0m, i.OptionalNumber("CapacityM3"), i.OptionalNumber("MaxStops") is decimal stops ? (int)stops : null, i.Optional("Driver"))).ConfigureAwait(true),
                        _ => $"Vehicle {i.Text("Vehicle").ToUpperInvariant()} saved.");
                }

                VehicleLoad load = Check(i.Text("Step") == "Assign"
                    ? await planning.AssignAsync(c.CompanyId, new DeliveryAssignment(i.Optional("Delivery") ?? string.Empty, i.Text("Vehicle"), i.Date("Date"), i.Optional("Route"), i.Optional("Driver"),
                        i.OptionalNumber("Stop") is decimal stop ? (int)stop : null)).ConfigureAwait(true)
                    : await planning.LoadAsync(c.CompanyId, i.Text("Vehicle"), i.Date("Date")).ConfigureAwait(true));
                return $"{load.Vehicle} on {load.Date:yyyy-MM-dd}: {Q(load.WeightKg)} of {Q(load.CapacityKg)} kg ({load.Utilisation:0.#}%)" + Environment.NewLine
                    + string.Join(Environment.NewLine, load.Stops.Select(s => $"  {s.Stop}. {s.DeliveryReference} {s.Route ?? string.Empty} {Q(s.WeightKg)} kg"));
            },
            List = async c => Table(await c.Db.Set<Core.SalesOrders.DeliveryVehicle>().AsNoTracking().Where(v => v.CompanyId == c.CompanyId).OrderBy(v => v.Registration).ToListAsync().ConfigureAwait(true),
                ("Vehicle", v => v.Registration), ("Description", v => v.Description), ("Capacity kg", v => Q(v.CapacityKg)), ("Drops", v => v.MaxStops?.ToString(CultureInfo.InvariantCulture) ?? "-"),
                ("Driver", v => v.DefaultDriver ?? "-")),
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
        // A document prints as its standard text, or - when the company has named a layout for it under System Manager › Document layouts - through that
        // report, saved as a PDF at the path given (the layout runs with the document's number and date).
        async Task<string> PrintAsync(OperationContext c, OperationInput i, string kind, string number, DateOnly? date, Func<Task<One4All.Core.Common.Result<string>>> text)
        {
            string? json = await c.Db.Companies.Where(x => x.Id == c.CompanyId).Select(x => x.DocumentLayoutsJson).FirstOrDefaultAsync().ConfigureAwait(true);
            if (Application.Documents.DocumentLayoutSettings.Parse(json).LayoutFor(kind) is not string layout)
            {
                return Check(await text().ConfigureAwait(true));
            }

            string path = i.Optional("SaveTo") ?? throw new OperationException($"This document prints through report {layout}: give the PDF file path to save it to (or open {layout} in the report viewer).");
            Dictionary<string, object?> parameters = new(StringComparer.OrdinalIgnoreCase) { ["Number"] = number, ["Date"] = date };
            Application.Abstractions.Reporting.RenderedDocument pdf = Check(await c.Get<Application.Abstractions.Reporting.IReportRunner>()
                .RenderAsync(c.CompanyId, layout, parameters, Application.Abstractions.Reporting.ReportFormat.Pdf).ConfigureAwait(true));
            await System.IO.File.WriteAllBytesAsync(path, pdf.Content).ConfigureAwait(true);
            return $"{Application.Documents.DocumentKinds.Find(kind)?.Name ?? kind} {number} printed through layout {layout} to {path}.";
        }

        Operation Doc(string kind, string title, string label, Func<IDocumentLayoutService, long, string, Task<One4All.Core.Common.Result<string>>> print) => new()
        {
            Module = module, Title = title, IsDocument = true,
            Description = $"Prints the {title.ToLowerInvariant()} in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF.",
            Fields = [Req("Number", label), Opt("SaveTo", "PDF file path (when a layout is set)")],
            Run = (c, i) => PrintAsync(c, i, kind, i.Text("Number"), null, () => print(c.Get<IDocumentLayoutService>(), c.CompanyId, i.Text("Number"))),
        };

        yield return new Operation
        {
            Module = module, Title = "Print a document", IsDocument = true,
            Description = "Prints any document of the list - statements, reminders, remittances, payslips, vouchers, certificates, tickets, pick lists, RFQs, the chain of title, cheques, "
                + "shelf labels and the business documents - by its number (see Document layouts › Identified by), as the standard text or through the company's own layout as a PDF.",
            Fields = [Pick("Document", "Document", [.. Application.Documents.DocumentKinds.All.Select(k => k.Name)]), Req("Number", "Number or code (two parts as a/b where the document says so)"),
                new OperationField("Date", "Date (as at, pay date or month; today when blank)", FieldKind.Date, null, Required: false), Opt("SaveTo", "PDF file path (when a layout is set)")],
            Run = (c, i) =>
            {
                Application.Documents.DocumentKind kind = Application.Documents.DocumentKinds.Find(i.Text("Document")) ?? throw new OperationException("Choose a document.");
                return PrintAsync(c, i, kind.Kind, i.Text("Number"), i.OptionalDate("Date"),
                    () => c.Get<ITextDocumentService>().RenderAsync(c.CompanyId, kind.Kind, i.Text("Number"), i.OptionalDate("Date")));
            },
        };
        yield return Doc(Application.Documents.DocumentKinds.Quotation, "Quotation", "Quote number", (s, co, n) => s.QuotationAsync(co, n));
        yield return Doc(Application.Documents.DocumentKinds.OrderConfirmation, "Order confirmation", "Sales order number", (s, co, n) => s.OrderConfirmationAsync(co, n));
        yield return Doc(Application.Documents.DocumentKinds.DeliveryNote, "Delivery note", "Delivery reference", (s, co, n) => s.DeliveryNoteAsync(co, n));
        yield return Doc(Application.Documents.DocumentKinds.PackingSlip, "Packing slip", "Delivery reference", (s, co, n) => s.DeliveryNoteAsync(co, n, packing: true));
        yield return Doc(Application.Documents.DocumentKinds.Requisition, "Purchase requisition", "Requisition number", (s, co, n) => s.RequisitionAsync(co, n));
        yield return Doc(Application.Documents.DocumentKinds.GoodsReceivedNote, "Goods received note", "GRN number", (s, co, n) => s.GoodsReceivedNoteAsync(co, n));
        yield return Doc(Application.Documents.DocumentKinds.SupplierDebitNote, "Supplier debit note", "Debit note number", (s, co, n) => s.SupplierDebitNoteAsync(co, n));
        yield return Doc(Application.Documents.DocumentKinds.FeeInvoice, "Fee invoice", "Invoice number", (s, co, n) => s.FeeInvoiceAsync(co, n));
        yield return Doc(Application.Documents.DocumentKinds.PaymentCertificate, "Payment certificate", "Certificate number", (s, co, n) => s.PaymentCertificateAsync(co, n));
        yield return new Operation
        {
            Module = module, Title = "Municipal bill", Description = "Prints a stand's monthly municipal bill, in the standard layout or through the company's own (as a PDF).", IsDocument = true,
            Fields = [Req("Stand", "Stand number"), Today("Month", "Any day in the month"), Opt("SaveTo", "PDF file path (when a layout is set)")],
            Run = (c, i) => PrintAsync(c, i, Application.Documents.DocumentKinds.MunicipalBill, i.Text("Stand"), i.Date("Month"),
                () => c.Get<IDocumentLayoutService>().MunicipalBillAsync(c.CompanyId, i.Text("Stand"), i.Date("Month"))),
        };
        yield return new Operation
        {
            Module = module, Title = "Tax invoice or credit note", Description = "Prints a customer tax invoice or credit note by its number, in the standard layout or through the company's own (as a PDF).",
            IsDocument = true, Fields = [Req("Number", "Invoice or credit note number"), Opt("SaveTo", "PDF file path (when a layout is set)")],
            Run = (c, i) => PrintAsync(c, i, Application.Documents.DocumentKinds.TaxInvoice, i.Text("Number"), null, async () =>
            {
                string number = i.Text("Number").Trim();
                long? item = await c.Db.OpenItems.AsNoTracking().Where(o => o.CompanyId == c.CompanyId && o.Ledger == Core.Trading.TradingLedger.AccountsReceivable && o.DocumentNumber == number)
                    .Select(o => (long?)o.Id).FirstOrDefaultAsync().ConfigureAwait(true);
                return item is null ? One4All.Core.Common.Error.NotFound("Doc.InvoiceNotFound", $"No invoice or credit note {number}.")
                    : await c.Get<Application.Abstractions.Trading.IInvoiceDocumentService>().RenderTextAsync(c.CompanyId, item.Value).ConfigureAwait(true);
            }),
        };
        yield return new Operation
        {
            Module = module, Title = "Purchase order document", Description = "Prints a purchase order by its number, in the standard layout or through the company's own (as a PDF).",
            IsDocument = true, Fields = [Req("Number", "Order number"), Opt("SaveTo", "PDF file path (when a layout is set)")],
            Run = (c, i) => PrintAsync(c, i, Application.Documents.DocumentKinds.PurchaseOrder, i.Text("Number"), null, async () =>
            {
                string number = i.Text("Number").Trim();
                long? order = await c.Db.PurchaseOrders.AsNoTracking().Where(o => o.CompanyId == c.CompanyId && o.OrderNumber == number).Select(o => (long?)o.Id).FirstOrDefaultAsync().ConfigureAwait(true);
                return order is null ? One4All.Core.Common.Error.NotFound("Doc.OrderNotFound", $"No purchase order {number}.")
                    : await c.Get<Application.Abstractions.Procurement.IPurchaseOrderDocumentService>().RenderTextAsync(c.CompanyId, order.Value).ConfigureAwait(true);
            }),
        };
        yield return new Operation
        {
            Module = module, Title = "Shelf labels", Description = "Prints shelf labels at a price list's prices (all items, or a comma-separated list).", IsDocument = true,
            Fields = [Req("PriceList", "Price list code"), Opt("Items", "Item codes (comma-separated)"), Num("PerRow", "Labels per row", "2"), Tick("Print", "Send to the label printer (System Manager › Devices)")],
            Run = async (c, i) =>
            {
                string labels = Check(await c.Get<IDocumentLayoutService>().ShelfLabelsAsync(c.CompanyId, i.Text("PriceList"),
                    i.Optional("Items")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), i.Whole("PerRow")).ConfigureAwait(true));
                if (!i.Flag("Print"))
                {
                    return labels;
                }

                if (c.Get<Application.Abstractions.Pos.IDeviceProvider>().LabelPrinter is not Application.Abstractions.Pos.ILabelPrinter printer)
                {
                    throw new OperationException("No label printer is set up (System Manager › Devices).");
                }

                string? outcome = await printer.PrintAsync(System.Text.Encoding.UTF8.GetBytes(labels + Environment.NewLine)).ConfigureAwait(true);
                return labels + Environment.NewLine + (outcome ?? "Sent to the label printer.");
            },
        };
    }

    // A pay element as keyed on the pay elements form (for a new one or an edit).
    private static NewPayElement ElementFrom(OperationInput i) => new NewPayElement(i.Optional("Code") ?? string.Empty, i.Optional("Name") ?? string.Empty,
            Enum.Parse<PayElementType>(i.Text("Type")), Enum.Parse<CalculationBasis>(i.Text("Basis")), (int)(i.OptionalNumber("Sequence") ?? 10), i.Optional("Account") ?? string.Empty)
        {
            FixedAmount = i.OptionalNumber("Amount") ?? 0m, Rate = i.OptionalNumber("Rate") ?? 0m, Formula = i.Optional("Formula"), Taxable = i.Flag("Taxable"), TaxTableCode = i.Optional("TaxTable"),
            Pensionable = i.Flag("Pensionable"), SubjectToSocialSecurity = i.Flag("SocialSecurity"), OvertimeBase = i.Flag("OvertimeBase"), LeavePayBase = i.Flag("LeaveBase"),
            ProRated = i.Flag("ProRated"), ArrearsRecoverable = !i.Flag("NoArrears"), TerminalBenefit = i.Flag("Terminal"), UnionDues = i.Flag("Union"),
            Group = i.Optional("Group"), BasisGroup = i.Optional("BasisGroup"), Floor = i.OptionalNumber("Floor"), Ceiling = i.OptionalNumber("Ceiling"), RoundTo = i.OptionalNumber("RoundTo"),
            Rounding = Enum.Parse<PayRounding>(i.Text("Rounding")), ContributionExpenseAccountCode = i.Optional("Expense"), DepartmentAccounts = i.Optional("DeptAccounts"),
            LookupVariable = i.Optional("LookupVariable"), LookupBands = i.Optional("LookupBands"),
            Recovers = Enum.Parse<PayRecovery>(i.Text("Recovers")), BenefitCategory = i.Optional("BenefitCategory"), PaidInCurrency = i.Optional("PaidIn"),
            CumulativeTax = i.Flag("CumulativeTax"), SplitCurrencyTax = i.Flag("SplitTax"), IsAnnualPayment = i.Flag("Annual"),
        };
}

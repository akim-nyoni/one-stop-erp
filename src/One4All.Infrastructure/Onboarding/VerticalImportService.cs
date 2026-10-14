using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Onboarding;
using One4All.Application.Onboarding;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.FixedAssets;
using One4All.Core.Municipal;
using One4All.Core.Payroll;
using One4All.Core.Schools;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Onboarding;

/// <summary>Opening-balance importers for the module registers, validate-first and all-or-nothing (brief §10).</summary>
public sealed class VerticalImportService : IVerticalImportService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">Posts the opening journals.</param>
    public VerticalImportService(One4AllDbContext db, IPostingEngine engine, Application.Abstractions.Budgeting.IBudgetingService? budgeting = null)
    {
        _db = db;
        _engine = engine;
        _budgeting = budgeting;
    }

    private readonly Application.Abstractions.Budgeting.IBudgetingService? _budgeting;

    /// <inheritdoc />
    public async Task<ImportReport> ImportStandsAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "stand", "parcel", "size");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        Dictionary<string, long> parcels = await _db.LandParcels.Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.ParcelNumber.ToUpper(), p => p.Id, ct).ConfigureAwait(false);
        Dictionary<string, long> schemes = await _db.HousingSchemes.Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Code.ToUpper(), s => s.Id, ct).ConfigureAwait(false);
        HashSet<string> existing = [.. await _db.HousingStands.Where(s => s.CompanyId == companyId).Select(s => s.StandNumber).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = [];
        List<(string Stand, string Parcel, long? Scheme, decimal Size, decimal Cost, Core.Housing.StandStatus Status)> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string stand = Code(row.Text("stand")), parcel = Code(row.Text("parcel")), scheme = Code(row.Text("scheme"));
                decimal size = row.Number("size") ?? 0m, cost = row.Number("cost") ?? 0m;
                Core.Housing.StandStatus status = Core.Housing.StandStatus.Available;
                if (stand.Length == 0 || parcel.Length == 0 || size <= 0m || cost < 0m
                    || (row.Text("status") is string given && (!Enum.TryParse(given, true, out status) || !Core.Housing.StandLifecycle.Unencumbered.Contains(status))))
                {
                    return "A stand needs a number, a parcel, a size above zero, a cost not below zero and, if given, a status from raw land to available "
                        + "(sold and allocated stands come in through their agreements).";
                }

                long? schemeId = null;
                if (scheme.Length > 0)
                {
                    if (!schemes.TryGetValue(scheme, out long id))
                    {
                        return $"Scheme {scheme} is not on the register.";
                    }

                    schemeId = id;
                }

                if (!seen.Add(stand))
                {
                    return $"Stand {stand} appears twice.";
                }

                if (existing.Contains(stand))
                {
                    skipped++;
                    return null;
                }

                toCreate.Add((stand, parcel, schemeId, size, cost, status));
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            foreach (IGrouping<string, (string Stand, string Parcel, long? Scheme, decimal Size, decimal Cost, Core.Housing.StandStatus Status)> group in toCreate.GroupBy(s => s.Parcel))
            {
                if (!parcels.TryGetValue(group.Key, out long parcelId))
                {
                    Core.Housing.LandParcel parcel = new(companyId, group.Key, $"Parcel {group.Key} (migrated)", group.Sum(s => s.Size), group.Sum(s => s.Cost));
                    _db.LandParcels.Add(parcel);
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                    parcelId = parcel.Id;
                }

                _db.HousingStands.AddRange(group.Select(s => new Core.Housing.HousingStand(companyId, parcelId, s.Scheme, s.Stand, s.Size, s.Cost, null, s.Status)));
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportBudgetAsync(long companyId, string csv, DateOnly yearDate, string label, Core.Budgeting.BudgetType type, string? department, bool commit,
        CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "account", "period", "amount");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        long? yearId = await _db.FiscalYears.Where(y => y.CompanyId == companyId && y.StartDate <= yearDate && y.EndDate >= yearDate).Select(y => (long?)y.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (yearId is null || _budgeting is null || string.IsNullOrWhiteSpace(label))
        {
            return new ImportReport(rows.Count, 0, 0, [new ImportError(0, yearId is null ? $"No fiscal year contains {yearDate:yyyy-MM-dd}." : "A budget import needs a label.")], false);
        }

        HashSet<string> accounts = await AccountsAsync(companyId, ct).ConfigureAwait(false);
        List<ImportError> errors = [];
        List<Application.Abstractions.Budgeting.NewBudgetLine> lines = [];
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string account = Code(row.Text("account"));
                decimal? period = row.Number("period"), amount = row.Number("amount");
                if (!accounts.Contains(account))
                {
                    return $"Account {account} is not in the chart.";
                }

                if (period is not decimal p || p != decimal.Truncate(p) || p < 1m || p > 13m || amount is null)
                {
                    return "A budget line needs a period from 1 to 13 and an amount.";
                }

                lines.Add(new Application.Abstractions.Budgeting.NewBudgetLine(account, (int)p, amount.Value, row.Text("cost_centre"), row.Number("quantity")));
                return null;
            });
        }

        return await CommitAsync(rows.Count, 0, errors, commit, ct, async () =>
        {
            Result<long> created = await _budgeting.CreateBudgetAsync(companyId, yearId.Value, label.Trim(), type, string.IsNullOrWhiteSpace(department) ? null : department.Trim(), lines, ct)
                .ConfigureAwait(false);
            return created.IsFailure ? created.Error : lines.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportFixedAssetsAsync(long companyId, string csv, DateOnly asAt, string suspenseAccountCode, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "asset_number", "description", "class", "acquired", "cost", "accumulated", "life_months", "cost_account",
            "accum_account", "expense_account");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        HashSet<string> accounts = await AccountsAsync(companyId, ct).ConfigureAwait(false);
        string suspense = Code(suspenseAccountCode);
        HashSet<string> existing = [.. await _db.FixedAssets.Where(a => a.CompanyId == companyId).Select(a => a.AssetNumber).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = SuspenseErrors(accounts, suspense);
        List<(FixedAsset Asset, decimal Accumulated)> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string number = Code(row.Text("asset_number")), description = row.Text("description") ?? string.Empty, assetClass = row.Text("class") ?? string.Empty;
                decimal cost = row.Number("cost") ?? 0m, accumulated = row.Number("accumulated") ?? 0m, residual = row.Number("residual") ?? 0m;
                int life = (int)(row.Number("life_months") ?? 0m);
                DateOnly acquired = row.Date("acquired") ?? asAt;
                DepreciationMethod method = DepreciationMethod.StraightLine;
                string costAccount = Code(row.Text("cost_account")), accumAccount = Code(row.Text("accum_account")), expenseAccount = Code(row.Text("expense_account"));
                if (number.Length == 0 || description.Length == 0 || cost <= 0m || accumulated < 0m || residual < 0m || accumulated > cost - residual || life <= 0 || acquired > asAt
                    || (row.Text("method") is string m && !Enum.TryParse(m, true, out method)))
                {
                    return "An asset needs a number, a description, a positive cost, depreciation to date between nil and cost less residual, a life in months, an "
                        + "acquisition date on or before the as-at date and, if given, a known method.";
                }

                if (new[] { costAccount, accumAccount, expenseAccount }.FirstOrDefault(a => !accounts.Contains(a)) is string unknown)
                {
                    return $"Account {unknown} does not exist.";
                }

                if (!seen.Add(number))
                {
                    return $"Asset {number} appears twice.";
                }

                if (existing.Contains(number))
                {
                    skipped++;
                    return null;
                }

                toCreate.Add((new FixedAsset(companyId, number, description, assetClass, cost, residual, acquired, method, life, row.Number("rate") ?? 0m, costAccount, accumAccount, expenseAccount),
                    accumulated));
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            foreach ((FixedAsset asset, decimal accumulated) in toCreate)
            {
                // Depreciation to date is recorded up to the as-at month; the next run charges the month after.
                asset.ApplyDepreciation(accumulated, asAt);
                _db.FixedAssets.Add(asset);
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            List<(string Account, decimal Debit)> movements = [.. toCreate.Select(t => (t.Asset.CostAccountCode, t.Asset.Cost)),
                .. toCreate.Where(t => t.Accumulated > 0m).Select(t => (t.Asset.AccumulatedDepreciationAccountCode, -t.Accumulated))];
            Result posted = await PostOpeningAsync(companyId, asAt, suspense, "OPENING-FA", "Opening fixed asset register", movements, ct).ConfigureAwait(false);
            return posted.IsFailure ? posted.Error : toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportEmployeesAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "employee_number", "name", "basic_salary", "engaged");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        HashSet<string> existing = [.. await _db.Employees.Where(e => e.CompanyId == companyId).Select(e => e.EmployeeNumber).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = [];
        List<Employee> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string number = Code(row.Text("employee_number")), name = row.Text("name") ?? string.Empty;
                decimal salary = row.Number("basic_salary") ?? -1m;
                DateOnly? engaged = row.Date("engaged");
                string? department = row.Text("department")?.ToUpperInvariant(), costCentre = row.Text("cost_centre")?.ToUpperInvariant();
                if (number.Length == 0 || name.Length == 0 || salary < 0m || engaged is null)
                {
                    return "An employee needs a number, a name, a basic salary of nil or more and an engagement date.";
                }

                if (department?.Length > 3 || costCentre?.Length > 3)
                {
                    return "Department and cost centre codes are at most three characters (the ledger dimension codes).";
                }

                if (!seen.Add(number))
                {
                    return $"Employee {number} appears twice.";
                }

                if (existing.Contains(number))
                {
                    skipped++;
                    return null;
                }

                // The optional columns a payroll needs from day one: tax and bank details, how and how often the employee is paid, and the contract.
                Gender gender = Gender.Unspecified;
                EmploymentType employment = EmploymentType.Permanent;
                PayMethod method = PayMethod.BankTransfer;
                PayFrequency frequency = PayFrequency.Monthly;
                if ((row.Text("gender") is string g && (!Enum.TryParse(g, true, out gender) || !Enum.IsDefined(gender)))
                    || (row.Text("employment_type") is string t && (!Enum.TryParse(t, true, out employment) || !Enum.IsDefined(employment)))
                    || (row.Text("payment_method") is string m && (!Enum.TryParse(m, true, out method) || !Enum.IsDefined(method)))
                    || (row.Text("pay_frequency") is string f && (!Enum.TryParse(f, true, out frequency) || !Enum.IsDefined(frequency))))
                {
                    return $"Gender ({string.Join("/", Enum.GetNames<Gender>())}), employment type ({string.Join("/", Enum.GetNames<EmploymentType>())}), "
                        + $"payment method ({string.Join("/", Enum.GetNames<PayMethod>())}) or pay frequency ({string.Join("/", Enum.GetNames<PayFrequency>())}) is not one of the choices.";
                }

                if (method == PayMethod.BankTransfer && row.Text("bank_account") is null && row.Text("bank_name") is not null)
                {
                    return "A bank name needs the bank account number.";
                }

                if (method == PayMethod.MobileMoney && row.Text("mobile_money") is null)
                {
                    return "Payment by mobile money needs the mobile money number.";
                }

                toCreate.Add(new Employee(companyId, number, name, salary, engaged.Value)
                {
                    NationalIdNumber = row.Text("national_id"), Department = department, CostCentre = costCentre, Grade = row.Text("grade"),
                    TaxNumber = row.Text("tax_number"), DateOfBirth = row.Date("date_of_birth"), Gender = gender, EmploymentType = employment, PaymentMethod = method,
                    PayFrequency = frequency, PayGroup = row.Text("pay_group")?.ToUpperInvariant(), BankName = row.Text("bank_name"), BankBranch = row.Text("bank_branch"),
                    BankAccountNumber = row.Text("bank_account"), MobileMoneyNumber = row.Text("mobile_money"), Email = row.Text("email"), Phone = row.Text("phone"),
                    JobTitle = row.Text("job_title"), ContractEndDate = row.Date("contract_end"), SocialSecurityNumber = row.Text("social_security"),
                });
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            _db.Employees.AddRange(toCreate);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportDimensionsAsync(long companyId, string csv, bool departments, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "code", "name");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        string what = departments ? "department" : "cost centre";
        HashSet<string> existing = departments
            ? [.. await _db.Departments.Where(d => d.CompanyId == companyId).Select(d => d.Code).ToListAsync(ct).ConfigureAwait(false)]
            : [.. await _db.CostCentres.Where(c => c.CompanyId == companyId).Select(c => c.Code).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = [];
        List<(string Code, string Name)> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string code = Code(row.Text("code")), name = row.Text("name") ?? string.Empty;
                if (code.Length is 0 or > 3 || name.Length == 0)
                {
                    return $"A {what} needs a code of one to three characters and a name.";
                }

                if (!seen.Add(code))
                {
                    return $"The {what} {code} appears twice.";
                }

                if (existing.Contains(code))
                {
                    skipped++;
                    return null;
                }

                toCreate.Add((code, name));
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            foreach ((string code, string name) in toCreate)
            {
                if (departments)
                {
                    _db.Departments.Add(new Department(companyId, code, name));
                }
                else
                {
                    _db.CostCentres.Add(new CostCentre(companyId, code, name));
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportTaxCodesAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "code", "name", "type", "rate", "effective_from");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        HashSet<string> existing = [.. await _db.TaxCodes.Where(t => t.CompanyId == companyId).Select(t => t.Code).ToListAsync(ct).ConfigureAwait(false)];
        HashSet<string> accounts = new(await _db.Accounts.Where(a => a.CompanyId == companyId).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);
        List<ImportError> errors = [];
        List<TaxCode> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string code = Code(row.Text("code")), name = row.Text("name") ?? string.Empty;
                decimal? rate = row.Number("rate");
                DateOnly? from = row.Date("effective_from");
                string? output = row.Text("output_account"), input = row.Text("input_account");
                if (code.Length == 0 || name.Length == 0 || !Enum.TryParse(row.Text("type"), true, out TaxType type) || !Enum.IsDefined(type) || rate is not (>= 0m and <= 100m) || from is null)
                {
                    return $"A tax code needs a code, a name, a type ({string.Join("/", Enum.GetNames<TaxType>())}), a rate from 0 to 100 and the date it applies from.";
                }

                if ((output is not null && !accounts.Contains(output)) || (input is not null && !accounts.Contains(input)))
                {
                    return "The output or input tax account is not in the chart of accounts.";
                }

                if (!seen.Add(code))
                {
                    return $"Tax code {code} appears twice.";
                }

                if (existing.Contains(code))
                {
                    skipped++;
                    return null;
                }

                TaxCode tax = new(companyId, code, name, type) { OutputAccountCode = output, InputAccountCode = input };
                tax.AddRate(from.Value, rate.Value);
                toCreate.Add(tax);
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            _db.TaxCodes.AddRange(toCreate);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportLeaveBalancesAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "employee_number", "leave_type", "days");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        Dictionary<string, long> employees = await _db.Employees.Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.EmployeeNumber, e => e.Id, StringComparer.OrdinalIgnoreCase, ct)
            .ConfigureAwait(false);
        HashSet<(long, string)> held = [.. (await _db.EmployeeLeaveBalances.Where(b => b.CompanyId == companyId).Select(b => new { b.EmployeeId, b.LeaveType }).ToListAsync(ct).ConfigureAwait(false))
            .Select(b => (b.EmployeeId, b.LeaveType.ToUpperInvariant()))];
        List<ImportError> errors = [];
        List<EmployeeLeaveBalance> toCreate = [];
        HashSet<(long, string)> seen = [];
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string number = Code(row.Text("employee_number")), type = Code(row.Text("leave_type"));
                decimal? days = row.Number("days"), taken = row.Number("taken");
                if (!employees.TryGetValue(number, out long employee))
                {
                    return $"No employee {number} (import employees first).";
                }

                if (type.Length == 0 || days is not >= 0m || taken is < 0m)
                {
                    return "An opening leave balance needs a leave type and days of nil or more (and taken, if given, nil or more).";
                }

                if (!seen.Add((employee, type)))
                {
                    return $"{number} {type} appears twice.";
                }

                if (held.Contains((employee, type)))
                {
                    skipped++;
                    return null;
                }

                EmployeeLeaveBalance balance = new(companyId, employee, type);
                balance.Accrue(days.Value);
                if (taken is decimal used && used > 0m)
                {
                    balance.Take(used);
                }

                toCreate.Add(balance);
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            _db.EmployeeLeaveBalances.AddRange(toCreate);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportPayrollYtdAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "employee_number", "tax_year", "code", "amount");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        Dictionary<string, long> employees = await _db.Employees.Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.EmployeeNumber, e => e.Id, StringComparer.OrdinalIgnoreCase, ct)
            .ConfigureAwait(false);
        HashSet<string> elements = new(await _db.PayElements.Where(e => e.CompanyId == companyId).Select(e => e.Code).ToListAsync(ct).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);
        List<ImportError> errors = [];
        List<(long Employee, int Year, string Code, decimal Amount)> figures = [];
        HashSet<(long, int, string)> seen = [];
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string number = Code(row.Text("employee_number")), code = Code(row.Text("code"));
                decimal? year = row.Number("tax_year"), amount = row.Number("amount");
                if (!employees.TryGetValue(number, out long employee))
                {
                    return $"No employee {number}.";
                }

                if (code != PayrollYtdOpening.TaxableCode && !elements.Contains(code))
                {
                    return $"'{code}' is neither TAXABLE nor a pay element.";
                }

                if (year is not (>= 2000 and <= 2100) || year != decimal.Truncate(year.Value) || amount is not >= 0m)
                {
                    return "A year-to-date figure needs a tax year and an amount of nil or more.";
                }

                if (!seen.Add((employee, (int)year.Value, code)))
                {
                    return $"{number} {code} {year} appears twice.";
                }

                figures.Add((employee, (int)year.Value, code, amount.Value));
                return null;
            });
        }

        return await CommitAsync(rows.Count, 0, errors, commit, ct, async () =>
        {
            List<long> ids = [.. figures.Select(f => f.Employee).Distinct()];
            Dictionary<(long, int, string), PayrollYtdOpening> existing = (await _db.Set<PayrollYtdOpening>().Where(o => ids.Contains(o.EmployeeId)).ToListAsync(ct).ConfigureAwait(false))
                .ToDictionary(o => (o.EmployeeId, o.TaxYear, o.Code));
            foreach ((long employee, int year, string code, decimal amount) in figures)
            {
                if (existing.TryGetValue((employee, year, code), out PayrollYtdOpening? opening))
                {
                    opening.Amount = amount;
                }
                else
                {
                    _db.Set<PayrollYtdOpening>().Add(new PayrollYtdOpening(companyId, employee, year, code, amount));
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return figures.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportEmployeeLoansAsync(long companyId, string csv, DateOnly asAt, string suspenseAccountCode, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "employee_number", "reference", "outstanding", "instalment", "loan_account");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        HashSet<string> accounts = await AccountsAsync(companyId, ct).ConfigureAwait(false);
        string suspense = Code(suspenseAccountCode);
        Dictionary<string, long> employees = await _db.Employees.Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.EmployeeNumber, e => e.Id, ct).ConfigureAwait(false);
        HashSet<string> existing = [.. await _db.EmployeeLoans.Where(l => l.CompanyId == companyId).Select(l => l.Reference).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = SuspenseErrors(accounts, suspense);
        List<EmployeeLoan> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string employee = Code(row.Text("employee_number")), reference = Code(row.Text("reference")), account = Code(row.Text("loan_account"));
                decimal outstanding = row.Number("outstanding") ?? 0m, instalment = row.Number("instalment") ?? 0m;
                if (reference.Length == 0 || outstanding <= 0m || instalment <= 0m)
                {
                    return "A loan needs a reference, a positive balance and a positive instalment.";
                }

                if (!employees.TryGetValue(employee, out long employeeId))
                {
                    return $"Employee {employee} does not exist (import employees first).";
                }

                if (!accounts.Contains(account))
                {
                    return $"Account {account} does not exist.";
                }

                if (!seen.Add(reference))
                {
                    return $"Loan {reference} appears twice.";
                }

                if (existing.Contains(reference))
                {
                    skipped++;
                    return null;
                }

                toCreate.Add(new EmployeeLoan(companyId, employeeId, reference, outstanding, instalment, asAt, account));
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            _db.EmployeeLoans.AddRange(toCreate);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            Result posted = await PostOpeningAsync(companyId, asAt, suspense, "OPENING-LOANS", "Opening staff loan balances",
                [.. toCreate.Select(l => (l.LoanReceivableAccountCode, l.Outstanding))], ct).ConfigureAwait(false);
            return posted.IsFailure ? posted.Error : toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportMetersAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "serial", "stand", "service", "reading", "date");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        var links = await (from l in _db.Set<PropertyServiceLink>()
                           join p in _db.Properties on l.PropertyId equals p.Id
                           where p.CompanyId == companyId && l.IsActive
                           select new { Link = l, p.StandNumber }).ToListAsync(ct).ConfigureAwait(false);
        HashSet<string> serials = [.. await _db.Set<Meter>().Where(m => m.CompanyId == companyId).Select(m => m.Serial).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = [];
        List<(Meter Meter, PropertyServiceLink Link, decimal Reading, DateOnly Date)> toInstall = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        HashSet<PropertyServiceLink> claimed = [];
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string serial = Code(row.Text("serial")), stand = Code(row.Text("stand"));
                decimal? reading = row.Number("reading");
                DateOnly? date = row.Date("date");
                int digits = (int)(row.Number("digits") ?? 0m);
                decimal multiplier = row.Number("multiplier") ?? 1m;
                if (serial.Length == 0 || reading is not >= 0m || date is null || digits is < 0 or > 12 || multiplier <= 0m)
                {
                    return "A meter needs a serial, an opening reading of nil or more, a date, 0 to 12 dials and a positive multiplier.";
                }

                if (!Enum.TryParse(row.Text("service"), ignoreCase: true, out ServiceType service))
                {
                    return $"'{row.Text("service")}' is not a service.";
                }

                if (serials.Contains(serial) || !seen.Add(serial))
                {
                    return $"Meter {serial} is already on the register or appears twice.";
                }

                PropertyServiceLink? link = links.Where(l => l.StandNumber == stand && l.Link.ServiceType == service).Select(l => l.Link).FirstOrDefault();
                if (link is null)
                {
                    return $"Stand {stand} has no active {service} service.";
                }

                if (link.MeterId is not null || !claimed.Add(link))
                {
                    return $"Stand {stand}'s {service} service already has a meter.";
                }

                Meter meter = new(companyId, serial, multiplier) { Digits = digits, InstalledOn = date };
                meter.Start(reading.Value, date.Value, 0m);
                toInstall.Add((meter, link, reading.Value, date.Value));
                return null;
            });
        }

        return await CommitAsync(rows.Count, 0, errors, commit, ct, async () =>
        {
            _db.Set<Meter>().AddRange(toInstall.Select(m => m.Meter));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            foreach ((Meter meter, PropertyServiceLink link, decimal reading, DateOnly date) in toInstall)
            {
                link.MeterId = meter.Id;
                _db.Set<MeterReading>().Add(new MeterReading(companyId, meter.Id, date, reading, reading, 0m, 0m, MeterReadingKind.Actual, ReadingFlags.None, accepted: true));
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return toInstall.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportPropertiesAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "stand", "ward", "ratepayer");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        Dictionary<string, long> customers = await _db.Customers.Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Code, c => c.Id, ct).ConfigureAwait(false);
        Dictionary<string, long> tariffs = (await _db.Tariffs.Where(t => t.CompanyId == companyId).OrderBy(t => t.EffectiveFrom).Select(t => new { t.Code, t.Id }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.Code).ToDictionary(g => g.Key, g => g.First().Id);
        HashSet<string> existing = [.. await _db.Properties.Where(p => p.CompanyId == companyId).Select(p => p.StandNumber).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = [];
        List<Property> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string stand = Code(row.Text("stand")), ward = row.Text("ward") ?? string.Empty, ratepayer = Code(row.Text("ratepayer"));
                decimal valuation = row.Number("valuation") ?? 0m, size = row.Number("size") ?? 0m;
                DateOnly connected = row.Date("connected") ?? DateOnly.FromDateTime(DateTime.Today);
                if (stand.Length == 0 || ward.Length == 0 || valuation < 0m || size < 0m)
                {
                    return "A property needs a stand number, a ward and no negative valuation or size.";
                }

                if (!customers.TryGetValue(ratepayer, out long ratepayerId))
                {
                    return $"Ratepayer {ratepayer} is not a customer (import customers first).";
                }

                List<(ServiceType Service, long TariffId)> services = [];
                foreach (string part in (row.Text("services") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                    if (bits.Length != 2 || !Enum.TryParse(bits[0], true, out ServiceType service) || !tariffs.TryGetValue(bits[1].ToUpperInvariant(), out long tariffId))
                    {
                        return $"Service '{part}' is not service:tariff with a known service and tariff code.";
                    }

                    services.Add((service, tariffId));
                }

                if (!seen.Add(stand))
                {
                    return $"Stand {stand} appears twice.";
                }

                if (existing.Contains(stand))
                {
                    skipped++;
                    return null;
                }

                Property property = new(companyId, stand, ward, ratepayerId) { Valuation = valuation, SizeSquareMetres = size };
                foreach ((ServiceType service, long tariffId) in services)
                {
                    property.AddService(service, tariffId, connected);
                }

                toCreate.Add(property);
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            _db.Properties.AddRange(toCreate);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportStudentsAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "admission", "name", "grade", "guardian");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        Dictionary<string, long> customers = await _db.Customers.Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Code, c => c.Id, ct).ConfigureAwait(false);
        HashSet<string> existing = [.. await _db.Students.Where(s => s.CompanyId == companyId).Select(s => s.AdmissionNumber).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = [];
        List<Student> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string admission = Code(row.Text("admission")), name = row.Text("name") ?? string.Empty, grade = row.Text("grade") ?? string.Empty, guardian = Code(row.Text("guardian"));
                bool boarder = false;
                if (admission.Length == 0 || name.Length == 0 || grade.Length == 0 || (row.Text("boarder") is string b && !bool.TryParse(b, out boarder)))
                {
                    return "A student needs an admission number, a name, a grade and, if given, boarder true / false.";
                }

                if (!customers.TryGetValue(guardian, out long guardianId))
                {
                    return $"Guardian {guardian} is not a customer (import customers first).";
                }

                if (!seen.Add(admission))
                {
                    return $"Student {admission} appears twice.";
                }

                if (existing.Contains(admission))
                {
                    skipped++;
                    return null;
                }

                toCreate.Add(new Student(companyId, admission, name, grade, guardianId) { IsBoarder = boarder, AdmittedOn = row.Date("admitted") });
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            _db.Students.AddRange(toCreate);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportAccountsAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "code", "name", "type");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        HashSet<string> existing = [.. await _db.Accounts.Where(a => a.CompanyId == companyId).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = [];
        List<LedgerAccount> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string code = Code(row.Text("code")), name = row.Text("name") ?? string.Empty;
                bool manual = true, budget = true;
                if (code.Length is 0 or > 8 || name.Length == 0 || !Enum.TryParse(row.Text("type"), true, out AccountType type) || !Enum.IsDefined(type)
                    || (row.Text("manual") is string m && !bool.TryParse(m, out manual)) || (row.Text("budget") is string b && !bool.TryParse(b, out budget)))
                {
                    return "An account needs a code of up to 8 characters, a name, a type (Asset, Liability, Equity, Income, Expense) and, if given, manual / budget true or false.";
                }

                if (!seen.Add(code))
                {
                    return $"Account {code} appears twice.";
                }

                if (existing.Contains(code))
                {
                    skipped++;
                    return null;
                }

                toCreate.Add(new LedgerAccount(companyId, code, name, type, row.Text("category")) { AllowManualJournal = manual, BudgetEnabled = budget, IsActive = true });
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            _db.Accounts.AddRange(toCreate);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportWarehousesAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "code", "name");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        HashSet<string> existing = [.. await _db.Warehouses.Where(w => w.CompanyId == companyId).Select(w => w.Code).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = [];
        List<Core.Inventory.Warehouse> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string code = Code(row.Text("code")), name = row.Text("name") ?? string.Empty;
                if (code.Length == 0 || name.Length == 0)
                {
                    return "A warehouse needs a code and a name.";
                }

                if (!seen.Add(code))
                {
                    return $"Warehouse {code} appears twice.";
                }

                if (existing.Contains(code))
                {
                    skipped++;
                    return null;
                }

                toCreate.Add(new Core.Inventory.Warehouse(companyId, code, name));
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            _db.Warehouses.AddRange(toCreate);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportRentalUnitsAsync(long companyId, string csv, DateOnly asAt, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "code", "description", "type", "rooms", "rent", "rent_account");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        HashSet<string> accounts = [.. await _db.Accounts.Where(a => a.CompanyId == companyId).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)];
        HashSet<string> existing = [.. await _db.Set<Core.Housing.RentalUnit>().Where(u => u.CompanyId == companyId).Select(u => u.Code).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = [];
        List<Core.Housing.RentalUnit> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            Parse(row, errors, () =>
            {
                string code = Code(row.Text("code")), description = row.Text("description") ?? string.Empty, account = (row.Text("rent_account") ?? string.Empty).Trim();
                decimal rooms = row.Number("rooms") ?? -1m, rent = row.Number("rent") ?? -1m;
                decimal? most = row.Number("max_occupants");
                if (code.Length == 0 || description.Length == 0 || !Enum.TryParse(row.Text("type"), true, out Core.Housing.RentalUnitType type) || !Enum.IsDefined(type)
                    || rooms < 0m || rooms != decimal.Truncate(rooms) || rent < 0m || most is <= 0m)
                {
                    return "A rental unit needs a code, a description, a type (House, Flat, Room, HostelBed, Cottage), whole rooms, a rent of zero or more and, if given, a positive occupancy limit.";
                }

                if (!accounts.Contains(account))
                {
                    return $"Rent account {account} does not exist.";
                }

                if (!seen.Add(code))
                {
                    return $"Unit {code} appears twice.";
                }

                if (existing.Contains(code))
                {
                    skipped++;
                    return null;
                }

                Core.Housing.RentalUnit unit = new(companyId, code, description, type, (int)rooms, rent, account, row.Date("vacant_since") ?? asAt)
                {
                    Suburb = row.Text("suburb"),
                    MaxOccupants = most is decimal m ? (int)m : null,
                };
                if (row.Text("vacant_since") is null)
                {
                    unit.Let();
                }

                toCreate.Add(unit);
                return null;
            });
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            _db.Set<Core.Housing.RentalUnit>().AddRange(toCreate);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RegisterCheck>>> ReconcileRegistersAsync(long companyId, CancellationToken ct = default)
    {
        var assets = await _db.FixedAssets.AsNoTracking().Where(a => a.CompanyId == companyId && a.Status != FixedAssetStatus.Disposed)
            .Select(a => new { a.CostAccountCode, a.AccumulatedDepreciationAccountCode, a.Cost, a.AccumulatedDepreciation }).ToListAsync(ct).ConfigureAwait(false);
        var loans = await _db.EmployeeLoans.AsNoTracking().Where(l => l.CompanyId == companyId).Select(l => new { l.LoanReceivableAccountCode, l.Outstanding }).ToListAsync(ct).ConfigureAwait(false);
        List<string> assetAccounts = [.. assets.SelectMany(a => new[] { a.CostAccountCode, a.AccumulatedDepreciationAccountCode }).Distinct()];
        List<string> loanAccounts = [.. loans.Select(l => l.LoanReceivableAccountCode).Distinct()];
        List<string> all = [.. assetAccounts.Concat(loanAccounts).Distinct()];
        Dictionary<string, decimal> balances = await _db.Journals.Where(j => j.CompanyId == companyId && j.Status == JournalStatus.Posted).SelectMany(j => j.Lines)
            .Where(l => all.Contains(l.AccountCode)).GroupBy(l => l.AccountCode).Select(g => new { g.Key, Balance = g.Sum(l => l.BaseDebit - l.BaseCredit) })
            .ToDictionaryAsync(x => x.Key, x => x.Balance, ct).ConfigureAwait(false);
        List<RegisterCheck> checks =
        [
            new("Fixed assets (net book value)", assetAccounts.Sum(a => balances.GetValueOrDefault(a)), assets.Sum(a => a.Cost - a.AccumulatedDepreciation)),
            new("Staff loans", loanAccounts.Sum(a => balances.GetValueOrDefault(a)), loans.Sum(l => l.Outstanding)),
        ];
        return checks;
    }

    private static string Code(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static ImportReport Missing(IReadOnlyList<string> columns) => new(0, 0, 0, [new ImportError(1, $"Missing columns: {string.Join(", ", columns)}.")], false);

    private static List<ImportError> SuspenseErrors(HashSet<string> accounts, string suspense)
        => accounts.Contains(suspense) ? [] : [new ImportError(0, $"Suspense account {suspense} does not exist.")];

    // Runs one row's checks; a returned message or a badly formatted number / date is that line's error.
    private static void Parse(CsvRow row, List<ImportError> errors, Func<string?> check)
    {
        try
        {
            if (check() is string problem)
            {
                errors.Add(new ImportError(row.LineNumber, problem));
            }
        }
        catch (FormatException ex)
        {
            errors.Add(new ImportError(row.LineNumber, ex.Message));
        }
    }

    private async Task<HashSet<string>> AccountsAsync(long companyId, CancellationToken ct)
        => [.. await _db.Accounts.Where(a => a.CompanyId == companyId).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)];

    // Writes only when asked and the file is clean, all in one unit of work; a failure part-way rolls everything back.
    private async Task<ImportReport> CommitAsync(int rows, int skipped, List<ImportError> errors, bool commit, CancellationToken ct, Func<Task<Result<int>>> write)
    {
        if (errors.Count > 0 || !commit)
        {
            return new ImportReport(rows, 0, skipped, errors, false);
        }

        Result<int> written = await _db.AtomicAsync(write, ct).ConfigureAwait(false);
        return written.IsSuccess
            ? new ImportReport(rows, written.Value, skipped, [], true)
            : new ImportReport(rows, 0, skipped, [new ImportError(0, written.Error.Message)], false);
    }

    // One journal: each account's net movement (positive debit), the balance to the migration suspense account.
    private async Task<Result> PostOpeningAsync(long companyId, DateOnly date, string suspense, string documentNumber, string narrative, IReadOnlyList<(string Account, decimal Debit)> movements,
        CancellationToken ct)
    {
        List<JournalLineRequest> lines = [.. movements.GroupBy(m => m.Account, StringComparer.Ordinal).Select(g => (Account: g.Key, Net: g.Sum(m => m.Debit))).Where(x => x.Net != 0m)
            .Select(x => new JournalLineRequest { AccountCode = x.Account, Side = x.Net > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(x.Net) })];
        if (lines.Count == 0)
        {
            return Result.Success();
        }

        decimal net = lines.Sum(l => l.Side == JournalSide.Debit ? l.TransactionAmount : -l.TransactionAmount);
        if (net != 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = suspense, Side = net > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(net), Narrative = "Migration suspense" });
        }

        CurrencyCode currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).SingleAsync(ct).ConfigureAwait(false);
        string number = documentNumber;
        int clash = await _db.Journals.CountAsync(j => j.CompanyId == companyId && j.DocumentNumber.StartsWith(documentNumber), ct).ConfigureAwait(false);
        if (clash > 0)
        {
            number = $"{documentNumber}-{clash + 1}";
        }

        Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = currency, EnforceManualJournalRules = false,
            DocumentType = "OpeningBalance", DocumentNumber = number, Narrative = narrative, Lines = lines,
        }, ct).ConfigureAwait(false);
        return posted.IsFailure ? Result.Failure(posted.Error) : Result.Success();
    }
}

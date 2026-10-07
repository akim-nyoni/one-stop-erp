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
    public VerticalImportService(One4AllDbContext db, IPostingEngine engine)
    {
        _db = db;
        _engine = engine;
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

                toCreate.Add(new Employee(companyId, number, name, salary, engaged.Value)
                {
                    NationalIdNumber = row.Text("national_id"), Department = department, CostCentre = costCentre, Grade = row.Text("grade"),
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

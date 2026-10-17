using One4All.Core.Payroll;

namespace One4All.Application.Payroll;

/// <summary>What the pre-commit checks know about one employee in a run.</summary>
/// <param name="Number">The employee number.</param>
/// <param name="NationalId">The national ID, if captured.</param>
/// <param name="TaxNumber">The tax number, if captured.</param>
/// <param name="PaymentMethod">How net pay is paid.</param>
/// <param name="BankAccount">The bank account, if captured.</param>
/// <param name="MobileNumber">The mobile-money wallet, if captured.</param>
/// <param name="Status">The employment status.</param>
/// <param name="TerminationDate">The last day of service, if terminated.</param>
/// <param name="ContractEndDate">When a fixed-term contract ends, if it does.</param>
/// <param name="BasicSalary">The basic salary.</param>
/// <param name="Grade">The grade, if graded.</param>
/// <param name="GradeMinimum">The grade's lowest salary on the scale in force, if there is one.</param>
/// <param name="GradeMaximum">The grade's highest salary on the scale in force, if there is one.</param>
/// <param name="NegativeLeave">The leave types with a negative balance and the balance.</param>
/// <param name="BankDetailsChangedOn">When the bank or wallet details last changed after the previous run, or null.</param>
public sealed record RunEmployee(string Number, string? NationalId, string? TaxNumber, PayMethod PaymentMethod, string? BankAccount, string? MobileNumber, EmployeeStatus Status,
    DateOnly? TerminationDate, DateOnly? ContractEndDate, decimal BasicSalary, string? Grade, decimal? GradeMinimum, decimal? GradeMaximum,
    IReadOnlyList<(string LeaveType, decimal Balance)> NegativeLeave, DateTimeOffset? BankDetailsChangedOn);

/// <summary>
/// The payroll officer's safety net before commit (brief §4.15.6 pre-commit validation / exception report): no bank details, duplicate bank accounts or wallets
/// across employees, duplicate national IDs, no tax number, terminated employees still in the run, employees in the previous run missing from this one,
/// negative leave balances, salary outside the grade's scale, contracts expired, and bank details changed since the last run. Pure.
/// </summary>
public static class PayrollRunChecks
{
    /// <summary>The exceptions in a run.</summary>
    /// <param name="employees">The employees being paid.</param>
    /// <param name="previousRunNumbers">The employees paid in the previous regular run of the pay group.</param>
    /// <param name="payDate">The pay date.</param>
    /// <returns>One line per exception, employee by employee.</returns>
    public static IReadOnlyList<string> Exceptions(IReadOnlyList<RunEmployee> employees, IReadOnlyCollection<string> previousRunNumbers, DateOnly payDate)
    {
        ArgumentNullException.ThrowIfNull(employees);
        ArgumentNullException.ThrowIfNull(previousRunNumbers);
        static string? Key(string? value) => string.IsNullOrWhiteSpace(value) ? null : new string([.. value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
        ILookup<string, RunEmployee> Shared(Func<RunEmployee, string?> selector) => employees.Where(e => Key(selector(e)) is not null).ToLookup(e => Key(selector(e))!);
        ILookup<string, RunEmployee> banks = Shared(e => e.PaymentMethod == PayMethod.BankTransfer ? e.BankAccount : null);
        ILookup<string, RunEmployee> wallets = Shared(e => e.PaymentMethod == PayMethod.MobileMoney ? e.MobileNumber : null);
        ILookup<string, RunEmployee> ids = Shared(e => e.NationalId);

        List<string> issues = [];
        foreach (RunEmployee e in employees.OrderBy(e => e.Number, StringComparer.Ordinal))
        {
            void Add(string issue) => issues.Add($"{e.Number}: {issue}");
            string Others(ILookup<string, RunEmployee> lookup, string key) => string.Join(", ", lookup[key].Where(o => o.Number != e.Number).Select(o => o.Number));

            if (e.PaymentMethod == PayMethod.BankTransfer && string.IsNullOrWhiteSpace(e.BankAccount))
            {
                Add("paid by bank transfer but has no bank account");
            }
            else if (e.PaymentMethod == PayMethod.MobileMoney && string.IsNullOrWhiteSpace(e.MobileNumber))
            {
                Add("paid by mobile money but has no wallet number");
            }

            if (e.PaymentMethod == PayMethod.BankTransfer && Key(e.BankAccount) is string bank && banks[bank].Count() > 1)
            {
                Add($"bank account shared with {Others(banks, bank)}");
            }

            if (e.PaymentMethod == PayMethod.MobileMoney && Key(e.MobileNumber) is string wallet && wallets[wallet].Count() > 1)
            {
                Add($"wallet shared with {Others(wallets, wallet)}");
            }

            if (Key(e.NationalId) is string id && ids[id].Count() > 1)
            {
                Add($"national ID shared with {Others(ids, id)}");
            }

            if (string.IsNullOrWhiteSpace(e.TaxNumber))
            {
                Add("no tax number");
            }

            if (e.Status == EmployeeStatus.Terminated || e.TerminationDate is DateOnly left && left < payDate.AddDays(1 - payDate.Day))
            {
                Add($"terminated{(e.TerminationDate is DateOnly when ? $" on {when:yyyy-MM-dd}" : string.Empty)} but still in the run");
            }

            if (e.ContractEndDate is DateOnly end && end < payDate)
            {
                Add($"contract expired on {end:yyyy-MM-dd}");
            }

            if (e.GradeMinimum is decimal min && e.BasicSalary < min)
            {
                Add($"basic {e.BasicSalary:N2} is below the {e.Grade} minimum of {min:N2}");
            }
            else if (e.GradeMaximum is decimal max && e.BasicSalary > max)
            {
                Add($"basic {e.BasicSalary:N2} is above the {e.Grade} maximum of {max:N2}");
            }

            foreach ((string type, decimal balance) in e.NegativeLeave)
            {
                Add($"{type} leave balance is negative ({balance:0.##} days)");
            }

            if (e.BankDetailsChangedOn is DateTimeOffset changed)
            {
                Add($"bank details changed on {changed:yyyy-MM-dd} since the last run - confirm before paying");
            }
        }

        HashSet<string> paying = new(employees.Select(e => e.Number), StringComparer.OrdinalIgnoreCase);
        issues.AddRange(previousRunNumbers.Where(n => !paying.Contains(n)).Order(StringComparer.Ordinal).Select(n => $"{n}: in the previous run but not in this one"));
        return issues;
    }
}

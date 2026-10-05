using One4All.Core.Payroll;

namespace One4All.Application.Payroll;

/// <summary>What the checks need to know about an employee.</summary>
/// <param name="Id">The employee.</param>
/// <param name="Number">The employee number.</param>
/// <param name="Name">The name.</param>
/// <param name="NationalId">The national ID, if captured.</param>
/// <param name="TaxNumber">The tax number, if captured.</param>
/// <param name="DateOfBirth">The date of birth, if captured.</param>
/// <param name="BasicSalary">The basic salary.</param>
/// <param name="PaymentMethod">How net pay is paid.</param>
/// <param name="BankAccount">The bank account, if captured.</param>
/// <param name="MobileNumber">The mobile-money wallet, if captured.</param>
/// <param name="EngagementDate">When they started.</param>
/// <param name="LeaveTakenDays">Leave days ever taken.</param>
public sealed record EmployeeFacts(long Id, string Number, string Name, string? NationalId, string? TaxNumber, DateOnly? DateOfBirth, decimal BasicSalary, PayMethod PaymentMethod,
    string? BankAccount, string? MobileNumber, DateOnly EngagementDate, decimal LeaveTakenDays);

/// <summary>
/// Pre-payroll validation and ghost-worker indicators (brief §14 PR4, PR68). Validation flags what stops a clean payment
/// or a statutory return: no national ID or tax number, a bank transfer without an account, mobile money without a wallet,
/// no date of birth, a zero basic, an employee under 16. Ghost-worker signals flag records that share a bank account,
/// wallet or national ID with another, have no ID at all, or have never taken leave after more than a year — each a
/// reason to verify the person exists, not proof. Pure and unit-tested.
/// </summary>
public static class EmployeeChecks
{
    /// <summary>The validation issues of one employee on a pay date.</summary>
    /// <param name="employee">The employee.</param>
    /// <param name="payDate">The pay date.</param>
    /// <returns>The issues, none when the record is clean.</returns>
    public static IReadOnlyList<string> Issues(EmployeeFacts employee, DateOnly payDate)
    {
        List<string> issues = [];
        if (string.IsNullOrWhiteSpace(employee.NationalId))
        {
            issues.Add("No national ID");
        }

        if (string.IsNullOrWhiteSpace(employee.TaxNumber))
        {
            issues.Add("No tax number");
        }

        if (employee.PaymentMethod == PayMethod.BankTransfer && string.IsNullOrWhiteSpace(employee.BankAccount))
        {
            issues.Add("Bank transfer without an account number");
        }

        if (employee.PaymentMethod == PayMethod.MobileMoney && string.IsNullOrWhiteSpace(employee.MobileNumber))
        {
            issues.Add("Mobile money without a wallet number");
        }

        if (employee.DateOfBirth is null)
        {
            issues.Add("No date of birth");
        }
        else if (employee.DateOfBirth.Value.AddYears(16) > payDate)
        {
            issues.Add("Under 16 on the pay date");
        }

        if (employee.BasicSalary <= 0m)
        {
            issues.Add("Basic salary is zero");
        }

        return issues;
    }

    /// <summary>The ghost-worker signals across a payroll, by employee.</summary>
    /// <param name="employees">The active employees.</param>
    /// <param name="asAt">The date.</param>
    /// <returns>Each employee with at least one signal, and the signals.</returns>
    public static IReadOnlyDictionary<long, IReadOnlyList<string>> GhostSignals(IReadOnlyList<EmployeeFacts> employees, DateOnly asAt)
    {
        static string? Key(string? value) => string.IsNullOrWhiteSpace(value) ? null : new string([.. value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
        ILookup<string, EmployeeFacts> Shared(Func<EmployeeFacts, string?> selector)
            => employees.Where(e => Key(selector(e)) is not null).ToLookup(e => Key(selector(e))!);
        ILookup<string, EmployeeFacts> banks = Shared(e => e.PaymentMethod == PayMethod.BankTransfer ? e.BankAccount : null);
        ILookup<string, EmployeeFacts> wallets = Shared(e => e.PaymentMethod == PayMethod.MobileMoney ? e.MobileNumber : null);
        ILookup<string, EmployeeFacts> ids = Shared(e => e.NationalId);
        Dictionary<long, IReadOnlyList<string>> signals = [];
        foreach (EmployeeFacts employee in employees)
        {
            List<string> found = [];
            void Duplicate(ILookup<string, EmployeeFacts> lookup, string? value, string what)
            {
                if (Key(value) is string key && lookup[key].Count() > 1)
                {
                    found.Add($"{what} shared with {string.Join(", ", lookup[key].Where(o => o.Id != employee.Id).Select(o => o.Number))}");
                }
            }

            Duplicate(banks, employee.PaymentMethod == PayMethod.BankTransfer ? employee.BankAccount : null, "Bank account");
            Duplicate(wallets, employee.PaymentMethod == PayMethod.MobileMoney ? employee.MobileNumber : null, "Wallet");
            Duplicate(ids, employee.NationalId, "National ID");
            if (string.IsNullOrWhiteSpace(employee.NationalId))
            {
                found.Add("No national ID");
            }

            if (employee.LeaveTakenDays == 0m && employee.EngagementDate.AddYears(1) <= asAt)
            {
                found.Add("No leave taken in over a year");
            }

            if (found.Count > 0)
            {
                signals[employee.Id] = found;
            }
        }

        return signals;
    }
}

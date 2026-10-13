using System.Globalization;
using System.Text;

namespace One4All.Application.Onboarding;

/// <summary>
/// The CSV templates for every master-data and opening-balance import (brief §10 data migration "templates"): each kind's header row with the
/// required columns first, then the optional ones, and an example row, so a migration team fills in the right columns before the first preview.
/// </summary>
public static class ImportTemplates
{
    private static readonly Dictionary<string, (string Header, string Example)> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Accounts"] = ("code,name,type,category,manual,budget", "6150,Motor Vehicle Expenses,Expense,Administration,true,true"),
        ["Warehouses"] = ("code,name", "MAIN,Main Store"),
        ["Customers"] = ("code,name,currency,credit_limit", "CUST001,Acme Trading,USD,5000"),
        ["Suppliers"] = ("code,name,currency", "SUP001,Office Supplies Ltd,USD"),
        ["Items"] = ("code,description,type,revenue_account,cost_account,category", "PAPER-A4,A4 paper ream,Stock,4000,5000,Stationery"),
        ["Stock"] = ("item,warehouse,quantity,unit_cost", "PAPER-A4,MAIN,120,4.50"),
        ["Receivables"] = ("trader,document,date,amount", "CUST001,INV-0991,2026-02-28,1250.00"),
        ["Payables"] = ("trader,document,date,amount", "SUP001,SI-4410,2026-02-20,-380.00"),
        ["TrialBalance"] = ("account,debit,credit", "1100,25000.00,0"),
        ["FixedAssets"] = ("asset_number,description,class,acquired,cost,accumulated,life_months,cost_account,accum_account,expense_account,residual,method,rate",
            "FA-0001,Toyota Hilux,VEHICLES,2024-03-01,42000,14000,60,1650,1651,6800,5000,StraightLine,"),
        ["Employees"] = ("employee_number,name,basic_salary,engaged,national_id,department,cost_centre,grade", "E001,Tendai Moyo,1200,2023-01-09,63-123456A00,FIN,ADM,B2"),
        ["StaffLoans"] = ("employee_number,reference,outstanding,instalment,loan_account", "E001,LOAN-7,600,100,1270"),
        ["PayrollYtd"] = ("employee_number,tax_year,code,amount", "E001,2026,TAXABLE,2400"),
        ["Properties"] = ("stand,ward,ratepayer,valuation,size,connected,services", "STD-1001,Ward 5,CUST001,85000,450,2015-06-01,Water:WAT1;Refuse:REF1"),
        ["Meters"] = ("serial,stand,service,reading,date,digits,multiplier", "WM-88213,STD-1001,Water,1534,2026-02-28,5,1"),
        ["Students"] = ("admission,name,grade,guardian,boarder,admitted", "ADM2026-001,Rudo Ncube,Form 1,CUST001,false,2026-01-12"),
        ["Stands"] = ("stand,parcel,size,cost,scheme,status", "ST-0001,PARCEL-A,300,1500,Scheme 1,Available"),
        ["RentalUnits"] = ("code,description,type,rooms,rent,rent_account,vacant_since,suburb,max_occupants", "FLAT-12,Flat 12 Block C,Flat,2,180,4110,,Mabvuku,4"),
        ["Budget"] = ("account,period,amount,cost_centre,quantity", "6100,1,2500,ADM,"),
    };

    /// <summary>Gets the import kinds there are templates for.</summary>
    public static IReadOnlyList<string> Kinds => [.. Templates.Keys];

    /// <summary>The template for an import: its header row and an example row.</summary>
    /// <param name="kind">The import (Accounts, Customers, Items…).</param>
    /// <returns>The CSV text, or null for an unknown kind.</returns>
    public static string? For(string kind) => Templates.TryGetValue(kind, out var template) ? template.Header + "\r\n" + template.Example + "\r\n" : null;

    /// <summary>Writes rows under a header as CSV, quoting values with commas, quotes or line breaks.</summary>
    /// <param name="header">The header row.</param>
    /// <param name="rows">The rows' values.</param>
    /// <returns>The CSV text.</returns>
    public static string Csv(string header, IEnumerable<IEnumerable<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        StringBuilder sb = new();
        sb.Append(header).Append("\r\n");
        foreach (IEnumerable<object?> row in rows)
        {
            sb.Append(string.Join(',', row.Select(Cell))).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string Cell(object? value)
    {
        string text = value switch
        {
            null => string.Empty,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.####", CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }
}

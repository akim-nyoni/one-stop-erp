using System.Data;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.Operations;

/// <summary>How a field on an operation form is entered.</summary>
public enum FieldKind
{
    /// <summary>Free text (codes, references, notes).</summary>
    Text,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A date.</summary>
    Date,

    /// <summary>A yes / no tick.</summary>
    Flag,

    /// <summary>One of a fixed list.</summary>
    Choice,
}

/// <summary>A field on an operation's form.</summary>
/// <param name="Name">The key the operation reads it by.</param>
/// <param name="Label">The label shown.</param>
/// <param name="Kind">How it is entered.</param>
/// <param name="Default">The starting value.</param>
/// <param name="Required">Whether it must be filled.</param>
/// <param name="Choices">The options, for a choice.</param>
/// <param name="ChoicesFrom">Loads the options when the form opens (the company's currencies, say), for a choice.</param>
public sealed record OperationField(string Name, string Label, FieldKind Kind = FieldKind.Text, string? Default = null, bool Required = true, IReadOnlyList<string>? Choices = null,
    Func<OperationContext, Task<IReadOnlyList<string>>>? ChoicesFrom = null);

/// <summary>What an operation runs against: a scoped service provider, the company and the signed-in user.</summary>
/// <param name="Services">The scope's services.</param>
/// <param name="CompanyId">The company.</param>
/// <param name="UserId">The signed-in user.</param>
public sealed record OperationContext(IServiceProvider Services, long CompanyId, long UserId)
{
    /// <summary>Gets the page of the live list being shown (0: the first); lists read one page at a time from the database.</summary>
    public int ListPage { get; init; }

    /// <summary>Gets the scope's database context.</summary>
    public One4AllDbContext Db => Services.GetRequiredService<One4AllDbContext>();

    /// <summary>Gets a service from the scope.</summary>
    /// <typeparam name="T">The service.</typeparam>
    /// <returns>The service.</returns>
    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();
}

/// <summary>
/// One action on the operations console: its module, title, form fields, what it does with them, and an optional live list
/// (open tickets, transfers in transit…) shown underneath.
/// </summary>
public sealed class Operation
{
    /// <summary>Gets the module it belongs to (menu grouping).</summary>
    public required string Module { get; init; }

    /// <summary>Gets the title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets what it does.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the form fields.</summary>
    public IReadOnlyList<OperationField> Fields { get; init; } = [];

    /// <summary>Gets the action: returns the message (or document text) to show.</summary>
    public required Func<OperationContext, OperationInput, Task<string>> Run { get; init; }

    /// <summary>Gets the live list shown under the form.</summary>
    public Func<OperationContext, Task<DataTable>>? List { get; init; }

    /// <summary>Gets the list's caption.</summary>
    public string? ListTitle { get; init; }

    /// <summary>Gets a value indicating whether the operation has a list (shown in its own window).</summary>
    public bool HasList => List is not null;

    /// <summary>Gets a value indicating whether the result is a printable document (shown in full, monospaced).</summary>
    public bool IsDocument { get; init; }

    /// <summary>Gets where the action sits in the menus when its title would place it wrongly (null: worked out from the title).</summary>
    public Application.Navigation.MenuArea? Area { get; init; }

    /// <summary>Gets the menu the action sits under (Maintenance, Transactions, Enquiries or Utilities).</summary>
    public Application.Navigation.MenuArea MenuArea => Area ?? Application.Navigation.MenuAreas.Classify(Title);
}

/// <summary>Raised by an operation when it cannot proceed; the message is shown to the user.</summary>
public sealed class OperationException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong.</param>
    public OperationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public OperationException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="inner">The cause.</param>
    public OperationException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>The values typed into an operation's form, read by name and parsed.</summary>
public sealed class OperationInput
{
    private readonly IReadOnlyDictionary<string, string?> _values;
    private readonly IReadOnlyDictionary<string, string> _labels;

    /// <summary>Creates the input.</summary>
    /// <param name="values">The raw values by field name.</param>
    /// <param name="labels">The field labels by name (for messages).</param>
    public OperationInput(IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, string> labels)
    {
        _values = values;
        _labels = labels;
    }

    private string Label(string name) => _labels.GetValueOrDefault(name, name);

    /// <summary>A text value, or null when blank.</summary>
    /// <param name="name">The field.</param>
    /// <returns>The trimmed text.</returns>
    public string? Optional(string name) => _values.TryGetValue(name, out string? v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    /// <summary>A required text value.</summary>
    /// <param name="name">The field.</param>
    /// <returns>The trimmed text.</returns>
    public string Text(string name) => Optional(name) ?? throw new OperationException($"{Label(name)} is required.");

    /// <summary>A number, or null when blank.</summary>
    /// <param name="name">The field.</param>
    /// <returns>The number.</returns>
    public decimal? OptionalNumber(string name)
        => Optional(name) is not string text ? null
            : decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) || decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
                ? value : throw new OperationException($"{Label(name)} must be a number.");

    /// <summary>A required number.</summary>
    /// <param name="name">The field.</param>
    /// <returns>The number.</returns>
    public decimal Number(string name) => OptionalNumber(name) ?? throw new OperationException($"{Label(name)} is required.");

    /// <summary>A whole number.</summary>
    /// <param name="name">The field.</param>
    /// <returns>The number.</returns>
    public int Whole(string name) => (int)Number(name);

    /// <summary>A date, or null when blank.</summary>
    /// <param name="name">The field.</param>
    /// <returns>The date.</returns>
    public DateOnly? OptionalDate(string name)
        => Optional(name) is not string text ? null
            : DateOnly.TryParse(text, CultureInfo.InvariantCulture, out DateOnly date) || DateOnly.TryParse(text, CultureInfo.CurrentCulture, out date)
                ? date : throw new OperationException($"{Label(name)} must be a date.");

    /// <summary>A required date.</summary>
    /// <param name="name">The field.</param>
    /// <returns>The date.</returns>
    public DateOnly Date(string name) => OptionalDate(name) ?? throw new OperationException($"{Label(name)} is required.");

    /// <summary>A yes / no value.</summary>
    /// <param name="name">The field.</param>
    /// <returns>True when ticked.</returns>
    public bool Flag(string name) => string.Equals(Optional(name), "True", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses a "CODE:qty[:date]; CODE:qty" list.</summary>
    /// <param name="name">The field.</param>
    /// <returns>The entries.</returns>
    public IReadOnlyList<(string Code, decimal Quantity, DateOnly? Date)> Pairs(string name)
    {
        List<(string, decimal, DateOnly?)> entries = [];
        foreach (string part in (Optional(name) ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
            if (bits.Length < 2 || !decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal qty))
            {
                throw new OperationException($"{Label(name)}: '{part}' should look like CODE:quantity.");
            }

            DateOnly? date = bits.Length > 2 && DateOnly.TryParse(bits[2], CultureInfo.InvariantCulture, out DateOnly d) ? d : null;
            entries.Add((bits[0], qty, date));
        }

        return entries;
    }
}

/// <summary>Helpers shared by the operations: result checks, record look-ups by code, and list building.</summary>
public static class Ops
{
    /// <summary>One page of a list query, read from the database (the console's page, of the list's page size).</summary>
    /// <typeparam name="T">The row.</typeparam>
    /// <param name="query">The ordered query.</param>
    /// <param name="c">The context (its list page).</param>
    /// <param name="size">The page size.</param>
    /// <returns>The page.</returns>
    public static IQueryable<T> Page<T>(this IQueryable<T> query, OperationContext c, int size) => query.Skip(Math.Max(0, c.ListPage) * size).Take(size);

    /// <summary>One page of rows already read.</summary>
    /// <typeparam name="T">The row.</typeparam>
    /// <param name="rows">The rows.</param>
    /// <param name="c">The context (its list page).</param>
    /// <param name="size">The page size.</param>
    /// <returns>The page.</returns>
    public static IEnumerable<T> Page<T>(this IEnumerable<T> rows, OperationContext c, int size) => rows.Skip(Math.Max(0, c.ListPage) * size).Take(size);

    /// <summary>The standard date format.</summary>
    /// <param name="date">The date.</param>
    /// <returns>yyyy-MM-dd.</returns>
    public static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Money format.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>Two decimals with separators.</returns>
    public static string M(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>Quantity format.</summary>
    /// <param name="quantity">The quantity.</param>
    /// <returns>Up to three decimals.</returns>
    public static string Q(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Throws the result's error, or returns the success message.</summary>
    /// <param name="result">The result.</param>
    /// <param name="success">The message on success.</param>
    /// <returns>The message.</returns>
    public static string Check(Result result, string success) => result.IsSuccess ? success : throw new OperationException(result.Error.Message);

    /// <summary>Throws the result's error, or returns its value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>The value.</returns>
    public static T Check<T>(Result<T> result) => result.IsSuccess ? result.Value : throw new OperationException(result.Error.Message);

    /// <summary>Throws the result's error, or returns the success message.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="success">The message on success.</param>
    /// <returns>The message.</returns>
    public static string Check<T>(Result<T> result, string success) => Check(result, _ => success);

    /// <summary>Throws the result's error, or describes its value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="success">Builds the message from the value.</param>
    /// <returns>The message.</returns>
    public static string Check<T>(Result<T> result, Func<T, string> success) => success(Check(result));

    /// <summary>Finds a record's id by a matching rule, or explains that it does not exist.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The set.</param>
    /// <param name="match">The rule.</param>
    /// <param name="id">The id selector.</param>
    /// <param name="what">What is looked for (for the message).</param>
    /// <param name="key">The value typed.</param>
    /// <returns>The id.</returns>
    public static async Task<long> FindAsync<T>(IQueryable<T> source, Expression<Func<T, bool>> match, Expression<Func<T, long>> id, string what, string key)
    {
        long found = await source.Where(match).Select(id).FirstOrDefaultAsync().ConfigureAwait(true);
        return found != 0 ? found : throw new OperationException($"No {what} '{key}'.");
    }

    /// <summary>Finds an item by code.</summary>
    /// <param name="c">The context.</param>
    /// <param name="code">The code.</param>
    /// <returns>The id.</returns>
    public static Task<long> ItemAsync(OperationContext c, string code)
        => FindAsync(c.Db.Items, i => i.CompanyId == c.CompanyId && i.Code.ToUpper() == code.ToUpper(), i => i.Id, "item", code);

    /// <summary>Finds a warehouse by code.</summary>
    /// <param name="c">The context.</param>
    /// <param name="code">The code.</param>
    /// <returns>The id.</returns>
    public static async Task<long> WarehouseAsync(OperationContext c, string code)
    {
        long id = await FindAsync(c.Db.Warehouses, w => w.CompanyId == c.CompanyId && w.Code.ToUpper() == code.ToUpper(), w => w.Id, "warehouse", code).ConfigureAwait(true);

        // Row-level scopes: a user limited to certain warehouses cannot work in others (brief §5.1).
        return await c.Get<Application.Abstractions.Security.IDataScopeService>().AllowsAsync(c.UserId, Core.Security.ScopeDimension.Warehouse, id).ConfigureAwait(true)
            ? id
            : throw new OperationException($"You have no access to warehouse {code.ToUpperInvariant()}.");
    }

    /// <summary>Finds a customer by code.</summary>
    /// <param name="c">The context.</param>
    /// <param name="code">The code.</param>
    /// <returns>The id.</returns>
    public static Task<long> CustomerAsync(OperationContext c, string code)
        => FindAsync(c.Db.Customers, x => x.CompanyId == c.CompanyId && x.Code.ToUpper() == code.ToUpper(), x => x.Id, "customer", code);

    /// <summary>Finds a supplier by code.</summary>
    /// <param name="c">The context.</param>
    /// <param name="code">The code.</param>
    /// <returns>The id.</returns>
    public static Task<long> SupplierAsync(OperationContext c, string code)
        => FindAsync(c.Db.Suppliers, x => x.CompanyId == c.CompanyId && x.Code.ToUpper() == code.ToUpper(), x => x.Id, "supplier", code);

    /// <summary>Finds a contract by number.</summary>
    /// <param name="c">The context.</param>
    /// <param name="number">The number.</param>
    /// <returns>The id.</returns>
    public static Task<long> ContractAsync(OperationContext c, string number)
        => FindAsync(c.Db.JobContracts, x => x.CompanyId == c.CompanyId && x.ContractNumber.ToUpper() == number.ToUpper(), x => x.Id, "contract", number);

    /// <summary>Finds an employee by number.</summary>
    /// <param name="c">The context.</param>
    /// <param name="number">The number.</param>
    /// <returns>The id.</returns>
    public static Task<long> EmployeeAsync(OperationContext c, string number)
        => FindAsync(c.Db.Employees, x => x.CompanyId == c.CompanyId && x.EmployeeNumber.ToUpper() == number.ToUpper(), x => x.Id, "employee", number);

    /// <summary>Finds a user by user name (for authorisers).</summary>
    /// <param name="c">The context.</param>
    /// <param name="name">The user name.</param>
    /// <returns>The id.</returns>
    public static Task<long> UserAsync(OperationContext c, string name)
        => FindAsync(c.Db.Users, u => u.NormalizedUsername == name.ToUpper(), u => u.Id, "user", name);

    /// <summary>Finds a user by name when one was given.</summary>
    /// <param name="c">The context.</param>
    /// <param name="name">The user name, or null.</param>
    /// <returns>The id, or null.</returns>
    public static async Task<long?> OptionalUserAsync(OperationContext c, string? name) => name is null ? null : await UserAsync(c, name).ConfigureAwait(true);

    /// <summary>Builds a list table from rows.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="rows">The rows.</param>
    /// <param name="columns">Each column's header and value.</param>
    /// <returns>The table.</returns>
    public static DataTable Table<T>(IEnumerable<T> rows, params (string Header, Func<T, object?> Value)[] columns)
    {
        DataTable table = new() { Locale = CultureInfo.InvariantCulture };
        foreach ((string header, _) in columns)
        {
            table.Columns.Add(header, typeof(string));
        }

        foreach (T row in rows)
        {
            table.Rows.Add([.. columns.Select(c => Convert.ToString(c.Value(row), CultureInfo.InvariantCulture) ?? string.Empty)]);
        }

        return table;
    }

    /// <summary>A field for a date that defaults to today.</summary>
    /// <param name="name">The key.</param>
    /// <param name="label">The label.</param>
    /// <returns>The field.</returns>
    public static OperationField Today(string name = "Date", string label = "Date") => new(name, label, FieldKind.Date, D(DateOnly.FromDateTime(DateTime.Today)));

    /// <summary>An optional text field.</summary>
    /// <param name="name">The key.</param>
    /// <param name="label">The label.</param>
    /// <param name="value">A default.</param>
    /// <returns>The field.</returns>
    public static OperationField Opt(string name, string label, string? value = null) => new(name, label, FieldKind.Text, value, Required: false);

    /// <summary>A required text field.</summary>
    /// <param name="name">The key.</param>
    /// <param name="label">The label.</param>
    /// <param name="value">A default.</param>
    /// <returns>The field.</returns>
    public static OperationField Req(string name, string label, string? value = null) => new(name, label, FieldKind.Text, value);

    /// <summary>A required number field.</summary>
    /// <param name="name">The key.</param>
    /// <param name="label">The label.</param>
    /// <param name="value">A default.</param>
    /// <returns>The field.</returns>
    public static OperationField Num(string name, string label, string? value = null) => new(name, label, FieldKind.Number, value);

    /// <summary>An optional number field.</summary>
    /// <param name="name">The key.</param>
    /// <param name="label">The label.</param>
    /// <returns>The field.</returns>
    public static OperationField OptNum(string name, string label) => new(name, label, FieldKind.Number, Required: false);

    /// <summary>A tick field.</summary>
    /// <param name="name">The key.</param>
    /// <param name="label">The label.</param>
    /// <param name="ticked">Ticked by default.</param>
    /// <returns>The field.</returns>
    public static OperationField Tick(string name, string label, bool ticked = false) => new(name, label, FieldKind.Flag, ticked ? "True" : "False", Required: false);

    /// <summary>A choice field.</summary>
    /// <param name="name">The key.</param>
    /// <param name="label">The label.</param>
    /// <param name="choices">The options (the first is the default).</param>
    /// <returns>The field.</returns>
    public static OperationField Pick(string name, string label, params string[] choices) => new(name, label, FieldKind.Choice, choices[0], Choices: choices);

    /// <summary>The code part of a lookup choice ("C001 — Tendai Moyo" → "C001"); other text as it is.</summary>
    /// <param name="value">The chosen or typed value.</param>
    /// <returns>The code.</returns>
    public static string? CodeOf(string? value)
    {
        if (value is null)
        {
            return null;
        }

        int dash = value.IndexOf(" — ", StringComparison.Ordinal);
        return dash > 0 ? value[..dash].Trim() : value;
    }

    /// <summary>
    /// A lookup: the company's records listed as "CODE — Name" to pick from (or the code typed), so nobody has to remember codes. The operation
    /// receives the code alone. Optional lookups offer a blank first.
    /// </summary>
    /// <param name="name">The field name.</param>
    /// <param name="label">The label.</param>
    /// <param name="required">Whether one must be chosen.</param>
    /// <param name="rows">Reads the codes and names, in the order to show.</param>
    /// <returns>The field.</returns>
    public static OperationField Lookup(string name, string label, bool required, Func<OperationContext, Task<List<(string Code, string Name)>>> rows)
        => new(name, label, FieldKind.Choice, Required: required, Choices: [], ChoicesFrom: async c =>
        {
            List<(string Code, string Name)> found = await rows(c).ConfigureAwait(true);
            IEnumerable<string> shown = found.Select(r => string.IsNullOrWhiteSpace(r.Name) || r.Name == r.Code ? r.Code : $"{r.Code} — {r.Name}");
            return required ? [.. shown] : [string.Empty, .. shown];
        });

    /// <summary>A customer lookup (code — name).</summary>
    /// <param name="name">The field name.</param>
    /// <param name="label">The label.</param>
    /// <param name="required">Whether one must be chosen.</param>
    /// <returns>The field.</returns>
    public static OperationField CustomerPick(string name, string label, bool required = true) => Lookup(name, label, required,
        async c => (await c.Db.Customers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).Take(2000).Select(x => new { x.Code, x.Name }).ToListAsync().ConfigureAwait(true))
            .Select(x => (x.Code, x.Name)).ToList());

    /// <summary>A supplier lookup (code — name).</summary>
    /// <param name="name">The field name.</param>
    /// <param name="label">The label.</param>
    /// <param name="required">Whether one must be chosen.</param>
    /// <returns>The field.</returns>
    public static OperationField SupplierPick(string name, string label, bool required = true) => Lookup(name, label, required,
        async c => (await c.Db.Suppliers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).Take(2000).Select(x => new { x.Code, x.Name }).ToListAsync().ConfigureAwait(true))
            .Select(x => (x.Code, x.Name)).ToList());

    /// <summary>A product / item lookup (code — description).</summary>
    /// <param name="name">The field name.</param>
    /// <param name="label">The label.</param>
    /// <param name="required">Whether one must be chosen.</param>
    /// <returns>The field.</returns>
    public static OperationField ItemPick(string name, string label, bool required = true) => Lookup(name, label, required,
        async c => (await c.Db.Items.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.IsActive).OrderBy(x => x.Code).Take(2000).Select(x => new { x.Code, x.Description }).ToListAsync().ConfigureAwait(true))
            .Select(x => (x.Code, x.Description)).ToList());

    /// <summary>A warehouse / store lookup (code — name).</summary>
    /// <param name="name">The field name.</param>
    /// <param name="label">The label.</param>
    /// <param name="required">Whether one must be chosen.</param>
    /// <returns>The field.</returns>
    public static OperationField WarehousePick(string name, string label, bool required = true) => Lookup(name, label, required,
        async c => (await c.Db.Warehouses.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).Select(x => new { x.Code, x.Name }).ToListAsync().ConfigureAwait(true))
            .Select(x => (x.Code, x.Name)).ToList());

    /// <summary>An employee lookup (number — name).</summary>
    /// <param name="name">The field name.</param>
    /// <param name="label">The label.</param>
    /// <param name="required">Whether one must be chosen.</param>
    /// <returns>The field.</returns>
    public static OperationField EmployeePick(string name, string label, bool required = true) => Lookup(name, label, required,
        async c => (await c.Db.Employees.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.EmployeeNumber).Take(2000).Select(x => new { x.EmployeeNumber, x.FullName }).ToListAsync().ConfigureAwait(true))
            .Select(x => (x.EmployeeNumber, x.FullName)).ToList());

    /// <summary>A currency dropdown: the company's base currency first, then every currency it has exchange rates for.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="label">The label.</param>
    /// <returns>The field.</returns>
    public static OperationField CurrencyPick(string name, string label) => new(name, label, FieldKind.Choice, Required: false, Choices: [], ChoicesFrom: async c =>
    {
        Core.Accounting.CurrencyCode baseCurrency = await c.Db.Companies.Where(x => x.Id == c.CompanyId).Select(x => x.BaseCurrency).FirstAsync().ConfigureAwait(true);
        List<Core.Accounting.CurrencyCode> others = await c.Db.ExchangeRates.Where(r => r.ToCurrency == baseCurrency).Select(r => r.FromCurrency).Distinct().ToListAsync().ConfigureAwait(true);
        return [baseCurrency.Value, .. others.Select(o => o.Value).Where(o => o != baseCurrency.Value).Order(StringComparer.Ordinal)];
    });
}

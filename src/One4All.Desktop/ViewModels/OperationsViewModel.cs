using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Desktop.Operations;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>One row of a document's line grid: the item (picked from the list or typed), the quantity, the price and one more value.</summary>
public sealed partial class OperationLineVm : ObservableObject
{
    /// <summary>The item (CODE or "CODE - description").</summary>
    [ObservableProperty]
    private string? _item;

    /// <summary>The quantity (with its unit after it, "2 BOX", where the document allows).</summary>
    [ObservableProperty]
    private string? _quantity;

    /// <summary>The price (blank: the price list's).</summary>
    [ObservableProperty]
    private string? _price;

    /// <summary>The one further value the document takes (a tax code, say).</summary>
    [ObservableProperty]
    private string? _extra;

    /// <summary>Gets whether anything is on the row.</summary>
    public bool IsFilled => !string.IsNullOrWhiteSpace(Item) && !string.IsNullOrWhiteSpace(Quantity);
}

/// <summary>One field on the operation form, bound two-way.</summary>
public sealed partial class OperationFieldVm : ObservableObject
{
    /// <summary>Creates the field.</summary>
    /// <param name="field">The definition.</param>
    public OperationFieldVm(OperationField field)
    {
        Field = field;
        _value = field.Default;
        _dateValue = field.Kind == FieldKind.Date && DateTime.TryParse(field.Default, System.Globalization.CultureInfo.InvariantCulture, out DateTime d) ? d : null;
        _isTicked = field.Default == "True";
        foreach (string choice in field.Choices ?? [])
        {
            Choices.Add(choice);
        }
    }

    /// <summary>Gets the definition.</summary>
    public OperationField Field { get; }

    /// <summary>Gets the label (starred when required).</summary>
    public string Label => Field.Required && Field.Kind != FieldKind.Flag ? Field.Label + " *" : Field.Label;

    /// <summary>Gets a value indicating whether it is typed.</summary>
    public bool IsText => Field.Kind is FieldKind.Text or FieldKind.Number && !IsDateText && AutoLookup is null;

    /// <summary>Gets a value indicating whether it is a date (a date field, or a text field that asks for yyyy-MM-dd, picked on a calendar).</summary>
    public bool IsDate => Field.Kind == FieldKind.Date || IsDateText;

    /// <summary>Gets whether a typed field asks for a date (its label says yyyy-MM-dd): it is picked on a calendar instead.</summary>
    public bool IsDateText => Field.Kind == FieldKind.Text && Field.Label.Contains("yyyy-MM-dd", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets what a typed field names, when the company's own records can be offered for it: a ledger account, a customer, a supplier or a user
    /// (from its label); null for ordinary text. Such a field is picked from a list that can still be typed into.
    /// </summary>
    public string? AutoLookup
    {
        get
        {
            if (Field.Kind != FieldKind.Text || Field.Name == "Code" || IsDateText)
            {
                return null;
            }

            string label = Field.Label.ToLowerInvariant();

            // A currency is picked from the currency table; a list of currency-to-account pairs ("EUR:1120; ZAR:1130") stays typed.
            if (label.Contains("currency", StringComparison.Ordinal))
            {
                return label.Contains("account", StringComparison.Ordinal) || label.Contains(':', StringComparison.Ordinal) || label.Contains("amount", StringComparison.Ordinal)
                    || label.Contains("rate", StringComparison.Ordinal) || label.Contains("balance", StringComparison.Ordinal) ? null : "Currencies";
            }

            if (label.Contains("customer", StringComparison.Ordinal) && label.Contains("code", StringComparison.Ordinal))
            {
                return "Customers";
            }

            if (label.Contains("supplier", StringComparison.Ordinal) && label.Contains("code", StringComparison.Ordinal))
            {
                return "Suppliers";
            }

            if (label.Contains("user name", StringComparison.Ordinal))
            {
                return "Users";
            }

            return label.Contains("account", StringComparison.Ordinal) && !label.Contains("number", StringComparison.Ordinal) && !label.Contains("customer", StringComparison.Ordinal)
                && !label.Contains("supplier", StringComparison.Ordinal) && !label.Contains("account code (up to", StringComparison.Ordinal) ? "Accounts" : null;
        }
    }

    /// <summary>Gets whether the field shows a list to pick from (a choice, or a typed field offered the company's records).</summary>
    public bool ShowsList => IsChoice || AutoLookup is not null;

    /// <summary>Whether the field applies to the step chosen on the form (fields marked for other steps are hidden).</summary>
    [ObservableProperty]
    private bool _isShown = true;

    /// <summary>
    /// Shows the field only for the steps its label names in brackets ("Customer code (take)", "Amount (take) / NRV (cancel)"); a field whose
    /// brackets name no step applies to every step.
    /// </summary>
    /// <param name="step">The step chosen.</param>
    /// <param name="steps">The form's steps.</param>
    public void ShowFor(string? step, IReadOnlyList<string> steps)
    {
        if (Field.Name == "Step" || string.IsNullOrWhiteSpace(step) || steps.Count == 0)
        {
            IsShown = true;
            return;
        }

        List<string> named = [];
        foreach (System.Text.RegularExpressions.Match hint in System.Text.RegularExpressions.Regex.Matches(Field.Label, @"\(([^()]*)\)"))
        {
            foreach (string token in hint.Groups[1].Value.Split(['/', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string word = token.Split(':')[0].Trim();
                named.AddRange(steps.Where(s => s.Equals(word, StringComparison.OrdinalIgnoreCase)
                    || (word.Length >= 4 && s.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                    || (word.Length >= 4 && s.Split(' ')[0].Equals(word, StringComparison.OrdinalIgnoreCase))));
            }
        }

        IsShown = named.Count == 0 || named.Contains(step, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets a value indicating whether it is a tick.</summary>
    public bool IsFlag => Field.Kind == FieldKind.Flag;

    /// <summary>Gets a value indicating whether it is a choice.</summary>
    public bool IsChoice => Field.Kind == FieldKind.Choice;

    /// <summary>Gets the choices (loaded from the database for a field that names where from).</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> Choices { get; } = [];

    /// <summary>The typed or chosen value.</summary>
    [ObservableProperty]
    private string? _value;

    /// <summary>The picked date.</summary>
    [ObservableProperty]
    private DateTime? _dateValue;

    /// <summary>The tick.</summary>
    [ObservableProperty]
    private bool _isTicked;

    /// <summary>The value as the operation reads it.</summary>
    /// <returns>The raw value.</returns>
    public string? Raw() => Field.Kind switch
    {
        FieldKind.Date => DateValue is DateTime d ? Ops.D(DateOnly.FromDateTime(d)) : null,
        FieldKind.Flag => IsTicked ? "True" : "False",
        FieldKind.Choice => Ops.CodeOf(Value),
        _ when IsDateText => DateValue is DateTime picked ? Ops.D(DateOnly.FromDateTime(picked)) : Value,
        _ when AutoLookup is not null => Ops.CodeOf(Value),
        _ when IsLines && Lines.Any(l => l.IsFilled) => LinesText(),
        _ => Value,
    };

    /// <summary>Gets whether the field is a document's lines (ITEM:qty...), entered on a line grid as well as typed.</summary>
    public bool IsLines => Field.Kind == FieldKind.Text && Field.Name == "Lines" && Field.Label.Contains("ITEM:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the width the field takes on the form (a line grid is wider).</summary>
    public double Width => IsLines ? 560 : 250;

    /// <summary>Gets the line grid's rows.</summary>
    public ObservableCollection<OperationLineVm> Lines { get; } = [new()];

    /// <summary>Gets the items to pick on the line grid.</summary>
    public ObservableCollection<string> ItemChoices { get; } = [];

    /// <summary>Adds a row to the line grid.</summary>
    [RelayCommand]
    private void AddLine() => Lines.Add(new OperationLineVm());

    /// <summary>Removes a row from the line grid.</summary>
    /// <param name="line">The row.</param>
    [RelayCommand]
    private void RemoveLine(OperationLineVm? line)
    {
        if (line is not null && Lines.Count > 1)
        {
            Lines.Remove(line);
        }
    }

    // The grid written the way the document's text takes it: ITEM:qty@price where the label says so, else ITEM:qty[:price[:extra]].
    private string LinesText()
    {
        bool at = Field.Label.Contains("@price", StringComparison.OrdinalIgnoreCase);
        return string.Join("; ", Lines.Where(l => l.IsFilled).Select(l =>
        {
            string head = $"{Ops.CodeOf(l.Item)!.Trim()}:{l.Quantity!.Trim()}";
            string? price = string.IsNullOrWhiteSpace(l.Price) ? null : l.Price.Trim();
            string? extra = string.IsNullOrWhiteSpace(l.Extra) ? null : l.Extra.Trim();
            return at ? $"{head}@{price ?? "0"}" : extra is not null ? $"{head}:{price}:{extra}" : price is not null ? $"{head}:{price}" : head;
        }));
    }

    /// <summary>Gets a value indicating whether the choices are the company's records (typed codes are accepted too; a currency is only picked).</summary>
    public bool IsLookup => !IsCurrency && (Field.ChoicesFrom is not null || AutoLookup is not null);

    /// <summary>Gets whether the field is a currency: picked from the currency table, never typed.</summary>
    public bool IsCurrency => AutoLookup == "Currencies" || (Field.ChoicesFrom is not null && Field.Label.Contains("currency", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Backs the operations console: the module's actions on the left, the chosen action's form, result and live list on the right.
/// Each run opens its own scope and calls the tested application service through the <see cref="Operation"/>.
/// </summary>
public sealed partial class OperationsViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly long _userId;
    private long _companyId;
    private int _listPage;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="module">The module to open on.</param>
    /// <param name="area">The menu area to show (null: all the module's actions).</param>
    public OperationsViewModel(IServiceProvider services, long userId, string module, Application.Navigation.MenuArea? area = null)
    {
        _services = services;
        _userId = userId;
        Area = area;
        _module = OperationCatalogue.Modules.Contains(module) ? module : OperationCatalogue.Modules[0];
        ShowModule();
    }

    /// <summary>Creates the view model for one action, opened in its own window from the navigation.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="operation">The action.</param>
    /// <param name="title">The name the navigation shows for it.</param>
    /// <param name="context">Where it sits in the navigation ("HR &amp; Payroll › Leave").</param>
    public OperationsViewModel(IServiceProvider services, long userId, Operation operation, string? title = null, string? context = null)
    {
        _services = services;
        _userId = userId;
        _module = operation.Module;
        IsSingle = true;
        SingleTitle = title ?? operation.Title;
        SingleContext = context;
        Operations.Add(operation);
        Selected = operation;
    }

    /// <summary>Gets a value indicating whether the window shows one action (opened from the navigation) rather than a module's list of actions.</summary>
    public bool IsSingle { get; }

    /// <summary>Gets a value indicating whether the module's list of actions is shown.</summary>
    public bool ShowsActionList => !IsSingle;

    /// <summary>Gets the action's name when the window shows one action.</summary>
    public string? SingleTitle { get; }

    /// <summary>Gets where the action sits in the navigation, when opened from it.</summary>
    public string? SingleContext { get; }

    /// <summary>Gets the line under the heading: plain instructions for one screen, the console's own line otherwise.</summary>
    public string Subtitle => IsSingle
        ? (SingleContext is null ? string.Empty : SingleContext + "  -  ") + "Fill in the boxes (those marked * must be filled) and press OK. Cancel closes without saving."
        : Localization.UiText.T("Choose an action, fill the form and run it. Records are looked up by their codes; every rule is enforced by the posting services.");

    /// <summary>Gets the line under the list window's title: where the screen sits.</summary>
    public string ListContext => SingleContext ?? Localization.UiText.T(Module);

    /// <summary>Gets a value indicating whether there is output to show under the form.</summary>
    public bool HasResult => !string.IsNullOrEmpty(Result);

    /// <summary>Gets a value indicating whether the last run's output is a short message (one line) rather than a document or a table.</summary>
    public bool ResultIsShort => !HasResult || (!Result.Contains('\n') && Result.Length <= 200);

    /// <summary>Fills the form from a list row: each column whose name matches a field (ignoring case and spaces) sets that field, so Edit… starts from the record.</summary>
    /// <param name="row">The row.</param>
    public void Prefill(System.Data.DataRowView row)
    {
        ArgumentNullException.ThrowIfNull(row);
        static string Key(string text) => new([.. text.Where(char.IsLetterOrDigit)]);
        foreach (OperationFieldVm field in Fields)
        {
            System.Data.DataColumn? column = row.Row.Table.Columns.Cast<System.Data.DataColumn>().FirstOrDefault(c => string.Equals(Key(c.ColumnName), Key(field.Field.Name), StringComparison.OrdinalIgnoreCase)
                || string.Equals(Key(c.ColumnName), Key(field.Field.Label), StringComparison.OrdinalIgnoreCase));
            if (column is null || row[column.ColumnName] is DBNull or null)
            {
                continue;
            }

            string text = Convert.ToString(row[column.ColumnName], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            switch (field.Field.Kind)
            {
                case FieldKind.Date when DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out DateTime date):
                    field.DateValue = date;
                    break;
                case FieldKind.Flag:
                    field.IsTicked = text is "True" or "Yes" or "true";
                    break;
                case FieldKind.Text when field.IsDateText && DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out DateTime picked):
                    field.DateValue = picked;
                    break;
                case FieldKind.Choice:
                case FieldKind.Text when field.AutoLookup is not null:
                    field.Value = field.Choices.FirstOrDefault(choice => Ops.CodeOf(choice)?.Equals(text, StringComparison.OrdinalIgnoreCase) == true || choice.Equals(text, StringComparison.OrdinalIgnoreCase)) ?? text;
                    break;
                default:
                    field.Value = text.Replace(",", string.Empty).Length > 0 && field.Field.Kind == FieldKind.Number ? text.Replace(",", string.Empty) : text;
                    break;
            }
        }
    }

    /// <summary>Deletes the record with the key, when the operation allows it, and refreshes the list.</summary>
    /// <param name="key">The record's key (its code).</param>
    /// <returns>What happened.</returns>
    public async Task<string> DeleteAsync(string key)
    {
        if (Selected?.Delete is not Func<OperationContext, string, Task<string>> delete)
        {
            return "These records cannot be deleted.";
        }

        if (await RefusalAsync(Selected, true, Core.Security.OperationFlags.Delete).ConfigureAwait(true) is string refused)
        {
            StatusMessage = refused;
            return refused;
        }

        try
        {
            string done = await InScopeAsync(c => delete(c, key)).ConfigureAwait(true);
            StatusMessage = done;
            await RefreshAsync().ConfigureAwait(true);
            return done;
        }
        catch (OperationException ex)
        {
            StatusMessage = ex.Message;
            return ex.Message;
        }
    }

    /// <summary>Gets the list toolbar's Add… button text: the screen's own name.</summary>
    public string AddText => SingleTitle ?? Selected?.Title ?? "Add";

    /// <summary>Gets the OK button's text: OK for a form, Run for a document or an enquiry that shows output.</summary>
    public string OkText => Selected?.IsDocument == true || Selected?.HasList == false ? "▶  Run" : "✔  OK";

    /// <summary>Gets the button's text.</summary>
    public string RunText => IsSingle ? "✔  OK" : Localization.UiText.T("Run");

    /// <summary>Gets the modules.</summary>
    public IReadOnlyList<string> Modules => OperationCatalogue.Modules;

    /// <summary>Gets the menu area shown (null: all).</summary>
    public Application.Navigation.MenuArea? Area { get; }

    /// <summary>Gets the console's heading: the module, and the menu area when one was chosen.</summary>
    public string Heading => IsSingle ? SingleTitle! : Area is Application.Navigation.MenuArea area
        ? $"{Localization.UiText.T(Module)} - {Localization.UiText.T(area.ToString())}"
        : Localization.UiText.T(Module);

    /// <summary>Gets the module's operations.</summary>
    public ObservableCollection<Operation> Operations { get; } = [];

    /// <summary>Gets the selected operation's fields.</summary>
    public ObservableCollection<OperationFieldVm> Fields { get; } = [];

    /// <summary>The module shown.</summary>
    [ObservableProperty]
    private string _module;

    /// <summary>The chosen operation.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(RefreshCommand), nameof(PreviousPageCommand), nameof(NextPageCommand))]
    private Operation? _selected;

    /// <summary>The outcome of the last run (or the printed document).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult), nameof(ResultIsShort))]
    private string _result = string.Empty;

    /// <summary>Whether the last run failed.</summary>
    [ObservableProperty]
    private bool _failed;

    /// <summary>The live list.</summary>
    [ObservableProperty]
    private DataView? _list;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Choose an action.";

    /// <summary>True while running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(RefreshCommand), nameof(PreviousPageCommand), nameof(NextPageCommand))]
    private bool _isBusy;

    private bool CanRun => Selected is not null && !IsBusy;

    private bool CanRefresh => Selected?.List is not null && !IsBusy;

    partial void OnModuleChanged(string value) => ShowModule();

    partial void OnSelectedChanged(Operation? value)
    {
        _ = RecordUseAsync(value, "Open");
        Fields.Clear();
        foreach (OperationField field in value?.Fields ?? [])
        {
            Fields.Add(new OperationFieldVm(field));
        }

        // Fields marked for other steps are hidden as the step changes.
        if (Fields.FirstOrDefault(f => f.Field.Name == "Step" && f.Field.Kind == FieldKind.Choice) is OperationFieldVm stepField)
        {
            void Apply()
            {
                List<string> steps = [.. stepField.Choices];
                foreach (OperationFieldVm f in Fields)
                {
                    f.ShowFor(stepField.Value, steps);
                }
            }

            stepField.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(OperationFieldVm.Value))
                {
                    Apply();
                }
            };
            Apply();
        }

        Result = string.Empty;
        Failed = false;
        List = null;
        _listPage = 0;
        OnPropertyChanged(nameof(PageText));
        StatusMessage = value?.Description ?? "Choose an action.";
        _ = LoadChoicesAsync();
        if (value?.List is not null)
        {
            _ = RefreshAsync();
        }
    }

    // Choices that come from the database (the company's currencies…) are read when the form opens; the first is taken unless one is set.
    private async Task LoadChoicesAsync()
    {
        // Typed fields that name an account, customer, supplier or user offer the company's records (and can still be typed into).
        foreach (IGrouping<string, OperationFieldVm> kind in Fields.Where(f => f.AutoLookup is not null).GroupBy(f => f.AutoLookup!).ToList())
        {
            try
            {
                List<string> rows = await InScopeAsync(async c => kind.Key switch
                {
                    "Customers" => await c.Db.Customers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.IsActive).OrderBy(x => x.Code).Select(x => x.Code + " - " + x.Name).Take(5000)
                        .ToListAsync().ConfigureAwait(true),
                    "Suppliers" => await c.Db.Suppliers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.IsActive).OrderBy(x => x.Code).Select(x => x.Code + " - " + x.Name).Take(5000)
                        .ToListAsync().ConfigureAwait(true),
                    "Currencies" => (await c.Db.Currencies.AsNoTracking().ToListAsync().ConfigureAwait(true)).OrderBy(x => x.Code.Value, StringComparer.Ordinal)
                        .Select(x => x.Code.Value + " - " + x.Name).ToList(),
                    "Users" => await c.Db.Users.AsNoTracking().OrderBy(x => x.Username).Select(x => x.Username + " - " + x.DisplayName).Take(2000).ToListAsync().ConfigureAwait(true),
                    _ => await c.Db.Accounts.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.IsActive).OrderBy(x => x.AccountCode).Select(x => x.AccountCode + " - " + x.Name)
                        .ToListAsync().ConfigureAwait(true),
                }).ConfigureAwait(true);
                foreach (OperationFieldVm field in kind)
                {
                    string? keep = field.Value;
                    field.Choices.Clear();

                    // An optional currency can be left blank (the base, the customer's and so on), so the list starts with a blank entry.
                    if (field.IsCurrency && !field.Field.Required)
                    {
                        field.Choices.Add(string.Empty);
                    }

                    foreach (string row in rows)
                    {
                        field.Choices.Add(row);
                    }

                    // A picked-only list shows its default ("USD") as the matching row ("USD - US Dollar").
                    field.Value = field.IsCurrency && !string.IsNullOrWhiteSpace(keep)
                        ? rows.FirstOrDefault(r => r.StartsWith(keep.Trim() + " - ", StringComparison.OrdinalIgnoreCase)) ?? keep
                        : keep;
                }
            }
            catch (InvalidOperationException)
            {
                // No company yet: the fields are typed into instead.
            }
        }

        // A line grid picks from the company's active items.
        foreach (OperationFieldVm lines in Fields.Where(f => f.IsLines).ToList())
        {
            try
            {
                List<string> items = await InScopeAsync(async c => await c.Db.Items.AsNoTracking().Where(i => i.CompanyId == c.CompanyId && i.IsActive).OrderBy(i => i.Code)
                    .Select(i => i.Code + " - " + i.Description).Take(5000).ToListAsync().ConfigureAwait(true)).ConfigureAwait(true);
                lines.ItemChoices.Clear();
                foreach (string item in items)
                {
                    lines.ItemChoices.Add(item);
                }
            }
            catch (InvalidOperationException)
            {
                // No company yet: the grid is typed into instead.
            }
        }

        foreach (OperationFieldVm field in Fields.Where(f => f.Field.ChoicesFrom is not null).ToList())
        {
            try
            {
                IReadOnlyList<string> choices = await InScopeAsync(c => field.Field.ChoicesFrom!(c)).ConfigureAwait(true);
                field.Choices.Clear();
                foreach (string choice in choices)
                {
                    field.Choices.Add(choice);
                }

                field.Value ??= choices.FirstOrDefault();
            }
            catch (InvalidOperationException)
            {
                // No company yet: the field stays empty and the operation uses its default.
            }
        }
    }

    private void ShowModule()
    {
        Operations.Clear();
        OnPropertyChanged(nameof(Heading));
        foreach (Operation operation in OperationCatalogue.All().Where(o => o.Module == Module && (Area is null || o.MenuArea == Area)))
        {
            Operations.Add(operation);
        }

        Selected = Operations.FirstOrDefault();
    }

    // Screens used (brief §14 report 236): each open and run is logged for the user activity and screen usage reports; logging never gets in the way.
    private async Task RecordUseAsync(Operation? operation, string action)
    {
        if (operation is null || _userId == 0)
        {
            return;
        }

        try
        {
            await InScopeAsync(async c =>
            {
                c.Db.ScreenUses.Add(new Core.Security.ScreenUse(c.CompanyId, c.UserId, operation.Module, operation.Title, action, DateTimeOffset.UtcNow));
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return true;
            }).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Usage logging is best effort.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    // A list as the signed-in user may see it: cost, margin, salary and credit-limit columns are removed for users without the right to them
    // (brief §5.1 field-level sensitivity; the same rules the API and the report runner apply).
    private Task<DataTable> VisibleListAsync(Func<OperationContext, Task<DataTable>> list) => InScopeAsync(async c =>
    {
        DataTable table = await list(c).ConfigureAwait(true);
        Core.Security.EffectivePermissions permissions = await c.Get<Application.Abstractions.Security.IPermissionService>().ResolveAsync(c.UserId).ConfigureAwait(true);
        Application.Security.FieldSensitivity.Strip(table, Application.Security.FieldSensitivity.HiddenFor(permissions));
        return table;
    });

    // The signed-in user's permission in the action's module and the module's licence, then the screen's own feature and the flags it needs
    // (any one of them), checked before every run, list and delete (brief §5.1, §18.3).
    private Task<string?> RefusalAsync(Operation operation, bool isWrite, Core.Security.OperationFlags needs) => InScopeAsync(async c =>
    {
        Core.Security.EffectivePermissions permissions = await c.Get<Application.Abstractions.Security.IPermissionService>().ResolveAsync(c.UserId).ConfigureAwait(true);
        Application.Abstractions.Licensing.ILicenceService licence = c.Get<Application.Abstractions.Licensing.ILicenceService>();
        return operation.PermissionCode is string code
            ? Application.Security.ScreenPermissions.Unlicensed(code, licence.IsModuleLicensed) ?? Application.Security.ScreenPermissions.Refusal(permissions, code, needs, operation.Title)
            : Application.Security.DesktopAccessRules.Refusal(permissions, operation.Module, isWrite, licence.IsModuleLicensed);
    });

    private async Task<T> InScopeAsync<T>(Func<OperationContext, Task<T>> work)
    {
        using IServiceScope scope = _services.CreateScope();
        if (_companyId == 0)
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            // The first company the user's data scope reaches (brief §5.1).
            Application.Security.DataScope reach = await scope.ServiceProvider.GetRequiredService<Application.Abstractions.Security.IDataScopeService>().ForUserAsync(_userId).ConfigureAwait(true);
            var companies = await db.Companies.OrderBy(c => c.Id).Select(c => new { c.Id, c.Code }).ToListAsync().ConfigureAwait(true);
            _companyId = companies.FirstOrDefault(c => reach.Allows(Core.Security.ScopeDimension.Company, c.Code, c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))?.Id ?? 0;
        }

        return await work(new OperationContext(scope.ServiceProvider, _companyId, _userId) { ListPage = _listPage }).ConfigureAwait(true);
    }

    /// <summary>Runs the chosen operation with the form's values.</summary>
    /// <returns>A task.</returns>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        Operation operation = Selected!;
        IsBusy = true;
        try
        {
            if (await RefusalAsync(operation, !operation.IsDocument, operation.Needs).ConfigureAwait(true) is string refused)
            {
                Result = refused;
                Failed = true;
                StatusMessage = "Not allowed.";
                return;
            }

            OperationInput input = new(Fields.ToDictionary(f => f.Field.Name, f => f.Raw()), Fields.ToDictionary(f => f.Field.Name, f => f.Field.Label));
            Result = await InScopeAsync(c => operation.Run(c, input)).ConfigureAwait(true);
            _ = RecordUseAsync(operation, "Run");
            Failed = false;
            StatusMessage = operation.IsDocument ? $"{operation.Title} printed." : "Done.";
            if (operation.List is not null)
            {
                List = (await VisibleListAsync(operation.List).ConfigureAwait(true)).DefaultView;
            }
        }
        catch (OperationException ex)
        {
            Result = ex.Message;
            Failed = true;
            StatusMessage = "Not done - see the message.";
        }
#pragma warning disable CA1031 // The console reports any failure to the user rather than crashing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Result = ex.GetBaseException().Message;
            Failed = true;
            StatusMessage = "Failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Gets which page of the list is shown.</summary>
    public string PageText => $"Page {_listPage + 1}";

    /// <summary>Shows the list's previous page.</summary>
    /// <returns>A task.</returns>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task PreviousPageAsync()
    {
        if (_listPage > 0)
        {
            _listPage--;
            OnPropertyChanged(nameof(PageText));
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Shows the list's next page (back to the last when there is nothing further).</summary>
    /// <returns>A task.</returns>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task NextPageAsync()
    {
        _listPage++;
        OnPropertyChanged(nameof(PageText));
        await RefreshAsync().ConfigureAwait(true);
        if (List is { Count: 0 } && _listPage > 0)
        {
            _listPage--;
            OnPropertyChanged(nameof(PageText));
            await RefreshAsync().ConfigureAwait(true);
            StatusMessage = "That is the last page.";
        }
    }

    /// <summary>Reloads the live list.</summary>
    /// <returns>A task.</returns>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (Selected?.List is not Func<OperationContext, Task<DataTable>> list)
        {
            return;
        }

        try
        {
            if (await RefusalAsync(Selected, false, Core.Security.OperationFlags.View).ConfigureAwait(true) is string refused)
            {
                StatusMessage = refused;
                List = null;
                return;
            }

            List = (await VisibleListAsync(list).ConfigureAwait(true)).DefaultView;
        }
#pragma warning disable CA1031 // A list that cannot load is reported, not fatal.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            StatusMessage = "Could not load the list: " + ex.GetBaseException().Message;
        }
    }
}

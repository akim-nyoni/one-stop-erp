using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Desktop.Operations;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

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
    public bool IsText => Field.Kind is FieldKind.Text or FieldKind.Number;

    /// <summary>Gets a value indicating whether it is a date.</summary>
    public bool IsDate => Field.Kind == FieldKind.Date;

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
        _ => Value,
    };

    /// <summary>Gets a value indicating whether the choices are the company's records (typed codes are accepted too).</summary>
    public bool IsLookup => Field.ChoicesFrom is not null;
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
                case FieldKind.Choice:
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
    public string AddText => (SingleTitle ?? Selected?.Title ?? "Add") + "…";

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
        Fields.Clear();
        foreach (OperationField field in value?.Fields ?? [])
        {
            Fields.Add(new OperationFieldVm(field));
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
            Failed = false;
            StatusMessage = operation.IsDocument ? $"{operation.Title} printed." : "Done.";
            if (operation.List is not null)
            {
                List = (await InScopeAsync(operation.List).ConfigureAwait(true)).DefaultView;
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

            List = (await InScopeAsync(list).ConfigureAwait(true)).DefaultView;
        }
#pragma warning disable CA1031 // A list that cannot load is reported, not fatal.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            StatusMessage = "Could not load the list: " + ex.GetBaseException().Message;
        }
    }
}

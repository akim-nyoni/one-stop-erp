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

    /// <summary>Gets the choices.</summary>
    public IReadOnlyList<string> Choices => Field.Choices ?? [];

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
        _ => Value,
    };
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

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="module">The module to open on.</param>
    public OperationsViewModel(IServiceProvider services, long userId, string module)
    {
        _services = services;
        _userId = userId;
        _module = OperationCatalogue.Modules.Contains(module) ? module : OperationCatalogue.Modules[0];
        ShowModule();
    }

    /// <summary>Gets the modules.</summary>
    public IReadOnlyList<string> Modules => OperationCatalogue.Modules;

    /// <summary>Gets the module's operations.</summary>
    public ObservableCollection<Operation> Operations { get; } = [];

    /// <summary>Gets the selected operation's fields.</summary>
    public ObservableCollection<OperationFieldVm> Fields { get; } = [];

    /// <summary>The module shown.</summary>
    [ObservableProperty]
    private string _module;

    /// <summary>The chosen operation.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(RefreshCommand))]
    private Operation? _selected;

    /// <summary>The outcome of the last run (or the printed document).</summary>
    [ObservableProperty]
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
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(RefreshCommand))]
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
        StatusMessage = value?.Description ?? "Choose an action.";
        if (value?.List is not null)
        {
            _ = RefreshAsync();
        }
    }

    private void ShowModule()
    {
        Operations.Clear();
        foreach (Operation operation in OperationCatalogue.All().Where(o => o.Module == Module))
        {
            Operations.Add(operation);
        }

        Selected = Operations.FirstOrDefault();
    }

    private async Task<T> InScopeAsync<T>(Func<OperationContext, Task<T>> work)
    {
        using IServiceScope scope = _services.CreateScope();
        if (_companyId == 0)
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync().ConfigureAwait(true);
        }

        return await work(new OperationContext(scope.ServiceProvider, _companyId, _userId)).ConfigureAwait(true);
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
            StatusMessage = "Not done — see the message.";
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

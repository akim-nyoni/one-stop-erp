using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Common;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A role or a user in the picker: a role's grants are edited outright, a user's as grant/deny overrides on top of their roles.</summary>
/// <param name="Id">The role or user id.</param>
/// <param name="Name">The name.</param>
/// <param name="IsUser">True for a user.</param>
/// <param name="IsSystem">A built-in role, or a system administrator.</param>
public sealed record RoleItem(long Id, string Name, bool IsUser = false, bool IsSystem = false)
{
    /// <summary>The display label.</summary>
    public string Display => (IsUser ? "User: " : "Role: ") + Name + (IsSystem ? (IsUser ? " (administrator)" : " (system)") : string.Empty);
}

/// <summary>
/// One operation cell. For a role it is ticked or not; for a user it is three-state: ticked grants, cleared denies and the indeterminate
/// state inherits from the roles, shaded green when the user ends up with the operation.
/// </summary>
public sealed partial class PermissionCellViewModel : ObservableObject
{
    private readonly bool _inherited;

    /// <summary>Creates a cell.</summary>
    /// <param name="flag">The operation.</param>
    /// <param name="offered">Whether the feature offers it.</param>
    /// <param name="forUser">True for a user's override cell.</param>
    /// <param name="inherited">Whether the user's roles give it.</param>
    /// <param name="value">The initial state.</param>
    public PermissionCellViewModel(OperationFlags flag, bool offered, bool forUser, bool inherited, bool? value)
    {
        Flag = flag;
        Offered = offered;
        ThreeState = forUser;
        _inherited = inherited;
        _value = value;
    }

    /// <summary>The operation.</summary>
    public OperationFlags Flag { get; }

    /// <summary>Whether the feature offers the operation.</summary>
    public bool Offered { get; }

    /// <summary>True for a user's grant/deny/inherit cell.</summary>
    public bool ThreeState { get; }

    /// <summary>The cell state: ticked, cleared, or (users only) null to inherit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shade), nameof(Tip))]
    private bool? _value;

    /// <summary>Whether the subject ends up with the operation.</summary>
    public bool Effective => Offered && (Value ?? _inherited);

    /// <summary>The cell background: green when a user has the operation, transparent otherwise.</summary>
    public string Shade => ThreeState && Effective ? "#DDF1E3" : "Transparent";

    /// <summary>The cell tooltip.</summary>
    public string Tip => !Offered ? $"{Flag}: not offered"
        : !ThreeState ? Flag.ToString()
        : Value switch
        {
            true => $"{Flag}: granted to this user",
            false => $"{Flag}: denied to this user",
            _ => $"{Flag}: {(_inherited ? "allowed" : "not allowed")} by the user's roles",
        };
}

/// <summary>One feature row on the grid with a cell for every operation flag.</summary>
public sealed class PermissionRowViewModel
{
    private readonly OperationFlags _available;

    /// <summary>Creates a row for a role's grant.</summary>
    /// <param name="definition">The feature.</param>
    /// <param name="current">The role's granted operations.</param>
    public PermissionRowViewModel(PermissionDefinition definition, OperationFlags current)
        : this(definition, PermissionCells.Columns.Select(f => (bool?)((definition.Available & f) == f && (current & f) == f)), forUser: false, OperationFlags.None)
    {
    }

    /// <summary>Creates a row for a user's overrides.</summary>
    /// <param name="definition">The feature.</param>
    /// <param name="inherited">The operations the user's roles give.</param>
    /// <param name="grant">The user's grant override.</param>
    /// <param name="deny">The user's deny override.</param>
    public PermissionRowViewModel(PermissionDefinition definition, OperationFlags inherited, OperationFlags grant, OperationFlags deny)
        : this(definition, PermissionCells.Columns.Select(f => PermissionCells.CellOf(f, grant, deny)), forUser: true, inherited)
    {
    }

    private PermissionRowViewModel(PermissionDefinition definition, IEnumerable<bool?> values, bool forUser, OperationFlags inherited)
    {
        Code = definition.Code;
        Module = definition.Module;
        Feature = definition.Name;
        _available = definition.Available;
        Cells = [.. PermissionCells.Columns.Zip(values, (flag, value) =>
            new PermissionCellViewModel(flag, (_available & flag) == flag, forUser, (inherited & flag) == flag, (_available & flag) == flag ? value : forUser ? null : false))];
    }

    /// <summary>The permission code.</summary>
    public string Code { get; }

    /// <summary>The owning module (the grid groups by it).</summary>
    public string Module { get; }

    /// <summary>The feature name.</summary>
    public string Feature { get; }

    /// <summary>A cell per operation, in <see cref="PermissionCells.Columns"/> order.</summary>
    public PermissionCellViewModel[] Cells { get; }

    /// <summary>A role's operations: the ticked cells the feature offers.</summary>
    /// <returns>The operations.</returns>
    public OperationFlags CurrentOperations() => PermissionCells.FromCells([.. Cells.Select(c => c.Value)], _available).Grant;

    /// <summary>A user's overrides.</summary>
    /// <returns>The grant and deny operations.</returns>
    public UserOverrideEdit Overrides()
    {
        (OperationFlags grant, OperationFlags deny) = PermissionCells.FromCells([.. Cells.Select(c => c.Value)], _available);
        return new UserOverrideEdit(Code, grant, deny);
    }
}

/// <summary>
/// Backs the permission grid (brief §5.2): pick a role and tick every operation it may perform per feature, or pick a user and grant or deny
/// operations on top of their roles. Features are grouped by module.
/// </summary>
public sealed partial class PermissionGridViewModel : ObservableObject
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider.</param>
    public PermissionGridViewModel(IServiceProvider services)
    {
        _services = services;
        PermissionsView = CollectionViewSource.GetDefaultView(Permissions);
        PermissionsView.Filter = MatchesFilter;
        PermissionsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PermissionRowViewModel.Module)));
        PermissionsView.SortDescriptions.Add(new SortDescription(nameof(PermissionRowViewModel.Module), ListSortDirection.Ascending));
        PermissionsView.SortDescriptions.Add(new SortDescription(nameof(PermissionRowViewModel.Feature), ListSortDirection.Ascending));
    }

    /// <summary>The roles and users available to edit.</summary>
    public ObservableCollection<RoleItem> Roles { get; } = [];

    /// <summary>The permission rows (all catalogue features).</summary>
    public ObservableCollection<PermissionRowViewModel> Permissions { get; } = [];

    /// <summary>The filtered, grouped view bound to the grid.</summary>
    public ICollectionView PermissionsView { get; }

    /// <summary>The selected role or user; changing it reloads the grid.</summary>
    [ObservableProperty]
    private RoleItem? _selectedRole;

    /// <summary>A module/feature filter for the grid.</summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>A status line for the footer.</summary>
    [ObservableProperty]
    private string _statusMessage = "Select a role, or a user to grant or deny operations on top of their roles.";

    /// <summary>Loads the roles and users.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

        List<RoleItem> roles = await db.Roles.OrderBy(r => r.Name).Select(r => new RoleItem(r.Id, r.Name, false, r.IsSystemRole)).ToListAsync(ct).ConfigureAwait(true);
        List<RoleItem> users = await db.Users.Where(u => u.IsActive).OrderBy(u => u.Username)
            .Select(u => new RoleItem(u.Id, u.DisplayName + " (" + u.Username + ")", true, u.IsSystemAdministrator)).ToListAsync(ct).ConfigureAwait(true);

        Roles.Clear();
        foreach (RoleItem item in roles.Concat(users))
        {
            Roles.Add(item);
        }
    }

    partial void OnFilterChanged(string value) => PermissionsView.Refresh();

    /// <summary>Gets the rows the filter shows.</summary>
    public IEnumerable<PermissionRowViewModel> VisibleRows => PermissionsView.Cast<PermissionRowViewModel>();

    /// <summary>
    /// Ticks or clears many cells at once (brief §5.1 "select all in row/column"): every offered cell of the rows given, or just one column of them.
    /// Clearing a user's cells returns them to what the user's roles give.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="column">One column (its index), or null for every column.</param>
    /// <param name="on">True to tick (grant), false to clear.</param>
    /// <returns>How many cells changed.</returns>
    public int SetCells(IEnumerable<PermissionRowViewModel> rows, int? column, bool on)
    {
        int changed = 0;
        foreach (PermissionRowViewModel row in rows)
        {
            for (int i = 0; i < row.Cells.Length; i++)
            {
                PermissionCellViewModel cell = row.Cells[i];
                bool? value = on ? true : cell.ThreeState ? null : false;
                if ((column is null || column == i) && cell.Offered && cell.Value != value)
                {
                    cell.Value = value;
                    changed++;
                }
            }
        }

        StatusMessage = changed == 0 ? "Nothing to change." : $"{changed} operation(s) {(on ? "ticked" : "cleared")} - Save to keep them.";
        return changed;
    }

    async partial void OnSelectedRoleChanged(RoleItem? value)
    {
        Permissions.Clear();
        if (value is null)
        {
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        if (value.IsUser)
        {
            User? user = await db.Users.Include(u => u.Overrides).AsNoTracking().FirstOrDefaultAsync(u => u.Id == value.Id).ConfigureAwait(true);
            IReadOnlyList<PermissionGrant> roleGrants = await scope.ServiceProvider.GetRequiredService<IPermissionService>().RoleGrantsAsync(value.Id).ConfigureAwait(true);
            Dictionary<string, OperationFlags> inherited = roleGrants.GroupBy(g => g.PermissionCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Aggregate(OperationFlags.None, (a, x) => a | x.Operations), StringComparer.OrdinalIgnoreCase);
            List<UserPermissionOverrideEntity> overrides = [.. user?.Overrides ?? []];
            OperationFlags Override(string code, bool isGrant) => overrides
                .Where(o => o.IsGrant == isGrant && string.Equals(o.PermissionCode, code, StringComparison.OrdinalIgnoreCase)).Aggregate(OperationFlags.None, (a, o) => a | o.Operations);
            foreach (PermissionDefinition definition in PermissionCatalogue.All)
            {
                Permissions.Add(new PermissionRowViewModel(definition, inherited.GetValueOrDefault(definition.Code), Override(definition.Code, true), Override(definition.Code, false)));
            }

            StatusMessage = value.IsSystem
                ? $"{value.Name} is a system administrator and has every operation; overrides apply only if that is removed."
                : $"Overrides for {value.Name}: tick to grant, clear to deny, leave indeterminate to inherit from the roles (green = allowed).";
        }
        else
        {
            Role? role = await db.Roles.Include(r => r.Permissions).AsNoTracking().FirstOrDefaultAsync(r => r.Id == value.Id).ConfigureAwait(true);
            Dictionary<string, OperationFlags> grants = role?.Permissions.ToDictionary(p => p.PermissionCode, p => p.Operations, StringComparer.OrdinalIgnoreCase) ?? [];
            foreach (PermissionDefinition definition in PermissionCatalogue.All)
            {
                Permissions.Add(new PermissionRowViewModel(definition, grants.GetValueOrDefault(definition.Code, OperationFlags.None)));
            }

            StatusMessage = $"Editing role {value.Name}: {Permissions.Count} features.";
        }

        PermissionsView.Refresh();
    }

    /// <summary>Saves the grid to the selected role or user.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or a validation error.</returns>
    public async Task<Result> SaveAsync(CancellationToken ct = default)
    {
        if (SelectedRole is null)
        {
            return Result.Failure(Error.Validation("Perm.NoRole", "Select a role or a user first."));
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        if (SelectedRole.IsUser)
        {
            Result saved = await scope.ServiceProvider.GetRequiredService<IPermissionService>()
                .SetOverridesAsync(SelectedRole.Id, [.. Permissions.Select(p => p.Overrides())], ct).ConfigureAwait(true);
            if (saved.IsSuccess)
            {
                StatusMessage = $"Saved overrides for {SelectedRole.Name} at {DateTime.Now:HH:mm:ss}.";
            }

            return saved;
        }

        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        Role? role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == SelectedRole.Id, ct).ConfigureAwait(true);
        if (role is null)
        {
            return Result.Failure(Error.NotFound("Perm.RoleNotFound", "The role no longer exists."));
        }

        foreach (PermissionRowViewModel row in Permissions)
        {
            role.Grant(row.Code, row.CurrentOperations());
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(true);
        StatusMessage = $"Saved permissions for {role.Name} at {DateTime.Now:HH:mm:ss}.";
        return Result.Success();
    }

    private bool MatchesFilter(object item)
    {
        if (string.IsNullOrWhiteSpace(Filter) || item is not PermissionRowViewModel row)
        {
            return true;
        }

        string term = Filter.Trim();
        return row.Module.Contains(term, StringComparison.OrdinalIgnoreCase)
            || row.Feature.Contains(term, StringComparison.OrdinalIgnoreCase)
            || row.Code.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}

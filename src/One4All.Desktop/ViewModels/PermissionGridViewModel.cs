using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Core.Common;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A role in the picker.</summary>
/// <param name="Id">The role id.</param>
/// <param name="Name">The role name.</param>
/// <param name="IsSystemRole">Whether it is a built-in system role.</param>
public sealed record RoleItem(long Id, string Name, bool IsSystemRole)
{
    /// <summary>The display label.</summary>
    public string Display => IsSystemRole ? $"{Name} (system)" : Name;
}

/// <summary>
/// One permission row on the grid: the common operation flags as editable checkboxes, enabled only where the
/// permission actually offers that operation. Non-common flags on the role's grant (authorise levels, price/
/// credit overrides, back-dating, …) are preserved untouched so editing the grid never silently drops them.
/// </summary>
public sealed partial class PermissionRowViewModel : ObservableObject
{
    /// <summary>The common operations shown as grid columns, in order.</summary>
    public static readonly OperationFlags[] CoreOperations =
    [
        OperationFlags.View, OperationFlags.Create, OperationFlags.Edit, OperationFlags.Delete,
        OperationFlags.Print, OperationFlags.Export, OperationFlags.Post, OperationFlags.Approve, OperationFlags.VoidReverse,
    ];

    private static readonly OperationFlags CoreMask = CoreOperations.Aggregate(OperationFlags.None, (a, f) => a | f);

    private readonly OperationFlags _available;
    private readonly OperationFlags _preservedNonCore;

    /// <summary>Creates a row from a catalogue definition and the role's current grant.</summary>
    /// <param name="definition">The permission definition.</param>
    /// <param name="current">The role's currently granted operations for this permission.</param>
    public PermissionRowViewModel(PermissionDefinition definition, OperationFlags current)
    {
        Code = definition.Code;
        Module = definition.Module;
        Feature = definition.Name;
        _available = definition.Available;
        _preservedNonCore = current & ~CoreMask;

        _view = Has(current, OperationFlags.View);
        _create = Has(current, OperationFlags.Create);
        _edit = Has(current, OperationFlags.Edit);
        _delete = Has(current, OperationFlags.Delete);
        _print = Has(current, OperationFlags.Print);
        _export = Has(current, OperationFlags.Export);
        _post = Has(current, OperationFlags.Post);
        _approve = Has(current, OperationFlags.Approve);
        _voidReverse = Has(current, OperationFlags.VoidReverse);

        bool Has(OperationFlags ops, OperationFlags flag) => (_available & flag) == flag && (ops & flag) == flag;
    }

    /// <summary>The permission code.</summary>
    public string Code { get; }

    /// <summary>The owning module.</summary>
    public string Module { get; }

    /// <summary>The feature name.</summary>
    public string Feature { get; }

    /// <summary>True when the permission offers each operation (drives per-cell enablement).</summary>
    public bool CanView => Offers(OperationFlags.View);
    public bool CanCreate => Offers(OperationFlags.Create);
    public bool CanEdit => Offers(OperationFlags.Edit);
    public bool CanDelete => Offers(OperationFlags.Delete);
    public bool CanPrint => Offers(OperationFlags.Print);
    public bool CanExport => Offers(OperationFlags.Export);
    public bool CanPost => Offers(OperationFlags.Post);
    public bool CanApprove => Offers(OperationFlags.Approve);
    public bool CanVoidReverse => Offers(OperationFlags.VoidReverse);

    [ObservableProperty] private bool _view;
    [ObservableProperty] private bool _create;
    [ObservableProperty] private bool _edit;
    [ObservableProperty] private bool _delete;
    [ObservableProperty] private bool _print;
    [ObservableProperty] private bool _export;
    [ObservableProperty] private bool _post;
    [ObservableProperty] private bool _approve;
    [ObservableProperty] private bool _voidReverse;

    private bool Offers(OperationFlags flag) => (_available & flag) == flag;

    /// <summary>The composed operations to grant: the ticked (and offered) common flags plus preserved others.</summary>
    public OperationFlags CurrentOperations()
    {
        OperationFlags ops = _preservedNonCore;
        if (View) { ops |= OperationFlags.View; }
        if (Create) { ops |= OperationFlags.Create; }
        if (Edit) { ops |= OperationFlags.Edit; }
        if (Delete) { ops |= OperationFlags.Delete; }
        if (Print) { ops |= OperationFlags.Print; }
        if (Export) { ops |= OperationFlags.Export; }
        if (Post) { ops |= OperationFlags.Post; }
        if (Approve) { ops |= OperationFlags.Approve; }
        if (VoidReverse) { ops |= OperationFlags.VoidReverse; }
        return ops & _available; // never grant an operation the permission does not offer
    }
}

/// <summary>
/// Backs the permission-grid editor: choose a role and tick the operations it may perform per feature, then
/// save. Grants are applied through the <see cref="Role"/> aggregate (which removes empty grants), and the
/// full catalogue is written each save so stale grants are cleaned up.
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
        PermissionsView.SortDescriptions.Add(new SortDescription(nameof(PermissionRowViewModel.Module), ListSortDirection.Ascending));
        PermissionsView.SortDescriptions.Add(new SortDescription(nameof(PermissionRowViewModel.Feature), ListSortDirection.Ascending));
    }

    /// <summary>The roles available to edit.</summary>
    public ObservableCollection<RoleItem> Roles { get; } = [];

    /// <summary>The permission rows (all catalogue features).</summary>
    public ObservableCollection<PermissionRowViewModel> Permissions { get; } = [];

    /// <summary>The filtered/sorted view bound to the grid.</summary>
    public ICollectionView PermissionsView { get; }

    /// <summary>The selected role; changing it reloads the grid from that role's grants.</summary>
    [ObservableProperty]
    private RoleItem? _selectedRole;

    /// <summary>A module/feature filter for the grid.</summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>A status line for the footer.</summary>
    [ObservableProperty]
    private string _statusMessage = "Select a role to edit its permissions.";

    /// <summary>Loads the roles.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

        List<RoleItem> roles = await db.Roles
            .OrderBy(r => r.Name)
            .Select(r => new RoleItem(r.Id, r.Name, r.IsSystemRole))
            .ToListAsync(ct).ConfigureAwait(true);

        Roles.Clear();
        foreach (RoleItem role in roles)
        {
            Roles.Add(role);
        }
    }

    partial void OnFilterChanged(string value) => PermissionsView.Refresh();

    async partial void OnSelectedRoleChanged(RoleItem? value)
    {
        Permissions.Clear();
        if (value is null)
        {
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

        Role? role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == value.Id).ConfigureAwait(true);
        Dictionary<string, OperationFlags> grants = role?.Permissions
            .ToDictionary(p => p.PermissionCode, p => p.Operations, StringComparer.OrdinalIgnoreCase) ?? [];

        foreach (PermissionDefinition definition in PermissionCatalogue.All)
        {
            OperationFlags current = grants.GetValueOrDefault(definition.Code, OperationFlags.None);
            Permissions.Add(new PermissionRowViewModel(definition, current));
        }

        PermissionsView.Refresh();
        StatusMessage = $"Editing {value.Name}: {Permissions.Count} features.";
    }

    /// <summary>Saves the grid to the selected role.</summary>
    /// <returns>Success or a validation error.</returns>
    public async Task<Result> SaveAsync(CancellationToken ct = default)
    {
        if (SelectedRole is null)
        {
            return Result.Failure(Error.Validation("Perm.NoRole", "Select a role first."));
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
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

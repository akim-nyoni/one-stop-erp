using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A table drawn on the waiter's floor screen.</summary>
/// <param name="Number">The table number.</param>
/// <param name="Left">Its left edge in pixels.</param>
/// <param name="Top">Its top edge in pixels.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="CornerRadius">Its corner radius (round tables are circles).</param>
/// <param name="Fill">Its colour: free, occupied, or warned.</param>
/// <param name="Line1">The number and seats.</param>
/// <param name="Line2">The tab total and minutes open, when occupied.</param>
/// <param name="Detail">The full description for the side panel.</param>
public sealed record FloorTableVm(string Number, double Left, double Top, double Width, double Height, double CornerRadius, string Fill, string Line1, string Line2, string Detail);

/// <summary>
/// Backs the waiter's floor screen (brief §4.1 restaurant "graphical floor / table plan"): every table at its place on the plan, coloured by
/// whether it is free, occupied or past its tab warning, with the open tab's total and age; refreshed on a timer so the floor stays current.
/// </summary>
public sealed partial class FloorPlanViewModel : ObservableObject
{
    /// <summary>Pixels per plan grid cell.</summary>
    public const double Cell = 24;

    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application services.</param>
    public FloorPlanViewModel(IServiceProvider services) => _services = services;

    /// <summary>Gets the tables to draw.</summary>
    public ObservableCollection<FloorTableVm> Tables { get; } = [];

    /// <summary>Gets or sets the table picked on the plan.</summary>
    [ObservableProperty]
    private FloorTableVm? _selected;

    /// <summary>Gets or sets the summary line.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Gets or sets the canvas width needed for the plan.</summary>
    [ObservableProperty]
    private double _canvasWidth = 800;

    /// <summary>Gets or sets the canvas height needed for the plan.</summary>
    [ObservableProperty]
    private double _canvasHeight = 600;

    /// <summary>Reloads the plan.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        if (_companyId == 0)
        {
            _companyId = await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
        }

        IReadOnlyList<FloorTable> plan = await scope.ServiceProvider.GetRequiredService<IFloorPlanService>().PlanAsync(_companyId, DateTimeOffset.UtcNow, ct).ConfigureAwait(true);
        string? keep = Selected?.Number;
        Tables.Clear();
        int unplaced = 0, maxRow = plan.Where(t => t.Y is not null).Select(t => t.Y!.Value).DefaultIfEmpty(0).Max();
        foreach (FloorTable table in plan.OrderBy(t => t.TableNumber, StringComparer.OrdinalIgnoreCase))
        {
            // Tables not yet placed line up along the bottom so nothing is hidden.
            (int x, int y) = table.X is int px && table.Y is int py ? (px, py) : (unplaced++ * 4, maxRow + 4);
            (double w, double h) = table.Shape switch
            {
                TableShape.Booth => (Cell * 4, Cell * 2),
                TableShape.BarSeat => (Cell * 1.5, Cell * 1.5),
                _ => (Cell * 3, Cell * 3),
            };
            string fill = table.Warning is not null ? "#C0392B" : table.Status == TableStatus.Occupied ? "#D68910" : "#239B56";
            string line2 = table.Status == TableStatus.Occupied ? $"{table.TabTotal.ToString("N2", CultureInfo.InvariantCulture)} · {table.MinutesOpen} min" : "free";
            string detail = $"Table {table.TableNumber} ({table.Seats} seats, {table.Shape})" + Environment.NewLine
                + (table.Status == TableStatus.Occupied ? $"Order {table.OrderNumber}: {table.TabTotal.ToString("N2", CultureInfo.InvariantCulture)}, open {table.MinutesOpen} min" : "Free")
                + (table.Warning is null ? string.Empty : Environment.NewLine + "⚠ " + table.Warning);
            Tables.Add(new FloorTableVm(table.TableNumber, x * Cell, y * Cell, w, h, table.Shape == TableShape.Round ? w / 2 : 6, fill, $"{table.TableNumber} ({table.Seats})", line2, detail));
        }

        CanvasWidth = Math.Max(800, Tables.Select(t => t.Left + t.Width).DefaultIfEmpty(0).Max() + Cell);
        CanvasHeight = Math.Max(600, Tables.Select(t => t.Top + t.Height).DefaultIfEmpty(0).Max() + Cell);
        int occupied = plan.Count(t => t.Status == TableStatus.Occupied), warned = plan.Count(t => t.Warning is not null);
        Summary = $"{plan.Count} table(s): {occupied} occupied, {plan.Count - occupied} free, {warned} warned · {plan.Where(t => t.Status == TableStatus.Occupied).Sum(t => t.TabTotal).ToString("N2", CultureInfo.InvariantCulture)} open on tabs · {DateTime.Now:HH:mm}";
        Selected = Tables.FirstOrDefault(t => t.Number == keep);
    }

    /// <summary>Picks a table.</summary>
    /// <param name="table">The table clicked.</param>
    [RelayCommand]
    private void Pick(FloorTableVm? table) => Selected = table;
}

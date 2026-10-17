using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop;

/// <summary>
/// "Who touched this record?" (brief §6): pick a record type and id (or arrive with them) to see who created it, who last changed it,
/// everyone involved, every logged change with its fields, and anything the database saw done to the row outside the application.
/// </summary>
public sealed class RecordHistoryWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly ComboBox _type = new() { IsEditable = true, Width = 220, Margin = new Thickness(4, 0, 12, 0) };
    private readonly TextBox _id = new() { Width = 120, Margin = new Thickness(4, 0, 12, 0) };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 6, 10, 6), FontSize = 13 };
    private readonly DataGrid _entries = new() { AutoGenerateColumns = true, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(10, 0, 10, 6) };
    private readonly DataGrid _database = new() { AutoGenerateColumns = true, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(10, 0, 10, 10), Height = 140 };
    private long _companyId;

    /// <summary>Creates the window.</summary>
    /// <param name="services">The application services.</param>
    /// <param name="entityType">The record type to show straight away.</param>
    /// <param name="entityId">The record id to show straight away.</param>
    public RecordHistoryWindow(IServiceProvider services, string? entityType = null, string? entityId = null)
    {
        _services = services;
        Title = "Who touched this record?";
        Width = 1000;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        _id.Text = entityId ?? string.Empty;
        _type.Text = entityType ?? string.Empty;

        Button show = new() { Content = "Show", Padding = new Thickness(14, 3, 14, 3), FontWeight = FontWeights.SemiBold };
        show.Click += async (_, _) => await ShowAsync().ConfigureAwait(true);
        StackPanel bar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
        bar.Children.Add(new TextBlock { Text = "Record type", VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(_type);
        bar.Children.Add(new TextBlock { Text = "Id", VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(_id);
        bar.Children.Add(show);

        DockPanel root = new();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);
        TextBlock dbTitle = new() { Text = "What the database saw (its own trigger; red = outside the application)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 10, 2) };
        StackPanel bottom = new();
        bottom.Children.Add(dbTitle);
        bottom.Children.Add(_database);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_entries);
        Content = root;

        Loaded += async (_, _) =>
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            _companyId = await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync().ConfigureAwait(true);
            _type.ItemsSource = await scope.ServiceProvider.GetRequiredService<IAuditService>().EntityTypesAsync(_companyId).ConfigureAwait(true);
            if (_type.Text.Length > 0 && _id.Text.Length > 0)
            {
                await ShowAsync().ConfigureAwait(true);
            }
        };
    }

    private async Task ShowAsync()
    {
        if (string.IsNullOrWhiteSpace(_type.Text) || string.IsNullOrWhiteSpace(_id.Text))
        {
            _summary.Text = "Choose the record type and type its id.";
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        RecordHistory history = await scope.ServiceProvider.GetRequiredService<IAuditService>().RecordHistoryAsync(_companyId, _type.Text, _id.Text).ConfigureAwait(true);
        static string When(AuditEntryView? e) => e is null ? "not in the log" : $"{e.UserName} on {e.OccurredOnUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
        _summary.Text = history.Entries.Count == 0 && history.DatabaseChanges.Count == 0
            ? $"Nothing is logged for {history.EntityType} #{history.EntityId}. (Is the type spelt as in the list?)"
            : $"{history.EntityType} #{history.EntityId} - created by {When(history.Created)}; last changed by {When(history.LastChanged)}. "
              + $"Touched by: {string.Join(", ", history.Users)}. {history.Entries.Count} log entr{(history.Entries.Count == 1 ? "y" : "ies")}"
              + (history.OutsideApplication.Count > 0 ? $"; {history.OutsideApplication.Count} change(s) made OUTSIDE the application." : ".");
        _summary.Foreground = history.OutsideApplication.Count > 0 ? Brushes.Firebrick : Brushes.Black;
        _entries.ItemsSource = history.Entries.Select(e => new
        {
            When = e.OccurredOnUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), User = e.UserName, e.Action, Fields = string.Join(", ", e.ChangedFields), e.Reason,
            Before = Short(e.OldValues), After = Short(e.NewValues), e.Terminal,
        }).ToList();
        _database.ItemsSource = history.DatabaseChanges.Select(c => new
        {
            When = c.OccurredOnUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), c.Operation, DatabaseUser = c.DbUser, Application = c.ApplicationName, Client = c.ClientAddress,
            Outside = c.OutsideApplication ? "YES" : string.Empty,
        }).ToList();
    }

    private static string Short(string? json) => json is null ? string.Empty : json.Length > 160 ? json[..160] + "…" : json;
}

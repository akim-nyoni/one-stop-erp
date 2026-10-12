using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using One4All.Application.Localization;

namespace One4All.Desktop;

/// <summary>
/// Scales every window's content to the user's text size (Ctrl + / Ctrl − / Ctrl 0, or View › Text Size), remembered on this computer for the
/// Windows user.
/// </summary>
public static class UiScaling
{
    private static readonly string StoreFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "One4All", "ui-scale.txt");

    /// <summary>Gets the text size in use.</summary>
    public static double Current { get; private set; } = 1.0;

    /// <summary>Reads the remembered size and hooks every window as it loads.</summary>
    public static void Register()
    {
        try
        {
            Current = UiScale.Parse(File.Exists(StoreFile) ? File.ReadAllText(StoreFile) : null);
        }
        catch (IOException)
        {
            Current = 1.0;
        }
        catch (UnauthorizedAccessException)
        {
            Current = 1.0;
        }

        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => Apply((Window)sender)));
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnKey));
    }

    /// <summary>Sets the text size for every open window and remembers it.</summary>
    /// <param name="scale">The new size.</param>
    public static void Set(double scale)
    {
        Current = UiScale.Parse(scale.ToString(CultureInfo.InvariantCulture));
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            Apply(window);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoreFile)!);
            File.WriteAllText(StoreFile, Current.ToString(CultureInfo.InvariantCulture));
        }
        catch (IOException)
        {
            // The size still applies for this session.
        }
        catch (UnauthorizedAccessException)
        {
            // The size still applies for this session.
        }
    }

    private static void Apply(Window window)
    {
        if (window.Content is FrameworkElement content)
        {
            content.LayoutTransform = Math.Abs(Current - 1.0) < 0.001 ? Transform.Identity : new ScaleTransform(Current, Current);
        }
    }

    private static void OnKey(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        double? next = e.Key switch
        {
            Key.OemPlus or Key.Add => UiScale.Larger(Current),
            Key.OemMinus or Key.Subtract => UiScale.Smaller(Current),
            Key.D0 or Key.NumPad0 => 1.0,
            _ => null,
        };
        if (next is double scale)
        {
            Set(scale);
            e.Handled = true;
        }
    }
}

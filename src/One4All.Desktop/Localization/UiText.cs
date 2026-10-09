using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.System;
using One4All.Application.Localization;

namespace One4All.Desktop.Localization;

/// <summary>The signed-in company's screen text: its locale's translations and its terminology (loaded at sign-in).</summary>
public static class UiText
{
    /// <summary>Gets or sets the catalog in use.</summary>
    public static TextCatalog Catalog { get; set; } = TextCatalog.English;

    /// <summary>The text to show for an English string.</summary>
    /// <param name="english">The English text.</param>
    /// <returns>The translated text with the company's terms.</returns>
    public static string T(string english) => Catalog[english];

    /// <summary>Loads the first company's catalog (the desktop works on one company).</summary>
    /// <param name="services">The application services.</param>
    /// <returns>A task.</returns>
    public static async Task LoadAsync(IServiceProvider services)
    {
        try
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            long companyId = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.One4AllDbContext>().Companies.OrderBy(c => c.Id).Select(c => c.Id)).ConfigureAwait(true);
            Catalog = companyId == 0 ? TextCatalog.English : await scope.ServiceProvider.GetRequiredService<ILocalizationService>().CatalogAsync(companyId).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Catalog = TextCatalog.English;
        }
    }

    /// <summary>The flow direction for the company's language.</summary>
    public static FlowDirection Direction => Catalog.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
}

/// <summary>XAML: <c>Header="{loc:T 'Account Enquiry…'}"</c> shows the text in the company's language with its terms.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    /// <summary>Creates the extension.</summary>
    /// <param name="text">The English text.</param>
    public TExtension(string text) => Text = text;

    /// <summary>Gets or sets the English text.</summary>
    [ConstructorArgument("text")]
    public string Text { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.T(Text);
}

/// <summary>Shows bound text (an operation's title, a field's label) in the company's language with its terms; untranslated text stays as it is.</summary>
public sealed class TextConverter : System.Windows.Data.IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is string text ? UiText.T(text) : value;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

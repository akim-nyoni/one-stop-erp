using System.Windows;
using One4All.Desktop.ViewModels;

namespace One4All.Desktop;

/// <summary>The municipal Enquiry Desk. All behaviour lives in <see cref="EnquiryDeskViewModel"/>; this code-behind only constructs it.</summary>
public partial class EnquiryDeskWindow : Window
{
    /// <summary>Creates the screen against the application services.</summary>
    /// <param name="services">The application service provider.</param>
    public EnquiryDeskWindow(IServiceProvider services)
    {
        DataContext = new EnquiryDeskViewModel(services);
        InitializeComponent();
    }
}

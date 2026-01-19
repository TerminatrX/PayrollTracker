using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

/// <summary>
/// Settings page with sidebar navigation for different configuration sections.
/// Handles company profile, tax configuration, and pay period settings.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }

    /// <summary>
    /// Handles settings navigation item clicks to scroll to the appropriate section.
    /// </summary>
    private void SettingsNavItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string sectionName)
        {
            // Find the target section and scroll to it
            FrameworkElement? targetSection = sectionName switch
            {
                "CompanyProfileSection" => CompanyProfileSection,
                "TaxConfigSection" => TaxConfigSection,
                "PayPeriodsSection" => PayPeriodsSection,
                _ => null
            };

            if (targetSection != null)
            {
                // Scroll the section into view
                targetSection.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalAlignmentRatio = 0.0
                });
            }
        }
    }
}

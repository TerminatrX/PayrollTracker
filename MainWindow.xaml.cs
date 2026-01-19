using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PayrollManager.UI.Views;

namespace PayrollManager.UI
{
    /// <summary>
    /// Main application window with NavigationView for page navigation.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            
            // Set window title
            Title = "PayrollManager - Enterprise Edition";
            
            // Select first nav item and navigate to Employees page
            MainNav.SelectedItem = MainNav.MenuItems[1]; // Skip header, select Employees
            ContentFrame.Navigate(typeof(EmployeesPage));
        }

        private void MainNav_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            {
                return;
            }

            var targetType = tag switch
            {
                "employees" => typeof(EmployeesPage),
                "payruns" => typeof(PayRunsPage),
                "paystubs" => typeof(PayStubPage),
                "reports" => typeof(ReportsPage),
                "settings" => typeof(SettingsPage),
                "help" => typeof(SettingsPage), // Placeholder - could create HelpPage
                _ => typeof(EmployeesPage)
            };

            if (ContentFrame.CurrentSourcePageType != targetType)
            {
                ContentFrame.Navigate(targetType);
            }
        }
    }
}

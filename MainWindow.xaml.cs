using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PayrollManager.UI.Views;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PayrollManager.UI
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            MainNav.SelectedItem = MainNav.MenuItems[0];
            ContentFrame.Navigate(typeof(EmployeeManagementPage));
        }

        private void MainNav_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            {
                return;
            }

            var targetType = tag switch
            {
                "employees" => typeof(EmployeeManagementPage),
                "payruns" => typeof(PayrollRunPage),
                "reports" => typeof(ReportsPage),
                "settings" => typeof(SettingsPage),
                _ => typeof(EmployeeManagementPage)
            };

            if (ContentFrame.CurrentSourcePageType != targetType)
            {
                ContentFrame.Navigate(targetType);
            }
        }
    }
}

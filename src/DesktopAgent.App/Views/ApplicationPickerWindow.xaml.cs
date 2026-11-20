using System.Windows;
using DesktopAgent.Core.Applications.Interfaces;
using DesktopAgent.Core.Applications.Models;

namespace DesktopAgent.App.Views;

public partial class ApplicationPickerWindow : Window
{
    private readonly IApplicationCatalog _catalog;
    public InstalledApplication? SelectedApplication { get; private set; }
    public ApplicationPickerWindow(IApplicationCatalog catalog)
    {
        InitializeComponent();
        _catalog = catalog;
        Loaded += (_, _) => Refresh();
    }
    private void SearchInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();
    private void Refresh() => ApplicationsList.ItemsSource = string.IsNullOrWhiteSpace(SearchInput.Text)
        ? _catalog.Applications.Where(item => item.IsLaunchable && !item.IsSystemApplication).OrderByDescending(item => item.IsFavorite).ThenBy(item => item.DisplayName).ToList()
        : _catalog.SearchApplications(SearchInput.Text, false, 100).Select(match => match.Application).ToList();
    private void Add_Click(object sender, RoutedEventArgs e) => Accept();
    private void ApplicationsList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Accept();
    private void Accept() { if (ApplicationsList.SelectedItem is InstalledApplication app) { SelectedApplication = app; DialogResult = true; } }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}

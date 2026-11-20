using System.Windows;
using DesktopAgent.App.ViewModels;
using DesktopAgent.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopAgent.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly IWindowService _windows;
    public SettingsWindow(SettingsViewModel viewModel, IWindowService windows, IServiceProvider services)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _windows = windows;
        DataContext = viewModel;
        AiSettingsHost.Content = ActivatorUtilities.CreateInstance<AiSettingsView>(services);
        ProjectSettingsHost.Content = ActivatorUtilities.CreateInstance<ProjectSettingsView>(services);
        FileSettingsHost.Content = ActivatorUtilities.CreateInstance<FileIntelligenceSettingsView>(services);
        DeveloperSettingsHost.Content = ActivatorUtilities.CreateInstance<DeveloperSettingsView>(services);
        UiInspectorHost.Content = ActivatorUtilities.CreateInstance<UiInspectorView>(services);
        WindowsDiagnosticsHost.Content = ActivatorUtilities.CreateInstance<WindowsDiagnosticsView>(services);
        ComputerUseHost.Content = ActivatorUtilities.CreateInstance<ComputerUseSettingsView>(services);
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ComputerUseHost.Content is ComputerUseSettingsView computer && !await computer.TrySaveAsync()) return;
        if (AiSettingsHost.Content is AiSettingsView ai && !await ai.TrySaveAsync()) return;
        if (ProjectSettingsHost.Content is ProjectSettingsView projects && !await projects.TrySaveAsync()) return;
        if (FileSettingsHost.Content is FileIntelligenceSettingsView files && !await files.TrySaveAsync()) return;
        if (DeveloperSettingsHost.Content is DeveloperSettingsView developer && !await developer.TrySaveAsync()) return;
        if (await _viewModel.SaveAsync()) Close();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Reset_Click(object sender, RoutedEventArgs e) => _windows.ResetPosition();
    private void Permissions_Click(object sender, RoutedEventArgs e) { Close(); _windows.ShowPermissions(); }
    private void Logs_Click(object sender, RoutedEventArgs e) => _viewModel.OpenLogs();
}

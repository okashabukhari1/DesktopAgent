using System.Windows;
using System.Windows.Controls;
using DesktopAgent.Core.Permissions;

namespace DesktopAgent.App.Views;

public partial class PermissionsView : System.Windows.Controls.UserControl
{
    private readonly IPolicyService _policy;
    private bool _loading;

    public PermissionsView(IPolicyService policy)
    {
        InitializeComponent();
        _policy = policy;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private Task RefreshAsync()
    {
        _loading = true;
        try
        {
            var rows = _policy.Capabilities
                .Where(item => item.Availability == CapabilityAvailability.Available)
                .GroupBy(item => item.Category)
                .Select(group => new CapabilityGroup(group.Key, group.Select(capability => new CapabilityRow(
                    capability,
                    _policy.GetPolicy(capability.Id),
                    capability.Availability == CapabilityAvailability.Available,
                    string.Join(" · ", new[] { capability.Availability == CapabilityAvailability.Available ? $"Risk: {capability.Risk}" : "Coming Later", capability.Requirement }
                        .Concat(AgentCapabilities.RequiredBy(capability.Id).Select(value => $"Used by: {value}"))))).ToArray()))
                .ToArray();
            CapabilityGroups.ItemsSource = rows;
        }
        finally { _loading = false; }
        return Task.CompletedTask;
    }

    private async void Policy_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || sender is not System.Windows.Controls.ComboBox { Tag: string id, SelectedItem: PermissionPolicy policy }) return;
        try
        {
            await _policy.SetPolicyAsync(id, policy);
            StatusText.Text = "Permission saved.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not save permission: {exception.Message}";
            await RefreshAsync();
        }
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show("Reset all permissions to their conservative defaults?", "Reset permissions",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        await _policy.ResetAsync();
        await RefreshAsync();
        StatusText.Text = "Permissions reset to defaults.";
    }

    private sealed record CapabilityGroup(string Name, IReadOnlyList<CapabilityRow> Items);

    private sealed class CapabilityRow(AgentCapability capability, PermissionPolicy policy, bool canConfigure, string details)
    {
        public string Id => capability.Id;
        public string Name => capability.Name;
        public string Description => capability.Description;
        public string Details => details;
        public bool CanConfigure => canConfigure;
        public PermissionPolicy Policy { get; } = policy;
        public IReadOnlyList<PermissionPolicy> Options { get; } = Enum.GetValues<PermissionPolicy>();
    }
}

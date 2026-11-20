using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopAgent.Core.Permissions;

namespace DesktopAgent.App.Services;

public sealed class AgentActionApprovalService : IAgentActionApprovalService
{
    public async Task<PermissionApproval> RequestAsync(AgentCapability capability, string target, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return PermissionApproval.Deny;
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        var prompt = await dispatcher.InvokeAsync(() => ShowPrompt(capability, target, cancellationToken));
        return await prompt;
    }

    private static Task<PermissionApproval> ShowPrompt(AgentCapability capability, string target, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromResult(PermissionApproval.Deny);
        var completion = new TaskCompletionSource<PermissionApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = PermissionApproval.Deny;
        var dialog = new Window
        {
            Title = "Permission required",
            Width = 500,
            SizeToContent = SizeToContent.Height,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = System.Windows.Media.Brushes.White,
            Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsVisible && window.ShowInTaskbar)
        };

        var root = new StackPanel { Margin = new Thickness(26) };
        root.Children.Add(new TextBlock { Text = "The assistant is requesting permission", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 38, 55)), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = capability.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 0) });
        root.Children.Add(new TextBlock { Text = capability.Description, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(91, 99, 119)) });
        root.Children.Add(new TextBlock { Text = $"Requested action: {target}\nRisk: {capability.Risk}", Margin = new Thickness(0, 15, 0, 0), TextWrapping = TextWrapping.Wrap });

        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        var deny = new System.Windows.Controls.Button { Content = "Deny", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 7, 12, 7) };
        var allowOnce = new System.Windows.Controls.Button { Content = "Allow once", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 7, 12, 7) };
        var alwaysAllow = new System.Windows.Controls.Button { Content = "Always allow", MinWidth = 110, Padding = new Thickness(12, 7, 12, 7) };
        deny.Click += (_, _) => { result = PermissionApproval.Deny; dialog.Close(); };
        allowOnce.Click += (_, _) => { result = PermissionApproval.AllowOnce; dialog.Close(); };
        alwaysAllow.Click += (_, _) => { result = PermissionApproval.AlwaysAllow; dialog.Close(); };
        buttons.Children.Add(deny);
        buttons.Children.Add(allowOnce);
        if (capability.Id is not (CapabilityIds.ApplicationClose or CapabilityIds.WorkspaceModify or CapabilityIds.ProjectModifyCatalog or DesktopAgent.Core.ComputerUse.ComputerUseCapabilities.Actions))
            buttons.Children.Add(alwaysAllow);
        root.Children.Add(buttons);
        dialog.Content = root;
        dialog.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { result = PermissionApproval.Deny; dialog.Close(); } };
        var registration = cancellationToken.Register(() => dialog.Dispatcher.BeginInvoke(() =>
        {
            result = PermissionApproval.Deny;
            if (dialog.IsVisible) dialog.Close();
        }));
        dialog.Closed += (_, _) => { registration.Dispose(); completion.TrySetResult(result); };
        if (cancellationToken.IsCancellationRequested)
        {
            registration.Dispose();
            return Task.FromResult(PermissionApproval.Deny);
        }
        // Keep the Assistant's Stop control usable while approval is pending.
        dialog.Show();
        return completion.Task;
    }
}

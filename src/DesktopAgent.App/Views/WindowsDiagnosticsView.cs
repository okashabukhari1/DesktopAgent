using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Infrastructure.UiAwareness;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace DesktopAgent.App.Views;

public sealed class WindowsDiagnosticsView : UserControl
{
    private readonly TextBox _output = new() { IsReadOnly = true, AcceptsReturn = true, Height = 420,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private CancellationTokenSource? _active;
    public WindowsDiagnosticsView(WindowsDiagnosticsService diagnostics, IPolicyService policy, IAgentActionApprovalService approval)
    {
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "Windows Diagnostics · Part 6.2", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = "Developer-only, on-demand Windows diagnostics. Local window titles and executable paths appear here, not in normal logs or AI. No passwords/text values, screenshots or input automation. Root creation is tested before children and bounded traversal.", TextWrapping = TextWrapping.Wrap });
        foreach (var (label, application) in new (string, string?)[] { ("Run Window Test", null), ("Test Calculator", "Calculator"), ("Test Notepad", "Notepad"), ("Test Windows Settings", "Settings") })
        {
            var button = new Button { Content = label, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            panel.Children.Add(button);
            button.Click += async (_, _) =>
            {
                if (_active is not null) return;
                using var request = new CancellationTokenSource(); _active = request;
                try
                {
                    var evaluation = policy.Evaluate(CapabilityIds.ScreenInspectUi);
                    if (evaluation.Outcome is PolicyOutcome.Denied or PolicyOutcome.Unavailable) { _output.Text = "Diagnostics blocked in Permissions."; return; }
                    if (evaluation.Outcome == PolicyOutcome.ConfirmationRequired &&
                        await approval.RequestAsync(AgentCapabilities.Find(CapabilityIds.ScreenInspectUi)!, "Local Windows diagnostics (not sent to AI)", request.Token) == PermissionApproval.Deny)
                    { _output.Text = "Not approved."; return; }
                    if (policy.Evaluate(CapabilityIds.ScreenInspectUi).Outcome is PolicyOutcome.Denied or PolicyOutcome.Unavailable) { _output.Text = "Permissions changed; diagnostics blocked."; return; }
                    _output.Text = "Running independent Win32 operations, then UIA root on the MTA worker…";
                    var report = await diagnostics.RunAsync(application, request.Token);
                    var text = new StringBuilder($"WINDOW DIAGNOSTIC\nCorrelation: {report.CorrelationId}\nBuild: {report.Build}\nTime: {report.Timestamp:O}\n");
                    foreach (var step in report.Steps)
                    {
                        text.AppendLine($"\n{step.Operation}\n{(step.Success ? "PASS" : "FAIL")} · {step.DurationMilliseconds:F1} ms");
                        text.AppendLine(JsonSerializer.Serialize(step.Data ?? (object?)step.Failure, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    _output.Text = text.ToString();
                }
                catch (OperationCanceledException) { _output.Text = "Diagnostics cancelled. No additional work queued."; }
                catch (Exception e) { _output.Text = JsonSerializer.Serialize(diagnostics.Failure(e, "Diagnostic interface"), new JsonSerializerOptions { WriteIndented = true }); }
                finally { _active = null; }
            };
        }
        var stop = new Button { Content = "Stop", HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        stop.Click += (_, _) => _active?.Cancel(); panel.Children.Add(stop); panel.Children.Add(_output);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Unloaded += (_, _) => _active?.Cancel();
    }
}

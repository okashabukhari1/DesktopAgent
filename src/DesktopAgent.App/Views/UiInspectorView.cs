using System.Text;
using System.Windows;
using System.Windows.Controls;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.UiAwareness;
using DesktopAgent.Infrastructure.UiAwareness;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace DesktopAgent.App.Views;

public sealed class UiInspectorView : UserControl
{
    private readonly IWindowsUiAutomationService _ui;
    private readonly IPolicyService _policy;
    private readonly IAgentActionApprovalService _approval;
    private readonly IActiveUserWindowTracker? _tracker;
    private readonly ComboBox _windows = new() { DisplayMemberPath = "Label", MinWidth = 300 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _query = new() { ToolTip = "Search name, control type, automation ID, class or non-sensitive value" };
    private readonly TextBox _tree = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 250,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly CheckBox _visible = new() { Content = "Visible only", IsChecked = true };
    private readonly CheckBox _interactive = new() { Content = "Interactive only" };
    private readonly CheckBox _enabled = new() { Content = "Enabled only" };
    private UiSnapshot? _snapshot;
    private CancellationTokenSource? _request;
    public UiInspectorView(IWindowsUiAutomationService ui, IPolicyService policy, IAgentActionApprovalService approval,
        IActiveUserWindowTracker? tracker = null)
    {
        _ui = ui; _policy = policy; _approval = approval; _tracker = tracker;
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "UI Inspector · Read-only", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = "On-demand, read-only diagnostics. Actual foreground and remembered external target are separate. No screenshots or input actions. Text values omitted. Nothing from this local inspector is sent to cloud AI.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        AddButton(panel, "Refresh", Refresh);
        panel.Children.Add(_windows); panel.Children.Add(_visible); panel.Children.Add(_interactive); panel.Children.Add(_enabled);
        AddButton(panel, "Inspect selected window", Inspect);
        AddButton(panel, "Stop inspection", () => { _request?.Cancel(); _ui.CancelInspection(); return Task.CompletedTask; });
        panel.Children.Add(_status);
        panel.Children.Add(new TextBlock { Text = "Search snapshot: name / type / automation ID / class / value" }); panel.Children.Add(_query);
        AddButton(panel, "Search captured tree", () => { ShowTree(_query.Text.Trim()); return Task.CompletedTask; });
        panel.Children.Add(_tree);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Unloaded += (_, _) => _request?.Cancel();
    }
    private void AddButton(StackPanel panel, string label, Func<Task> action)
    {
        var button = new Button { Content = label, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        panel.Children.Add(button);
        button.Click += async (_, _) =>
        {
            if (_request is not null && !label.StartsWith("Stop")) return;
            try { await action(); }
            catch (OperationCanceledException) { _status.Text = "Inspection stopped."; }
            catch (UiObservationException e) { _status.Text = UiTools.Failure(e.Code).Message; }
            catch (Exception) { _status.Text = "UI inspection unavailable. No automatic retry or input action was performed."; }
        };
    }
    private async Task<bool> Allowed(string scope, CancellationToken ct)
    {
        var evaluation = _policy.Evaluate(CapabilityIds.ScreenInspectUi);
        if (evaluation.Outcome is PolicyOutcome.Denied or PolicyOutcome.Unavailable) { _status.Text = "Read-only UI inspection is blocked in Permissions."; return false; }
        if (evaluation.Outcome == PolicyOutcome.ConfirmationRequired && await _approval.RequestAsync(AgentCapabilities.Find(CapabilityIds.ScreenInspectUi)!, scope, ct) == PermissionApproval.Deny)
        { _status.Text = "Not approved."; return false; }
        ct.ThrowIfCancellationRequested();
        return _policy.Evaluate(CapabilityIds.ScreenInspectUi).Outcome is not (PolicyOutcome.Denied or PolicyOutcome.Unavailable);
    }
    private async Task Refresh()
    {
        using var request = new CancellationTokenSource(); _request = request;
        try
        {
            if (!await Allowed("Local inspector: list open windows (metadata only)", request.Token)) return;
            var foreground = await _ui.GetForegroundWindow(request.Token);
            var windows = await _ui.ListOpenWindows(request.Token);
            var choices = windows.Select(w => new Choice(w, $"{w.ApplicationName} · {w.Title} · 0x{w.Handle:X}")).ToArray();
            _windows.ItemsSource = choices;
            _snapshot = null; _tree.Clear();
            if (_ui is WindowsUiAutomationService native)
            {
                _tracker?.RefreshForeground();
                var last = _tracker?.LastExternalForegroundWindow is { } reference && native.Probe.Reference(reference.Handle) == reference
                    ? await _ui.GetWindowInfo(reference.Handle, request.Token) : null;
                _status.Text = $"WINDOW TRACKING\nActual Foreground: {Label(foreground)}\nLast External: {Label(last)}\n";
                try
                {
                    var resolved = native.Targets.Resolve();
                    _windows.SelectedItem = choices.FirstOrDefault(c => c.Window.Handle == resolved.Window.Handle);
                    _status.Text += $"\nTARGET RESOLUTION\nResolved Target: {Label(resolved.Window)}\nSource: {resolved.Source}\n" + Win32(resolved.Window);
                    await Capture(resolved.Window.Handle, request.Token);
                }
                catch (UiObservationException e) { _status.Text += "\nCategory: " + e.Code + "\n" + UiTools.Failure(e.Code).Message; }
            }
            else _status.Text = "Actual foreground: " + Label(foreground);
        }
        finally { _request = null; }
    }
    private async Task Inspect()
    {
        if (_windows.SelectedItem is not Choice choice) { _status.Text = "Refresh, then select one currently open window."; return; }
        using var request = new CancellationTokenSource(); _request = request;
        _snapshot = null; _tree.Clear();
        try
        {
            var identity = (_ui as WindowsUiAutomationService)?.WindowIdentity(choice.Window.Handle);
            if (!await Allowed("Local inspector: selected window handle " + choice.Window.Handle, request.Token)) return;
            if (identity is not null && identity != (_ui as WindowsUiAutomationService)?.WindowIdentity(choice.Window.Handle)) throw new UiObservationException("StaleElement");
            var window = await _ui.GetWindowInfo(choice.Window.Handle, request.Token) ?? throw new UiObservationException("WindowClosed");
            _status.Text = "TARGET RESOLUTION\nExplicit selected target: " + Label(window) + "\n" + Win32(window);
            await Capture(window.Handle, request.Token);
        }
        finally { _request = null; }
    }
    private static string Label(WindowContext? w) => w is null ? "Unavailable / closed" : $"{w.ApplicationName} · 0x{w.Handle:X}";
    private static string Win32(WindowContext w) => $"\nWIN32\nValid: True\nHWND: 0x{w.Handle:X}\nPID: {w.ProcessId}\nProcess: {w.ProcessName}\nApplication: {w.ApplicationName}\nTitle: {w.Title}\nBounds: {w.Bounds}\nVisible: {w.IsVisible}\nActual foreground: {w.IsForeground}\n";
    private async Task Capture(long handle, CancellationToken ct)
    {
        try
        {
            _snapshot = await _ui.GetWindowTree(handle, new(MaximumDepth: 4, MaximumElements: 100, VisibleOnly: _visible.IsChecked == true,
                InteractiveOnly: _interactive.IsChecked == true, EnabledOnly: _enabled.IsChecked == true), ct);
            _status.Text += $"\nUI AUTOMATION\nProvider: Available\nRoot Element: {_snapshot.Window.RootElement?.ControlType}\nInteractive Elements: {_snapshot.InteractiveElements.Count}\nFocused: {_snapshot.FocusedElement?.ControlType ?? "Unavailable"}\n{_snapshot.Limitation}\nBounded: {_snapshot.Truncated}. References expire after two seconds.";
            ShowTree("");
        }
        catch (UiObservationException e)
        {
            _status.Text += $"\nUI AUTOMATION\nProvider: Unavailable\nCategory: {e.Code}\n" + UiTools.Failure(e.Code).Message;
            _tree.Text = "Window metadata above remains independent of accessibility. No controls were inferred.";
        }
    }
    private void ShowTree(string query)
    {
        if (_snapshot is null) { _tree.Text = "Inspect a selected window first."; return; }
        var text = new StringBuilder();
        if (!UiAlgorithms.IsFresh(_snapshot, DateTimeOffset.UtcNow)) text.AppendLine("HISTORICAL SNAPSHOT — refresh before using any element reference.");
        foreach (var e in _snapshot.Elements.Where(e => query.Length == 0 || new[] { e.Name, e.ControlType, e.AutomationId, e.ClassName, e.Value }.Any(v => v?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)))
        {
            var depth = 0; var parent = e.ParentId;
            while (parent is not null && depth < 8) { depth++; parent = _snapshot.Elements.FirstOrDefault(p => p.Id == parent)?.ParentId; }
            text.Append(' ', depth * 2).Append(e.Id).Append(" · ").Append(e.ControlType).Append(" · ").Append(e.Name)
                .Append(" · enabled=").Append(e.IsEnabled?.ToString() ?? "unknown").Append(" · visible=").Append(e.IsVisible?.ToString() ?? "unknown")
                .Append(" · selected=").Append(e.IsSelected?.ToString() ?? "unknown").Append(" · ID=").Append(e.AutomationId).Append(" · class=").Append(e.ClassName);
            if (e.Value is not null) text.Append(" · ").Append(e.Value);
            text.AppendLine();
        }
        _tree.Text = text.ToString();
    }
    private sealed record Choice(WindowContext Window, string Label);
}

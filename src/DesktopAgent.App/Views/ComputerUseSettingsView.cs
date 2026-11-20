using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using DesktopAgent.Core.ComputerUse;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Infrastructure.ComputerUse;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.UiAwareness;
using DesktopAgent.Infrastructure.UiAwareness;
using System.Diagnostics;
using System.Text.Json;

namespace DesktopAgent.App.Views;

public sealed class ComputerUseSettingsView : UserControl
{
    private readonly ISettingsService _settings;
    private readonly ComputerUseSession _session;
    private readonly CheckBox _enabled = new() { Content = "Enable controlled Computer Use", Margin = new Thickness(0, 12, 0, 12) };
    private readonly CheckBox _acknowledged = new() { Content = "I understand that enabled actions can change the selected application", Margin = new Thickness(0, 8, 0, 8) };
    private readonly CheckBox _pause = new() { Content = "Pause when I use my keyboard or mouse" };
    private readonly ComboBox _mode = new() { ItemsSource = Enum.GetValues<ComputerUseMode>(), Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox _trusted = new() { Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _actions = new(), _failures = new(), _duration = new(), _rounds = new();
    private readonly TextBox _pythonPath = new() { Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _backendStatus = new() { TextWrapping = TextWrapping.Wrap };
    public ComputerUseSettingsView(ISettingsService settings, ComputerUseSession session, PythonComputerBackend python,
        WindowsUiAutomationService native, IPolicyService policy, IAgentActionApprovalService approval)
    {
        _settings = settings; _session = session;
        var value = settings.Current.ComputerUse;
        _enabled.IsChecked = value.Enabled; _acknowledged.IsChecked = value.OnboardingAcknowledged;
        _pause.IsChecked = value.PauseOnUserInput; _mode.SelectedItem = value.Mode; _trusted.Text = string.Join(", ", value.TrustedApplications);
        _actions.Text = value.MaximumActions.ToString(); _failures.Text = value.MaximumFailures.ToString();
        _duration.Text = value.MaximumTaskSeconds.ToString(); _rounds.Text = value.MaximumPlanningRounds.ToString();
        _pythonPath.Text = value.PythonExecutable ?? "";
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "Controlled Computer Use", FontSize = 24 });
        panel.Children.Add(new TextBlock { Text = "Disabled by default. Actions run only during your explicit requests, with a visible assistant. Observe never acts; Assist asks for consequential actions; Trusted Actions trusts ordinary actions only in the process names below. High-risk actions still ask. Secrets, security dialogs and elevated apps remain protected.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(_enabled); panel.Children.Add(_acknowledged); panel.Children.Add(_mode); panel.Children.Add(_pause);
        panel.Children.Add(new CheckBox { Content = "Show action indicator (required while controlling the desktop)", IsChecked = true, IsEnabled = false, Margin = new Thickness(0, 8, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Trusted process names, comma-separated (for example Notepad)" }); panel.Children.Add(_trusted);
        panel.Children.Add(new TextBlock { Text = $"Emergency STOP: Ctrl+Alt+Shift+F11. Visible STOP cancels planning and queued input. Limits: {value.MaximumActions} actions / {value.MaximumFailures} failures / {value.MaximumTaskSeconds}s. Native calls already dispatched cannot be undone.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        foreach (var pair in new (string Label, TextBox Input)[] { ("Maximum actions (1–16)", _actions), ("Maximum consecutive failures (1–2)", _failures), ("Task duration in seconds (10–180)", _duration), ("AI planning rounds (1–12)", _rounds) })
        { panel.Children.Add(new TextBlock { Text = pair.Label, Margin = new Thickness(0, 6, 0, 0) }); panel.Children.Add(pair.Input); }
        panel.Children.Add(_status);
        panel.Children.Add(new TextBlock { Text = "Execution Backend", FontSize = 20, Margin = new Thickness(0, 16, 0, 6) });
        panel.Children.Add(new TextBlock { Text = "Native Windows: Available\nPython executable (blank = discover installed Python)" });
        panel.Children.Add(_pythonPath); panel.Children.Add(_backendStatus);
        var health = new System.Windows.Controls.Button { Content = "Test Python Worker", Margin = new Thickness(0, 6, 0, 6) };
        health.Click += async (_, _) =>
        {
            health.IsEnabled = false;
            try
            {
                var result = await python.HealthAsync();
                _backendStatus.Text = $"Python Worker: {(result.Verified ? "Available" : "Unavailable")}\nPython version: {python.PythonVersion ?? "Unknown"}\nWorker status: {python.Status}\nLast execution error: {python.LastError ?? "None"}";
            }
            catch (Exception) { _backendStatus.Text = "Python health check failed. No desktop input was sent."; }
            finally { health.IsEnabled = true; }
        };
        panel.Children.Add(health);
        var test = new System.Windows.Controls.Button { Content = "Test policy → Python (disposable Notepad)", Margin = new Thickness(0, 6, 0, 6) };
        test.Click += async (_, _) =>
        {
            if (!session.CanAct || session.IsActive)
            { _backendStatus.Text = "Save enabled Computer Use first, open Assistant, and finish any active task. Observe mode cannot type."; return; }
            test.IsEnabled = false;
            try
            {
                session.Begin(CancellationToken.None, "Open Notepad and type Hello World.");
                await System.Windows.Threading.Dispatcher.Yield();
                async Task Permit(string id)
                {
                    var evaluation = policy.Evaluate(id);
                    if (evaluation.Outcome is PolicyOutcome.Denied or PolicyOutcome.Unavailable) throw new InvalidOperationException("PermissionDenied");
                    if (evaluation.Outcome == PolicyOutcome.ConfirmationRequired)
                    {
                        session.Update(ComputerTaskState.WaitingForUser, "Waiting for disposable Notepad test permission");
                        if (await approval.RequestAsync(AgentCapabilities.Find(id)!, "Disposable Notepad test", session.Token) == PermissionApproval.Deny)
                            throw new InvalidOperationException("PermissionDenied");
                    }
                    session.Token.ThrowIfCancellationRequested();
                    if (policy.Evaluate(id).Outcome is PolicyOutcome.Denied or PolicyOutcome.Unavailable) throw new InvalidOperationException("PermissionDenied");
                }
                await Permit(CapabilityIds.ApplicationOpen);
                var folder = System.IO.Path.Combine(AppContext.BaseDirectory, "part72-smoke");
                System.IO.Directory.CreateDirectory(folder);
                var document = System.IO.Path.Combine(folder, "agent-policy-smoke-" + Guid.NewGuid().ToString("N") + ".txt");
                using (System.IO.File.Create(document)) { }
                session.Update(ComputerTaskState.Observing, "Opening disposable Notepad");
                var launch = new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe")) { UseShellExecute = false };
                launch.ArgumentList.Add(document);
                using var process = Process.Start(launch);
                WindowContext? window = null;
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    var candidates = (await native.ListOpenWindows(session.Token)).Where(w => w.ApplicationName == "Notepad" &&
                        (w.Title == System.IO.Path.GetFileName(document) + " - Notepad" || w.Title == System.IO.Path.GetFileNameWithoutExtension(document) + " - Notepad")).ToArray();
                    if (candidates.Length > 1) throw new UiObservationException("AmbiguousWindow");
                    if (candidates.Length == 1) { window = candidates[0]; break; }
                    await Task.Delay(250, session.Token);
                }
                if (window is null) throw new UiObservationException("WindowNotFound");
                await Permit(CapabilityIds.ScreenInspectUi);
                session.Update(ComputerTaskState.Observing, "Inspecting disposable Notepad editor");
                var snapshot = await native.GetWindowTree(window.Handle, ct: session.Token);
                var editors = UiAlgorithms.Flatten(snapshot.Window.RootElement!).Where(e => e.ControlType is "Edit" or "Document" &&
                    e.ClassName is "Edit" or "RichEditD2DPT" or "RichEditD2D" or "RichEdit50W").ToArray();
                if (editors.Length != 1) throw new UiObservationException("EditorUnavailableOrAmbiguous");
                var action = new ComputerAction(Guid.NewGuid(), ComputerActionType.TypeText, "window:Notepad", snapshot.SnapshotVersion, editors[0].Id, "Hello World");
                var executor = new ComputerActionExecutor(python, session, settings, policy, approval);
                var result = await executor.ExecuteAsync(action, session.Token);
                _backendStatus.Text = result.Status + ": " + result.Message;
                await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(AppContext.BaseDirectory, "part72-policy-result.json"), JsonSerializer.Serialize(result));
                if (result.Verified) session.Update(ComputerTaskState.Completed, "Policy → Python test verified");
            }
            catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or UiObservationException)
            { _backendStatus.Text = "Policy test stopped: " + (error is UiObservationException observation ? observation.Code : error is OperationCanceledException ? "Cancelled" : error.Message); }
            catch (Exception) { _backendStatus.Text = "Policy test failed. No automatic retry."; }
            finally { session.End(); test.IsEnabled = true; }
        };
        panel.Children.Add(test);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => _status.Text = session.Summary;
    }
    public async Task<bool> TrySaveAsync()
    {
        if (!int.TryParse(_actions.Text, out var actions) || actions is < 1 or > 16 || !int.TryParse(_failures.Text, out var failures) || failures is < 1 or > 2 ||
            !int.TryParse(_duration.Text, out var duration) || duration is < 10 or > 180 || !int.TryParse(_rounds.Text, out var rounds) || rounds is < 1 or > 12)
        { _status.Text = "Enter limits within the displayed ranges."; return false; }
        if (_enabled.IsChecked == true && (_acknowledged.IsChecked != true || !_session.EmergencyStopReady || !_session.InterruptionMonitorReady))
        { _status.Text = "Enable requires your acknowledgment and working emergency STOP/input monitoring. Check for a shortcut conflict."; return false; }
        _session.Stop();
        var next = _settings.Current.Clone();
        next.ComputerUse.Enabled = _enabled.IsChecked == true; next.ComputerUse.OnboardingAcknowledged = _acknowledged.IsChecked == true;
        next.ComputerUse.Mode = _mode.SelectedItem is ComputerUseMode mode ? mode : ComputerUseMode.Observe;
        next.ComputerUse.PauseOnUserInput = _pause.IsChecked == true;
        next.ComputerUse.MaximumActions = actions; next.ComputerUse.MaximumFailures = failures;
        next.ComputerUse.MaximumTaskSeconds = duration; next.ComputerUse.MaximumPlanningRounds = rounds;
        next.ComputerUse.TrustedApplications = _trusted.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (!string.IsNullOrWhiteSpace(_pythonPath.Text) && (!System.IO.Path.IsPathFullyQualified(_pythonPath.Text.Trim()) || !System.IO.File.Exists(_pythonPath.Text.Trim())))
        { _status.Text = "Enter an existing full Python executable path, or leave it blank for discovery."; return false; }
        next.ComputerUse.PythonExecutable = string.IsNullOrWhiteSpace(_pythonPath.Text) ? null : _pythonPath.Text.Trim();
        await _settings.SaveAsync(next); return true;
    }
}

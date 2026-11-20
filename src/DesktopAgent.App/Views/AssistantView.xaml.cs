using System.Windows;
using System.Windows.Input;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Infrastructure.Ai;

namespace DesktopAgent.App.Views;

public partial class AssistantView : System.Windows.Controls.UserControl
{
    private readonly IAssistantConversationService _conversation;
    private readonly ICredentialStore _credentials;
    private readonly ISettingsService _settings;
    private readonly IAiConnectionService _connection;
    private readonly IWindowService _windows;
    private readonly ICharacterService _character;
    private readonly DesktopAgent.Core.Projects.CurrentProjectContext _currentProject;
    private readonly DesktopAgent.Core.Developer.IDeveloperSessionManager _developer;
    private readonly DesktopAgent.Core.UiAwareness.IWindowsUiAutomationService _ui;
    private readonly DesktopAgent.Infrastructure.ComputerUse.ComputerUseSession _computer;
    private bool _visibleRegistered;
    public bool IsCompact { get; set; }
    public AssistantView(IAssistantConversationService conversation, ICredentialStore credentials, ISettingsService settings,
        IAiConnectionService connection, IWindowService windows, ICharacterService character, DesktopAgent.Core.Projects.CurrentProjectContext currentProject,
        DesktopAgent.Core.Developer.IDeveloperSessionManager developer, DesktopAgent.Core.UiAwareness.IWindowsUiAutomationService ui,
        DesktopAgent.Infrastructure.ComputerUse.ComputerUseSession computer)
    {
        InitializeComponent();
        _conversation = conversation; _credentials = credentials; _settings = settings; _connection = connection; _windows = windows;
        _character = character;
        _currentProject = currentProject;
        _developer = developer;
        _ui = ui;
        _computer = computer;
        Loaded += (_, _) => { _computer.Changed += RefreshEvent; RegisterVisibility(); };
        Unloaded += (_, _) => { _computer.Changed -= RefreshEvent; if (_visibleRegistered) { _visibleRegistered = false; _computer.ViewVisible(false); } };
        IsVisibleChanged += (_, _) => RegisterVisibility();
        Loaded += (_, _) => _ui.Changed += RefreshEvent;
        Unloaded += (_, _) => _ui.Changed -= RefreshEvent;
        Loaded += (_, _) => _developer.Changed += RefreshEvent;
        Unloaded += (_, _) => _developer.Changed -= RefreshEvent;
        Loaded += (_, _) => _currentProject.Changed += RefreshEvent;
        Unloaded += (_, _) => _currentProject.Changed -= RefreshEvent;
        Loaded += (_, _) => { _conversation.Changed += RefreshEvent; _connection.Changed += RefreshEvent; _character.StateChanged += CharacterChanged; Refresh(); };
        Unloaded += (_, _) => { _conversation.Changed -= RefreshEvent; _connection.Changed -= RefreshEvent; _character.StateChanged -= CharacterChanged; };
    }
    private void RefreshEvent(object? sender, EventArgs e) { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(Refresh); }
    private void RegisterVisibility()
    {
        var visible = IsLoaded && IsVisible;
        if (visible == _visibleRegistered) return;
        _visibleRegistered = visible; _computer.ViewVisible(visible);
    }
    private void CharacterChanged(object? sender, Core.Enums.CharacterState state) => RefreshEvent(sender, EventArgs.Empty);
    private void Refresh()
    {
        Header.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
        var inspected = _ui.LastSnapshot;
        UiObservationLabel.Visibility = _ui.IsInspecting || inspected is not null ? Visibility.Visible : Visibility.Collapsed;
        UiObservationLabel.Text = _ui.IsInspecting ? "Inspecting window… (accessibility only)" : inspected is null ? "" :
            $"Last window inspection: {inspected.Window.ProcessName} · {inspected.Window.Title} · {inspected.Timestamp:T} · {inspected.InteractiveElements.Count} controls (snapshot, not recording)";
        DeveloperCard.Visibility = _settings.Current.Developer.Enabled && IsCompact && _developer.Sessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var dev = _developer.Sessions.FirstOrDefault();
        DeveloperCard.Content = dev is null ? "View developer sessions" : dev.SourceTool + " · " + dev.State + " · View Session";
        FullButton.Visibility = IsCompact ? Visibility.Visible : Visibility.Collapsed;
        Messages.ItemsSource = _conversation.Entries.Where(entry => entry.Action is null || _settings.Current.Ai.ShowToolActivity || entry.Action.Choices is { Count: > 0 } || entry.Action.Sources is { Count: > 0 })
            .Select(entry => new DisplayEntry(entry)).ToArray();
        EmptyState.Visibility = _conversation.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SendButton.IsEnabled = !_conversation.IsBusy;
        InputBox.IsEnabled = !_conversation.IsBusy;
        StopButton.IsEnabled = true;
        StopButton.Content = "STOP";
        NewButton.IsEnabled = !_conversation.IsBusy;
        WorkStatus.Text = _computer.IsActive ? "Computer Use Active · " + _computer.State + " · " + _computer.Summary + " · " + _computer.ActionCount + " actions · STOP: Ctrl+Alt+Shift+F11" :
            _computer.State is DesktopAgent.Core.ComputerUse.ComputerTaskState.Paused or DesktopAgent.Core.ComputerUse.ComputerTaskState.Failed or DesktopAgent.Core.ComputerUse.ComputerTaskState.Cancelled ? _computer.Summary : _conversation.IsBusy ? _conversation.State + "…" : "";
        CurrentProjectLabel.Text = _currentProject.Current is { } project ? "Current project: " + project.Name : "No current project";
        ClearProjectButton.IsEnabled = !_conversation.IsBusy && _currentProject.Current is not null;
        try
        {
            var credentialExists = _credentials.Exists(ProviderCredentialNames.ForBaseUrl(_settings.Current.Ai.BaseUrl));
            ConnectionStatus.Text = !_settings.Current.Ai.Enabled ? "AI off · Direct commands available" : !credentialExists
                ? "Connect AI in Settings · Direct commands available"
                : _connection.Status.ConfigurationId == AssistantConversationService.ConfigurationId(_settings) && _connection.Status.State == AiConnectionState.Connected
                    ? "Connected · " + _settings.Current.Ai.Model : _connection.Status.State == AiConnectionState.Connected
                        ? "Configuration changed · Test connection" : _connection.Status.Message;
        }
        catch { ConnectionStatus.Text = "AI setup unavailable · Direct commands available"; }
        MessageScroll.ScrollToEnd();
    }
    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();
    private async void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Handle before TextBox consumes Enter for AcceptsReturn, in both views.
        // Shift+Enter remains native multiline editing; retain Ctrl+Enter compatibility.
        if (e.Key == Key.Enter && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control)
        {
            e.Handled = true;
            if (!e.IsRepeat) await SendAsync();
        }
    }
    private async Task SendAsync()
    {
        var text = InputBox.Text.Trim();
        if (text.Length == 0 || _conversation.IsBusy) return;
        InputBox.Clear();
        await _conversation.SendAsync(text);
        InputBox.Focus();
    }
    private async void Choice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: ApplicationChoice choice } && !_conversation.IsBusy)
            await _conversation.ChooseApplicationAsync(choice.Id);
    }
    private void Stop_Click(object sender, RoutedEventArgs e) => _conversation.Stop();
    private async void Source_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: DesktopAgent.Core.Projects.CodeSourceReference source } && !_conversation.IsBusy)
            await _conversation.OpenSourceAsync(source);
    }
    private void ClearProject_Click(object sender, RoutedEventArgs e) => _currentProject.Clear();
    private void New_Click(object sender, RoutedEventArgs e) => _conversation.Reset();
    private void Full_Click(object sender, RoutedEventArgs e) => _windows.ShowAssistant();
    private void Developer_Click(object sender, RoutedEventArgs e) => _windows.ShowDeveloper(_developer.Sessions.FirstOrDefault()?.Id);
    private sealed class DisplayEntry(ConversationEntry entry)
    {
        public string Speaker => entry.Speaker == "Action" ? (entry.Action!.Success ? "✓ " : "⚠ ") + entry.Action.Status : entry.Speaker;
        public string Text => entry.Text;
        public System.Windows.HorizontalAlignment Alignment => entry.Speaker == "You" ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
        public IReadOnlyList<ApplicationChoice> Choices => entry.Action?.Choices ?? [];
        public IReadOnlyList<DesktopAgent.Core.Projects.CodeSourceReference> Sources => entry.Action?.Sources?.Take(6).ToArray() ?? [];
    }
}

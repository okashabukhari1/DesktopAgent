using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Models;
using DesktopAgent.Core.Permissions;

namespace DesktopAgent.App.Views;

public partial class OnboardingWindow : Window
{
    public const int CurrentVersion = 2;
    private readonly ISettingsService _settings;
    private readonly IPolicyService _policy;
    private readonly AiSettingsView _aiSettings;
    private int _step = 1;
    private readonly List<(string Id, System.Windows.Controls.ComboBox Picker)> _choices = [];

    private static readonly (string Title, string Body)[] Pages =
    [
        ("Welcome", "Your personal Windows assistant. Open applications, manage saved workspaces, and ask questions about supported desktop state. You remain in control."),
        ("How it works", "You ask in text. The AI can propose a structured computer action. Your permission policy checks that action, then the existing Windows controller performs it and returns the real result. The AI cannot directly control Windows."),
        ("AI setup", "Connect an OpenAI-compatible provider in Settings → AI to use natural-language requests. You can skip that setup for now; direct commands continue to work without AI."),
        ("Application access", "Application discovery is required to find installed applications. Choose what the agent may do with that catalog. You can change every choice later in Permissions."),
        ("Workspaces", "Workspaces can launch the applications and entries you saved in a workspace. Choose whether the assistant may view, launch, or modify them."),
        ("File access", "File intelligence is not available in this phase. The agent does not read file contents, create or modify files, or delete files. Opening a selected file or folder only asks Windows to open it."),
        ("Privacy", "This version does not use a microphone, record audio, or continuously capture your screen. When AI is enabled, your request and the conversation context needed to answer it are sent to your configured provider. Approved tool results are returned for that request. Recent action activity is stored locally."),
        ("Safety", "Sensitive actions can require your confirmation. Denied capabilities stay blocked. Windows UAC and administrator prompts remain controlled by Windows; the agent does not bypass them."),
        ("You're ready", "Try “Open Spotify”, “What applications are running?”, “Do I have Postman installed?”, or “Start my programming workspace”. AI can be configured later in Settings → AI.")
    ];

    public OnboardingWindow(ISettingsService settings, IPolicyService policy, AiSettingsView aiSettings)
    {
        InitializeComponent();
        _settings = settings;
        _policy = policy;
        _aiSettings = aiSettings;
        ShowStep();
    }

    private void ShowStep()
    {
        StepText.Text = $"GETTING STARTED  ·  {_step} OF {Pages.Length}";
        StepProgress.Value = _step;
        TitleText.Text = Pages[_step - 1].Title;
        BodyText.Text = Pages[_step - 1].Body;
        BackButton.IsEnabled = _step > 1;
        NextButton.Visibility = _step < Pages.Length ? Visibility.Visible : Visibility.Collapsed;
        OpenAssistantButton.Visibility = _step == Pages.Length ? Visibility.Visible : Visibility.Collapsed;
        ReviewPermissionsButton.Visibility = _step == Pages.Length ? Visibility.Visible : Visibility.Collapsed;
        SkipAiButton.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        ChoicePanel.Children.Clear();
        _choices.Clear();
        if (_step == 3)
        {
            ChoicePanel.Children.Add(_aiSettings);
        }
        else if (_step == 4)
        {
            AddChoice(CapabilityIds.ApplicationDiscover);
            AddChoice(CapabilityIds.ApplicationOpen);
            AddChoice(CapabilityIds.ApplicationFocus);
            AddChoice(CapabilityIds.ApplicationWindowControl);
            AddChoice(CapabilityIds.ApplicationClose);
        }
        else if (_step == 5)
        {
            AddChoice(CapabilityIds.WorkspaceView);
            AddChoice(CapabilityIds.WorkspaceLaunch);
            AddChoice(CapabilityIds.WorkspaceModify);
        }
    }

    private void AddChoice(string id)
    {
        var capability = AgentCapabilities.Find(id)!;
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        row.Children.Add(new TextBlock { Text = capability.Name, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        var picker = new System.Windows.Controls.ComboBox { ItemsSource = Enum.GetValues<PermissionPolicy>(), SelectedItem = _policy.GetPolicy(id), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(picker, 1);
        row.Children.Add(picker);
        ChoicePanel.Children.Add(row);
        _choices.Add((id, picker));
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 3 && !await _aiSettings.TrySaveAsync()) return;
        await SaveChoicesAsync();
        _step = Math.Min(Pages.Length, _step + 1);
        ShowStep();
    }

    private void SkipAi_Click(object sender, RoutedEventArgs e)
    {
        _step = Math.Min(Pages.Length, _step + 1);
        ShowStep();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _step = Math.Max(1, _step - 1);
        ShowStep();
    }

    private async void OpenAssistant_Click(object sender, RoutedEventArgs e)
    {
        await CompleteAsync();
        Tag = "Assistant";
        DialogResult = true;
    }

    private async void ReviewPermissions_Click(object sender, RoutedEventArgs e)
    {
        await CompleteAsync();
        Tag = "Permissions";
        DialogResult = true;
    }

    private async Task SaveChoicesAsync()
    {
        foreach (var (id, picker) in _choices)
            if (picker.SelectedItem is PermissionPolicy policy)
                await _policy.SetPolicyAsync(id, policy);
    }

    private async Task CompleteAsync()
    {
        await SaveChoicesAsync();
        var updated = _settings.Current.Clone();
        updated.FirstRunCompleted = true;
        updated.OnboardingCompleted = true;
        updated.OnboardingVersion = CurrentVersion;
        updated.OnboardingCompletedAt = DateTimeOffset.Now;
        await _settings.SaveAsync(updated);
    }
}

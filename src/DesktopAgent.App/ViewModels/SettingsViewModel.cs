using System.Diagnostics;
using System.IO;
using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IStartupService _startupService;
    private readonly ICharacterService _characterService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly AppSettings _working;
    private string? _errorMessage;

    public SettingsViewModel(ISettingsService settingsService, IStartupService startupService,
        ICharacterService characterService, ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _characterService = characterService;
        _logger = logger;
        _working = settingsService.Current.Clone();
        _working.StartWithWindows = startupService.IsEnabled();
    }

    public bool StartWithWindows { get => _working.StartWithWindows; set { _working.StartWithWindows = value; OnPropertyChanged(); } }
    public bool AlwaysOnTop { get => _working.AlwaysOnTop; set { _working.AlwaysOnTop = value; OnPropertyChanged(); } }
    public bool RememberPosition { get => _working.RememberPosition; set { _working.RememberPosition = value; OnPropertyChanged(); } }
    public bool StartMinimized { get => _working.StartMinimized; set { _working.StartMinimized = value; OnPropertyChanged(); } }
    public bool ShowCharacterOnStartup { get => _working.ShowCharacterOnStartup; set { _working.ShowCharacterOnStartup = value; OnPropertyChanged(); } }
    public double CharacterSize { get => _working.CharacterSize; set { _working.CharacterSize = value; OnPropertyChanged(); } }
    public bool AnimationEnabled { get => _working.AnimationEnabled; set { _working.AnimationEnabled = value; OnPropertyChanged(); } }
    public double AnimationIntensity { get => _working.AnimationIntensity; set { _working.AnimationIntensity = value; OnPropertyChanged(); } }
    public AppTheme Theme { get => _working.Theme; set { _working.Theme = value; OnPropertyChanged(); } }
    public CharacterState DeveloperState { get => _characterService.State; set { _characterService.SetState(value); OnPropertyChanged(); } }
    public Array Themes => Enum.GetValues(typeof(AppTheme));
    public Array States => Enum.GetValues(typeof(CharacterState));
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public async Task<bool> SaveAsync()
    {
        try
        {
            if (_working.StartWithWindows != _startupService.IsEnabled())
                _startupService.SetEnabled(_working.StartWithWindows);
            _working.Position = _settingsService.Current.Position;
            _working.FirstRunCompleted = _settingsService.Current.FirstRunCompleted;
            _working.OnboardingCompleted = _settingsService.Current.OnboardingCompleted;
            _working.OnboardingVersion = _settingsService.Current.OnboardingVersion;
            _working.OnboardingCompletedAt = _settingsService.Current.OnboardingCompletedAt;
            _working.Ai = _settingsService.Current.Ai.Clone();
            _working.Projects = _settingsService.Current.Projects.Copy();
            _working.FileIntelligence = _settingsService.Current.FileIntelligence.Copy();
            _working.Developer = _settingsService.Current.Developer.Copy();
            _working.ComputerUse = _settingsService.Current.ComputerUse.Copy();
            await _settingsService.SaveAsync(_working);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Settings could not be saved");
            ErrorMessage = exception.Message;
            return false;
        }
    }

    public void OpenLogs()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAgent", "logs");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }
}

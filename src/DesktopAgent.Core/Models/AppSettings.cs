using DesktopAgent.Core.Enums;

namespace DesktopAgent.Core.Models;

public sealed class AppSettings
{
    public const double DefaultCharacterSize = 148;

    public bool StartWithWindows { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool RememberPosition { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool ShowCharacterOnStartup { get; set; } = true;
    public double CharacterSize { get; set; } = DefaultCharacterSize;
    public bool AnimationEnabled { get; set; } = true;
    public double AnimationIntensity { get; set; } = 0.55;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public CharacterPosition? Position { get; set; }
    public bool FirstRunCompleted { get; set; }
    public bool OnboardingCompleted { get; set; }
    public int OnboardingVersion { get; set; }
    public DateTimeOffset? OnboardingCompletedAt { get; set; }
    public AiProviderSettings Ai { get; set; } = new();
    public DesktopAgent.Core.Projects.ProjectSettings Projects { get; set; } = new();
    public DesktopAgent.Core.Projects.FileIntelligenceSettings FileIntelligence { get; set; } = new();
    public DesktopAgent.Core.Developer.DeveloperSettings Developer { get; set; } = new();
    public DesktopAgent.Core.ComputerUse.ComputerUseSettings ComputerUse { get; set; } = new();

    public AppSettings Clone() => new()
    {
        StartWithWindows = StartWithWindows,
        AlwaysOnTop = AlwaysOnTop,
        RememberPosition = RememberPosition,
        StartMinimized = StartMinimized,
        ShowCharacterOnStartup = ShowCharacterOnStartup,
        CharacterSize = CharacterSize,
        AnimationEnabled = AnimationEnabled,
        AnimationIntensity = AnimationIntensity,
        Theme = Theme,
        Position = Position is null ? null : new CharacterPosition(Position.X, Position.Y, Position.MonitorDeviceName),
        FirstRunCompleted = FirstRunCompleted,
        OnboardingCompleted = OnboardingCompleted,
        OnboardingVersion = OnboardingVersion,
        OnboardingCompletedAt = OnboardingCompletedAt,
        Ai = Ai.Clone(),
        Projects = Projects.Copy(),
        FileIntelligence = FileIntelligence.Copy(),
        Developer = Developer.Copy(),
        ComputerUse = ComputerUse.Copy()
    };
}

public sealed class AiProviderSettings
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "OpenAI-compatible";
    public string Model { get; set; } = "gpt-4o-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public int TimeoutSeconds { get; set; } = 60;
    public bool ShowToolActivity { get; set; } = true;
    public bool SaveConversationHistory { get; set; }

    public AiProviderSettings Clone() => (AiProviderSettings)MemberwiseClone();
}

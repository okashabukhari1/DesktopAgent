using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Interfaces;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DesktopAgent.Desktop.ViewModels;

public enum DesktopPage { Home, Assistant, Applications, Projects, Workspaces, Permissions, Activity, Developer, Settings }
public enum OperationStatus { Supported, Unsupported, PermissionRequired, TemporarilyUnavailable, Failed }
public sealed record OperationResult(OperationStatus Status, string Message);

public sealed class ShellViewModel(ICharacterService character, ICredentialStore credentials) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public DesktopPage SelectedPage { get; private set; }
    public AppTheme Theme { get; private set; }
    public string Draft { get; set; } = "";
    public CharacterState CharacterState => character.State;
    public bool CredentialStorageAvailable => credentials.IsAvailable;
    public OperationResult Navigate(DesktopPage page)
    {
        if (!Enum.IsDefined(page)) return new(OperationStatus.Failed, "That page is unavailable.");
        SelectedPage = page;
        Changed(nameof(SelectedPage));
        return new(OperationStatus.Supported, "Page opened.");
    }
    public OperationResult SetTheme(AppTheme theme)
    {
        if (!Enum.IsDefined(theme)) return new(OperationStatus.Failed, "Choose System, Light or Dark.");
        Theme = theme;
        Changed(nameof(Theme));
        return new(OperationStatus.Supported, "Appearance updated for this session.");
    }
    public OperationResult Submit() => new(OperationStatus.Unsupported,
        "AI chat is disconnected. No message was sent and no action was performed.");
    private void Changed([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

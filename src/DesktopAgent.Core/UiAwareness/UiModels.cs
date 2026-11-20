using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.UiAwareness;

public enum UiDialogRisk { Normal, WindowsSecuritySensitive, SecureDesktop, Unknown }
public sealed record UiElement
{
    public string Id { get; init; } = "";
    public string? ControlType { get; init; }
    private readonly string? _name;
    public string? Name { get => IsPassword == true ? "Password field" : _name; init => _name = value; }
    public string? AutomationId { get; init; }
    public string? ClassName { get; init; }
    // Values are intentionally never read from editable/text providers in this phase.
    private readonly string? _value;
    public string? Value { get => IsPassword == true ? "[PROTECTED]" : _value; init => _value = value; }
    public WindowRectangle? Bounds { get; init; }
    public bool? IsPassword { get; init; }
    public bool? IsEnabled { get; init; }
    public bool? IsVisible { get; init; }
    public bool? IsFocused { get; init; }
    public bool? IsSelected { get; init; }
    public bool? CanInvoke { get; init; }
    public bool? CanToggle { get; init; }
    public bool? CanExpandCollapse { get; init; }
    public bool? CanSelect { get; init; }
    public bool? CanSetValue { get; init; }
    public bool? CanScroll { get; init; }
    public bool? CanSetRangeValue { get; init; }
    public string? ParentId { get; init; }
    private readonly IReadOnlyList<UiElement> _children = [];
    public IReadOnlyList<UiElement> Children { get => IsPassword == true ? [] : _children; init => _children = value; }
    public bool IsInteractive => CanInvoke == true || CanToggle == true || CanExpandCollapse == true || CanSelect == true || CanSetValue == true || CanScroll == true || CanSetRangeValue == true;
}
public sealed record WindowContext(long Handle, int ProcessId, string ProcessName, string? ApplicationId,
    string? Title, WindowRectangle Bounds, bool IsVisible, bool IsEnabled, bool IsMinimized,
    bool IsMaximized, bool IsForeground, UiElement? RootElement, DateTimeOffset CapturedAt)
{
    public int? HostedProcessId { get; init; }
    public string? HostedProcessName { get; init; }
    public string ApplicationName { get; init; } = FriendlyName(ProcessName);
    public static string FriendlyName(string processName) => processName.ToLowerInvariant() switch
    {
        "notepad" => "Notepad", "calculatorapp" or "calculator" => "Calculator",
        "systemsettings" => "Settings", "explorer" => "File Explorer", "chrome" => "Google Chrome",
        "code" => "Visual Studio Code", _ => processName
    };
}
public sealed record DialogContext(string? WindowTitle, string Application, string DialogType, string? Message,
    IReadOnlyList<UiElement> Buttons, string? DefaultButton, UiDialogRisk RiskClassification);
public sealed record UiSnapshot(Guid SnapshotVersion, DateTimeOffset Timestamp, WindowContext Window,
    WindowContext? ForegroundWindow, UiElement? FocusedElement, IReadOnlyList<DialogContext> Dialogs,
    IReadOnlyList<UiElement> Elements, IReadOnlyList<UiElement> InteractiveElements, bool Truncated,
    string Status = "Success", string? Limitation = null);
public sealed record UiInspectionOptions(int MaximumDepth = 5, int MaximumElements = 160,
    bool VisibleOnly = true, bool InteractiveOnly = false, bool EnabledOnly = false)
{
    public UiInspectionOptions Bounded() => this with { MaximumDepth = Math.Clamp(MaximumDepth, 0, 8), MaximumElements = Math.Clamp(MaximumElements, 1, 200) };
}
public sealed record UiElementQuery(string? Name = null, string? ControlType = null, string? AutomationId = null, string? ClassName = null, string? Value = null);
public sealed record UiSearchResult(string Status, IReadOnlyList<UiElement> Candidates, bool SearchIncomplete);
public sealed class UiObservationException(string code, Exception? inner = null) : Exception(code, inner) { public string Code { get; } = code; }

public interface IWindowsUiAutomationService
{
    event EventHandler? Changed;
    bool IsInspecting { get; }
    UiSnapshot? LastSnapshot { get; }
    Task<WindowContext?> GetForegroundWindow(CancellationToken ct = default);
    Task<IReadOnlyList<WindowContext>> ListOpenWindows(CancellationToken ct = default);
    Task<WindowContext?> GetWindowInfo(long handle, CancellationToken ct = default);
    Task<UiSnapshot> GetWindowTree(long? handle, UiInspectionOptions? options = null, CancellationToken ct = default);
    Task<UiElement?> GetFocusedElement(long? handle, CancellationToken ct = default);
    Task<IReadOnlyList<UiElement>> GetUiElements(long? handle, UiInspectionOptions options, CancellationToken ct = default);
    Task<UiSearchResult> FindElement(long? handle, UiElementQuery query, CancellationToken ct = default);
    Task<UiElement> GetElementProperties(Guid snapshot, string reference, CancellationToken ct = default);
    Task<IReadOnlyList<DialogContext>> GetDialogs(long? handle, CancellationToken ct = default);
    Task<IReadOnlyList<UiElement>> GetInteractiveElements(long? handle, CancellationToken ct = default);
    void CancelInspection();
}
public interface IUiSnapshotService
{
    Task<UiSnapshot> CaptureAsync(long? handle = null, UiInspectionOptions? options = null, CancellationToken ct = default);
    void Invalidate();
}

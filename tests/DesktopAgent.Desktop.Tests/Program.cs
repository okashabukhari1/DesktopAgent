using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Automation.Services;
using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Services;
using DesktopAgent.Desktop.Services;
using DesktopAgent.Desktop.ViewModels;

var cases = new (string Name, Action Run)[]
{
    ("startup diagnostics omit exception payloads and inner secrets", () =>
    {
        using var output = new StringWriter();
        StartupFailure.Report(new InvalidOperationException("private-project-path", new Exception("credential-value")), output);
        var text = output.ToString();
        Check(text.Contains(nameof(InvalidOperationException)), "Failure category missing");
        Check(!text.Contains("private-project-path") && !text.Contains("credential-value") && text.Length < 300, "Exception payload leaked");
    }),
    ("navigation selects existing Settings surface", () =>
    {
        var vm = NewShell();
        Check(vm.Navigate(DesktopPage.Settings).Status == OperationStatus.Supported, "Settings navigation rejected");
        Check(vm.SelectedPage == DesktopPage.Settings, "Selected page did not change");
    }),
    ("invalid navigation preserves current surface", () =>
    {
        var vm = NewShell(); vm.Navigate(DesktopPage.Assistant);
        Check(vm.Navigate((DesktopPage)999).Status == OperationStatus.Failed, "Invalid route accepted");
        Check(vm.SelectedPage == DesktopPage.Assistant, "Invalid route changed current page");
    }),
    ("disconnected submission preserves draft and reports no execution", () =>
    {
        var vm = NewShell(); vm.Draft = "Open my project";
        Check(vm.Submit().Status == OperationStatus.Unsupported, "Disconnected submission claimed support");
        Check(vm.Draft == "Open my project", "Unsent draft was discarded");
    }),
    ("theme edits are session-only and reject invalid values", () =>
    {
        var vm = NewShell(); Check(vm.SetTheme(AppTheme.Dark).Status == OperationStatus.Supported, "Dark theme rejected");
        Check(vm.Theme == AppTheme.Dark, "Theme not applied");
        Check(vm.SetTheme((AppTheme)999).Status == OperationStatus.Failed && vm.Theme == AppTheme.Dark, "Invalid theme changed state");
        Check(NewShell().Theme == AppTheme.System, "Theme unexpectedly persisted");
    }),
    ("credential unavailability cannot become a plaintext store", () =>
    {
        var store = new UnavailableCredentialStore();
        Check(!store.IsAvailable && !store.Exists("test-provider"), "Unavailable store reports a credential");
        MustReject(() => store.Save("test-provider", "disposable-test-value"));
        MustReject(() => store.Read("test-provider"));
        MustReject(() => store.Delete("test-provider"));
    }),
    ("shell observes the actual existing Core character state", () =>
    {
        var character = new CharacterService(); var vm = new ShellViewModel(character, new UnavailableCredentialStore());
        character.SetState(CharacterState.Thinking); Check(vm.CharacterState == CharacterState.Thinking, "Core state not integrated");
        character.SetDeveloperState(true, false); Check(vm.CharacterState == CharacterState.Attention, "Core attention precedence not retained");
    }),
    ("existing Core parser preserves literal folder target", () =>
    {
        var command = new ComputerCommandParser().Parse("open folder /tmp/agent fixture");
        Check(command.Type == ComputerCommandType.OpenFolder && command.Target == "/tmp/agent fixture", "Existing command parsing changed");
    }),
    ("existing Core layout retains odd-width right remainder", () =>
    {
        var result = WindowLayoutCalculator.Calculate(WindowLayout.Right, new WindowRectangle(10, 20, 1001, 800), default);
        Check(result == new WindowRectangle(510, 20, 501, 800), "Existing geometry changed");
    })
};
var failed = 0;
foreach (var item in cases)
{
    try { item.Run(); Console.WriteLine("PASS " + item.Name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + item.Name + ": " + e.GetType().Name + " " + e.Message); }
}
Console.WriteLine($"{cases.Length - failed}/{cases.Length} passed");
return failed == 0 ? 0 : 1;

static ShellViewModel NewShell() => new(new CharacterService(), new UnavailableCredentialStore());
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void MustReject(Action action)
{
    try { action(); } catch (PlatformNotSupportedException) { return; }
    throw new InvalidOperationException("Credential operation did not fail closed");
}

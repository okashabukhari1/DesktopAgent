# Avalonia Desktop foundation architecture

Date: 2026-11-21, Asia/Karachi. Implements the user's Phase 2 **initial foundation** scope after native Wayland qualification. This does not implement the broader Phase 3 platform integrations or the complete screen migration described as future work in Phase 1.

## Actual build graph

```text
DesktopAgent.Linux.slnx
  DesktopAgent.Core                 existing, unchanged net10.0
  DesktopAgent.Desktop              new net10.0 executable
    -> DesktopAgent.Core
  DesktopAgent.Desktop.Tests        new dependency-free console harness
    -> DesktopAgent.Desktop -> Core
```

The original DesktopAgent.sln, WPF App, Infrastructure, DevHook, Python worker and tests are unchanged. No new Application/Platform.Abstractions/Linux assemblies were necessary for this foundation. There is exactly one existing Core implementation. The Windows Infrastructure assembly is not in the Linux graph; its AI/provider/conversation/router/policy/workspace implementations have neither been copied nor replaced. A safe extraction/adapter effort remains before connecting them. The approved Phase 2 instruction explicitly allows a disconnected chat when real pipeline integration exceeds this phase.

## Dependencies

All five direct Avalonia references are pinned to **12.1.6**: Avalonia, Avalonia.Wayland, Avalonia.Skia, Avalonia.HarfBuzz and Avalonia.Themes.Fluent. `src/DesktopAgent.Desktop/packages.lock.json` records the resolved graph and content hashes. Twenty-one packages resolve, including Avalonia.FreeDesktop/Remote.Protocol 12.1.6, BuildServices 11.3.2, NWayland 0.11.0, MicroCom.Runtime 0.11.6, Tmds.DBus.Protocol 0.94.1, HarfBuzzSharp 8.3.1.3 and SkiaSharp 3.119.4 plus their native assets. Some upstream native-asset dependencies include Windows/macOS/WebAssembly assets; their presence is not a Windows runtime/backend selection.

The existing repository uses `Microsoft.AspNetCore.App` for Microsoft.Extensions hosting/DI/logging. This foundation retains that framework reference rather than adding a new framework stack. **Both .NET 10 and ASP.NET Core 10 shared runtimes are required** by this framework-dependent executable. Both are installed on the audited machine. It is not a standalone/self-contained Linux distribution package.

Exact package availability was checked through NuGet; current tagged package/source documentation was inspected for Avalonia 12 APIs. Startup required a separate `UseHarfBuzz()` call in addition to `UseSkia()`. The prototype's first REA run caught this configuration omission before screen work; the correction was verified at runtime.

## Ownership and lifetime

| File | Responsibility |
| --- | --- |
| Program.cs | Linux-only entry, owns Generic Host and five-second bounded host stop, selects native backend explicitly, catches startup failure at a sanitized diagnostic boundary. |
| DesktopHost.cs | Empty host configuration, console logging, singleton existing Core CharacterService, unavailable credential adapter and ShellViewModel. No default application settings/user secrets/repositories/worker registrations. |
| App.cs | Fluent theme, desktop lifetime, main/qualification window selection, session theme updates, UI exception handling, host ApplicationStopping → UI shutdown, event/registration cleanup. |
| MainWindow.cs | Code-defined Avalonia view, inherited WPF navigation/identity, page rendering/bindings and lifecycle diagnostics. No filesystem/provider/policy/native action execution. |
| ViewModels/ShellViewModel.cs | Presentation state: selected existing page, unsent draft, validated session theme, existing Core character state, typed disconnected submission result. No substitute conversation/planning engine. |
| Services/UnavailableCredentialStore.cs | Implements the existing Core ICredentialStore contract: unavailable/Exists=false, read/write/delete fail closed. Never falls back to a file or reads actual secrets. |
| Services/StartupFailure.cs | Bounded exception category only; never prints message, inner exception payload or stack trace. Regression tested. |
| Views/CompanionMark.cs | Static Avalonia translation of the existing smiling purple WPF orb. No new artwork, desktop overlay or global window behavior. |
| QualificationWindow.cs / QualificationLog.cs | Retained explicit `--qualify` prototype and metadata-only stdout diagnostics. Disposable text is not logged; focused keyboard-event counts/text length are qualification-only. |

This is MVVM presentation with state/validation separated from code-defined views; it does not add a second MVVM framework or copy the WPF business services. View event handlers perform presentation actions only. Navigation and theme changes raise property notifications; draft binding is two-way. Page reconstruction retains the same ViewModel/draft. The main view and application detach their subscriptions on closure. The actual existing Core CharacterService is registered and tested, not a duplicate or fake.

SIGTERM/host cancellation posts desktop shutdown on the UI dispatcher. This was added after an REA idle-timeout capture exposed a missing bridge between host and UI lifetimes. A final explicit SIGTERM run exited zero with shell-closed/desktop-exit/process-ended. No background service or child worker is started.

## Platform and permission boundaries

Initialization is exactly `UseSkia().UseHarfBuzz().UseWayland()`. There is no `UsePlatformDetect`, `UseX11`, `UseWaylandWithFallback`, Wine, Windows UI hosting or alternate UI framework. Runtime evidence establishes `Avalonia.Wayland.WindowImpl` and native xdg-shell protocol traffic. An unavailable Wayland display fails with exit 1 even when XWayland's DISPLAY is supplied. See [qualification](../migration/NATIVE-WAYLAND-QUALIFICATION.md).

`OperationStatus` distinguishes Supported, Unsupported, PermissionRequired, TemporarilyUnavailable and Failed. Currently navigation/session appearance can succeed, invalid values fail, and chat submission returns Unsupported without discarding the draft. This is an actual unavailable result, not a success-returning business mock. Credential availability is independently false. No user policy is loaded or default policy granted to substitute for missing platform authority. No action path reaches AI tools, shell launch, files, input, accessibility, workspace execution or developer IPC, so the original Guardian/approval engine remains unchanged and unbypassed. A live approval dialog is not claimed implemented.

Applications/Projects/Workspaces/Permissions/Activity/Developer remain reachable navigation entries with explicit unavailable explanations. Home/Assistant/Settings are the functional initial surfaces. There are no fake saved projects, transcripts, successful tasks or connected indicators. Assistant accepts only an unsent in-memory draft; Send is disabled, and the ViewModel rejects direct Submit as well. Existing transcript persistence was not implemented in the original product, so none is imported here. Settings offers session-only appearance; no keys, provider configuration or old settings are read/saved.

## Data and future extension

The host registers no application repository and makes no application-data file writes. Output is console diagnostics; this task's build/test/evidence files use isolated /tmp artifacts or new repository documentation. The original 208 source/test/build inputs are fingerprint-verified unchanged. This is not an OS-wide filesystem trace: REA expressly reports unobserved filesystem/network effects, and framework font/theme/native-library access still occurs. The bounded claim is that this host does not load/import/mutate the application's existing user data or credentials.

Future Linux credential/platform adapters can replace unavailable registrations and expose capabilities without changing the provider/planner/router. Existing Windows implementation files/build graph remain available for future Windows support; no Windows feature implementation or packaging was added. Native Wayland positioning/topmost, tray, clipboard/IME/accessibility, integrations and packaging require their own later acceptance checks. Basic qualification does not certify those features.

## Build and run

From the repository root, with .NET SDK 10 and both shared runtimes installed:

```bash
dotnet build DesktopAgent.Linux.slnx --artifacts-path /tmp/desktop-agent-build \
  -m:1 -p:RestoreLockedMode=true
dotnet /tmp/desktop-agent-build/bin/DesktopAgent.Desktop/debug/DesktopAgent.Desktop.dll
dotnet /tmp/desktop-agent-build/bin/DesktopAgent.Desktop/debug/DesktopAgent.Desktop.dll --qualify
dotnet run --project tests/DesktopAgent.Desktop.Tests/DesktopAgent.Desktop.Tests.csproj \
  --artifacts-path /tmp/desktop-agent-build -p:BuildInParallel=false
```

Run from the actual GNOME Wayland user session. A working `WAYLAND_DISPLAY` and `XDG_RUNTIME_DIR` are required; D-Bus supplies desktop integration. Do not replace a missing compositor with DISPLAY. `--auto-close-seconds N` (1–180) supports bounded qualification/smoke runs; ordinary startup has no auto-close timer. No system packages, startup entry or hooks are installed by these commands. Build intermediate changes are isolated by `--artifacts-path`, avoiding copied Windows restore assets.

For the exact already verified binaries in this task, substitute `/tmp/desktop-agent-phase2/artifacts` for `/tmp/desktop-agent-build`. Package acquisition used `NUGET_PACKAGES=/tmp/desktop-agent-phase2/packages` and an isolated CLI home; those paths are temporary caches, not source requirements. NuGet/network or restrictive sandbox limitations may require running the same documented build in the user's normal environment. No package-audit/security certification is inferred from the restore.

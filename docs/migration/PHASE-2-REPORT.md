# Phase 2 — Avalonia Desktop foundation

Date: 2026-11-21, Asia/Karachi. **Status: COMPLETE for the requested initial foundation and native Wayland qualification.** AI chat/system integrations remain deliberately disconnected; this is not a complete UI or functionality migration. Phase 3 has not begun.

## Implemented

- Independent Avalonia 12.1.6 net10.0 Desktop project and Linux solution, alongside the unchanged WPF graph.
- Native Wayland-only startup, explicit Skia/HarfBuzz setup, Fluent styling, owned Generic Host/DI/logging/lifetime, sanitized startup/UI failures and graceful close/SIGTERM handling.
- Existing Core reference and real CharacterService integration; no provider/conversation/planner/router/policy/workspace rewrite or copy.
- Initial Home, Assistant and Settings surfaces, existing navigation destinations, purple smiling companion identity, normal resizable desktop layout, Light/Dark/System session appearance and retained in-memory unsent draft.
- Explicit unavailable states for unconnected features; disabled Send and a second ViewModel rejection boundary. Existing conversation history is not loaded/imported, and no fake transcript/reply is created.
- Existing ICredentialStore contract implemented as unavailable and fail-closed. No secure-store integration or plaintext fallback.
- Dependency-free portable behavioral tests and persisted REA runtime evidence.

There is no artificial background data load: this foundation starts from empty session state. Empty/disconnected/unavailable states are visible; invalid navigation/theme inputs return typed failure without changing valid state, and startup failures terminate with sanitized diagnostics. Data-backed loading/error/recovery screens belong with the actual connected services and are not presented as implemented.

## Native Wayland result

**PASS.** Before any screen migration, REA observed the prototype's actual `Avalonia.Wayland.WindowImpl` 12.1.6.0, xdg-shell surface creation/configuration and rendered-buffer attach/commit with DISPLAY disabled. The user interacted with it and confirmed rendering, movement, resizing, mouse and typing worked. A final missing-Wayland negative test failed even with DISPLAY=:0, establishing no silent XWayland fallback. See [qualification and evidence IDs](NATIVE-WAYLAND-QUALIFICATION.md).

The first prototype failed because explicit HarfBuzz shaping was omitted; this was corrected before qualification. The first shell capture later exposed host-signal shutdown missing its UI bridge; final SIGTERM/restart verification exited cleanly. Neither failed run is disguised as a pass.

The user also reported all shell navigation/theme/draft/layout checks worked. Runtime logs independently corroborate native opening and page navigation. Detailed visual/theme/draft retention observations remain user-reported, with that distinction retained in the test report.

## Exact source/project changes

All are **new files**; no original production/test source, old project, solution or shared build definition changed.

| File(s) | Change |
| --- | --- |
| [DesktopAgent.Linux.slnx](../../DesktopAgent.Linux.slnx) | Separate Core/Desktop/portable-test build entry. |
| [DesktopAgent.Desktop.csproj](../../src/DesktopAgent.Desktop/DesktopAgent.Desktop.csproj), [packages.lock.json](../../src/DesktopAgent.Desktop/packages.lock.json) | net10.0 executable, existing Core reference, five exact Avalonia 12.1.6 packages and existing Microsoft.Extensions shared-framework convention. |
| [Program.cs](../../src/DesktopAgent.Desktop/Program.cs), [DesktopHost.cs](../../src/DesktopAgent.Desktop/DesktopHost.cs), [App.cs](../../src/DesktopAgent.Desktop/App.cs) | Entry, native backend, DI/theme/application lifecycle and error boundaries. |
| [MainWindow.cs](../../src/DesktopAgent.Desktop/MainWindow.cs), [ShellViewModel.cs](../../src/DesktopAgent.Desktop/ViewModels/ShellViewModel.cs), [CompanionMark.cs](../../src/DesktopAgent.Desktop/Views/CompanionMark.cs) | Initial MVVM shell and existing visual identity translation. Views are code-defined Avalonia controls; no duplicate UI framework. |
| [UnavailableCredentialStore.cs](../../src/DesktopAgent.Desktop/Services/UnavailableCredentialStore.cs), [StartupFailure.cs](../../src/DesktopAgent.Desktop/Services/StartupFailure.cs) | Explicit unavailable credential boundary and bounded diagnostic reporting. |
| [QualificationWindow.cs](../../src/DesktopAgent.Desktop/QualificationWindow.cs), [QualificationLog.cs](../../src/DesktopAgent.Desktop/QualificationLog.cs) | Explicit retained prototype (`--qualify`) and metadata-only event output. |
| [DesktopAgent.Desktop.Tests.csproj](../../tests/DesktopAgent.Desktop.Tests/DesktopAgent.Desktop.Tests.csproj), [Program.cs](../../tests/DesktopAgent.Desktop.Tests/Program.cs) | Nine portable behavioral/Core integration cases. |

Documentation: this report; [baseline](PHASE-2-BASELINE.md); [qualification](NATIVE-WAYLAND-QUALIFICATION.md); [architecture/build/run instructions](../architecture/AVALONIA-DESKTOP-ARCHITECTURE.md); [test report](PHASE-2-TEST-REPORT.md); [implementation plan/ledger](../superpowers/plans/2026-11-21-phase-2-foundation.md); new diagnostic logs, source manifest and REA Evidence under `docs/diagnostics/phase-2/`.

## Build/run and results

Prerequisites: .NET 10 SDK for build; **.NET 10 and ASP.NET Core 10 shared runtimes** for this framework-dependent host; actual GNOME Wayland desktop and required native graphics/font libraries. Exact package/transitive versions and instructions are in the architecture. No system packages were installed and no self-contained/dual-platform package was created.

```bash
dotnet build DesktopAgent.Linux.slnx --artifacts-path /tmp/desktop-agent-build \
  -m:1 -p:RestoreLockedMode=true
dotnet /tmp/desktop-agent-build/bin/DesktopAgent.Desktop/debug/DesktopAgent.Desktop.dll
```

Use `--qualify` for the prototype, or `--auto-close-seconds 5` for a bounded lifecycle smoke run. Normal startup stays open until closed. Build output is isolated to avoid copied Windows NuGet assets. See the architecture for test commands and task-specific already-built paths.

| Verification | Result |
| --- | --- |
| Linux solution final build | **PASS**, zero warnings/errors |
| New portable suite | **PASS**, 9/9 |
| Native Wayland gate / interaction | **PASS**, runtime and user evidence distinguished |
| Final signal shutdown / no-fallback negative test | **PASS** |
| Original solution | **FAIL**, unchanged WindowsDesktop targeting/stale NuGet assets |
| Original C# harness | **BLOCKED**, before execution |
| Original Python suite | **FAIL**, 19 pass/two missing-pywinauto errors |
| Live AI, clipboard/IME/accessibility and platform integrations | **NOT RUN** |

## Preservation and security

All **208 baseline inputs remain byte-identical**, including WPF, Core, Infrastructure, original worker and tests. No app repositories, old configuration, conversations, credentials, workspace data or developer hooks were loaded/imported/modified by the new host. The isolated credential adapter rejects operations. No external action or tool backend is registered; unsupported operations cannot gain authority through a fake successful response. Original permission/approval checks remain unchanged. No global input/capture implementation, permission-setting change, background agent or restriction bypass was introduced.

The workspace still has no usable Git metadata; no branch/commit, worktree, merge, push or PR was created. Changes are additive and reviewable through the listed files and baseline fingerprints. REA Evidence was saved and the investigation reset; its filesystem/network sampling limitations remain explicit.

## Limits and next step

This host does not yet connect the existing Windows-packaged AI/conversation pipeline, persist themes/settings/drafts, import history, request permissions, discover/launch apps, open project files, run workspaces or supervise developers. Other existing navigation destinations explain their unavailable state. Floating companion/tray/topmost/transparency, clipboard/IME/AT-SPI and multi-monitor parity remain unqualified. No unsupported feature is represented as working.

Recommended next step, **only after explicit approval**, is Phase 3: safe Linux credential/storage/platform boundaries and extraction of the existing portable services, retaining their actual policy/cancellation/data safeguards. Then connect the real provider/conversation pipeline and validate a real response before enabling chat. Do not start Computer Use or silently switch backend as part of that work.

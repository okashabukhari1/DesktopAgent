# Phase 2 test report

Date: 2026-11-21, Asia/Karachi. Results apply to the new initial foundation. **PASS does not imply the original Windows features have been migrated.** See [baseline](PHASE-2-BASELINE.md) and [runtime qualification](NATIVE-WAYLAND-QUALIFICATION.md).

## Build and behavioral checks

| Check | Status | Evidence and scope |
| --- | --- | --- |
| Exact Avalonia 12.1.6 package acquisition | PASS | NuGet availability verified; 21 final resolved packages, Desktop packages.lock.json. Initial restore was slow; the earlier owned restore was terminated before completing the diagnostic retry. No system dependency installation. |
| First sandbox prototype build | FAIL | CLR aborted with Internal CLR error 0x80131506, before useful compilation diagnostics. [Log](../diagnostics/phase-2/prototype-build.log). Root sandbox/runtime mechanism not established; host build was tested separately. |
| Initial host compile iterations | FAIL, corrected | Avalonia 12 API differences (PlaceholderText, FontWeight, runtime backend inspection) were corrected; no legacy source edits. |
| Initial runtime startup | FAIL, corrected | Missing text shaping; added exact-version Avalonia.HarfBuzz and explicit UseHarfBuzz. [REA startup log](../diagnostics/phase-2/first-startup.log). |
| Independent prototype build after shaping correction | PASS | [Log](../diagnostics/phase-2/prototype-build-shaping.log), exit 0, zero warnings/errors. |
| Linux solution final build | PASS | [Log](../diagnostics/phase-2/linux-solution-build-final.log), exit 0, zero warnings/errors; Core + Desktop + Desktop.Tests only. |
| First behavioral RED run | FAIL, expected | [Log](../diagnostics/phase-2/desktop-tests-red.log): five unimplemented behavior cases failed; three existing Core integration cases passed. No placeholder remained in final code. |
| First implemented behavioral run | PASS | [Log](../diagnostics/phase-2/desktop-tests-green.log): 8/8. A preceding intermediate build exposed missing view/event delegate wiring, corrected before this pass. |
| Diagnostics regression RED run | FAIL, expected | [Log](../diagnostics/phase-2/diagnostics-red.log): startup diagnostic boundary unimplemented, 8/9. |
| Final behavioral suite | PASS | [Log](../diagnostics/phase-2/desktop-tests-final.log): **9/9**. Real shell state, unavailable credential adapter and original Core code, no mocked provider success. |
| Original solution baseline | FAIL, unchanged | [Log](../diagnostics/phase-2/baseline-build.log): NETSDK1100 and stale Windows NuGet fallback path, five errors. Not caused by the new Linux solution. |
| Original C# harness | BLOCKED | [Log](../diagnostics/phase-2/baseline-csharp-tests.log): NETSDK1100 before case execution. Zero original cases executed. |
| Original Python suite | FAIL, unchanged | [Log](../diagnostics/phase-2/baseline-python-tests.log): 21 cases, 19 pass/two errors importing pywinauto. No worker code/dependencies changed. |
| Original input preservation | PASS | All 208 baseline source/test/build input hashes match after implementation. Old WPF/Infrastructure/Core/worker/solution/build definitions remain unchanged. |
| Required reports and relative links | PASS | Four required reports plus baseline/plan/diagnostic evidence verified present. |

Nine final behavioral cases: valid navigation, invalid navigation retaining current surface, disconnected submission preserving draft, session-only validated themes, credential read/write/delete fail-closed behavior, real Core character-state/attention precedence, existing literal-folder command parsing, existing odd-width layout geometry, and omission of exception message/inner payload from bounded startup diagnostics. New feature cases were run failing before implementation; existing Core cases were regression/integration evidence.

The test harness follows the existing dependency-free executable convention. `dotnet test` is not its runner. It does not instantiate a fake working AI, workspace engine or native desktop controller. It is not a comprehensive Core/Windows regression suite and does not certify the preserved policy/router implementation.

## Actual desktop checks

| Check | Status | Evidence |
| --- | --- | --- |
| Native backend identity and Wayland connection | PASS | REA observed Avalonia.Wayland.WindowImpl 12.1.6.0 and xdg-shell/buffer protocol with DISPLAY disabled. |
| Prototype window creation/render submission/focus | PASS | Native protocol, window-opened/activated, buffer attach/commit. User confirmed visible rendering. |
| Prototype resize, mouse and keyboard/text | PASS | Resize dimensions, focused input events/lengths and button confirmations recorded; user confirmed all checks. |
| GNOME compositor movement | PASS, user-observed | User explicitly confirmed movement; no global-coordinate API or synthetic-input test used. |
| Prototype normal close/restart | PASS | Separate owned launches, window-closed/desktop-exit/process-ended and exit 0. |
| Initial shell navigation/theme/draft/layout | PASS within stated observation scope | User reported all shell checks worked. REA independently recorded native shell opening and navigation; theme/draft/resize checks are user-reported, not instrumented assertions. |
| Initial shell capture shutdown | FAIL, corrected | REA default idle timeout caused SIGTERM then SIGKILL; missing host→UI shutdown bridge identified. The initial capture does not prove normal closure. |
| Final shell SIGTERM shutdown/restart | PASS | Final rebuild, new native shell, explicit SIGTERM at 4s, shell-closed and exit 0 without forced termination. |
| Missing Wayland display / no XWayland fallback | PASS, negative test | Invalid WAYLAND_DISPLAY with DISPLAY=:0 exits 1 with sanitized AvaloniaWaylandException, no shell-opened. Expected failure demonstrates native-only behavior. |
| Live AI/provider response | NOT RUN | Pipeline/credential storage remain disconnected. No cloud/keyring request, fake response or working-chat claim. |
| Approval dialog / policy mutation | NOT RUN | Original engine untouched, no policy store loaded and no external actions registered. No Linux approval UI claim. |
| Clipboard/IME/Urdu/accessibility | NOT RUN | No consent/settings changes or runtime acceptance checks. Focused ordinary text input only was qualified. |
| Multi-monitor/fractional scale, transparency/topmost/tray | NOT RUN | Outside narrow foundation acceptance; no parity claim. |
| Capture/input automation, application discovery/launch, notifications, developer hooks | NOT RUN | Not implemented or started in this phase. |
| Original WPF startup | NOT RUN | No launch attempted; current Linux runtime lacks WindowsDesktop support. |

All REA IDs, logs, raw vs terminal-replay truncation, process-cleanup results and residual unknowns are detailed in [Native Wayland Qualification](NATIVE-WAYLAND-QUALIFICATION.md). Complete saved records are in [rea-final-evidence.json](../diagnostics/phase-2/rea-final-evidence.json); REA investigation state was saved and reset, with no persistent target left open. No screenshot or desktop video is claimed.

## Reproduction

The final tested build used a scratch CLI/package cache and output tree, avoiding the old Windows intermediates:

```bash
export DOTNET_CLI_HOME=/tmp/desktop-agent-phase2/dotnet
export NUGET_PACKAGES=/tmp/desktop-agent-phase2/packages
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
dotnet build DesktopAgent.Linux.slnx \
  --artifacts-path /tmp/desktop-agent-phase2/artifacts \
  -m:1 -p:RestoreLockedMode=true -p:NuGetAudit=false
dotnet run --project tests/DesktopAgent.Desktop.Tests/DesktopAgent.Desktop.Tests.csproj \
  --artifacts-path /tmp/desktop-agent-phase2/artifacts \
  -p:BuildInParallel=false -p:RestoreLockedMode=true -p:NuGetAudit=false
```

Final build also passed `--no-restore`. Disabling NuGetAudit for the isolated acquisition/test run is not a dependency vulnerability assessment. The committed lockfile records actual package resolution; optional audit endpoints are not a prerequisite to the native qualification result. Normal user build commands without the task-specific scratch settings are in [the architecture](../architecture/AVALONIA-DESKTOP-ARCHITECTURE.md).

REA positive runs supplied the actual Wayland runtime socket/bus endpoint and empty DISPLAY. The negative test supplied DISPLAY=:0 with a deliberately nonexistent Wayland display. The final signal test sent SIGTERM only to the owned process. REA traces do not observe every filesystem/network effect; data preservation is supported by source/DI inspection and unchanged baseline fingerprints, not an invented OS-wide trace.

Known limitations remain: Linux system services, credential persistence, provider/conversation integration, real approval UI, saved history, workspace/project operations, background supervision and Computer Use are unavailable. Build success and nine unit checks must not be extrapolated to them.

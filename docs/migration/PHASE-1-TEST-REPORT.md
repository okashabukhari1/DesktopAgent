# Phase 1 — build, tests and environment report

Date: 2026-11-21, Asia/Karachi. Results describe this supplied workspace and actual Ubuntu execution, not earlier Windows reports. Labels: **PASS**, **FAIL**, **BLOCKED**, **NOT RUN**. No application/runtime source was changed to make checks pass; no dependencies were installed.

## Current results

| Check | Status | Actual evidence / limitation |
| --- | --- | --- |
| Existing solution build, serial diagnostic run | **FAIL** | `dotnet build DesktopAgent.sln --no-restore -m:1 -v:normal`, exit 1: five reported errors, zero warnings. NETSDK1100 in Infrastructure/App/Tests; MSB4018 ResolvePackageAssets in Core/DevHook because copied assets reference a missing Windows fallback package folder. |
| Initial parallel/default build probe | **FAIL** | `dotnet build DesktopAgent.sln --no-restore -v:minimal`, exit 1 but summary misleadingly showed zero errors; rerun serially exposed the exact failures above. Not accepted as a success. |
| Existing Core build using copied restore assets | **FAIL** | `dotnet build src/DesktopAgent.Core/DesktopAgent.Core.csproj --no-restore -m:1 -v:minimal`, exit 1, MSB4018 missing `C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages`. Not a demonstrated C# compilation error. |
| Core with fresh isolated intermediates/restore | **PASS** | Exact command below, exit 0, zero warnings/errors. Built existing net10.0 source without retargeting or editing it. Establishes portable Core compilation only. |
| Existing C# executable test harness | **BLOCKED** | `dotnet run --project tests/DesktopAgent.Tests/DesktopAgent.Tests.csproj --no-restore -p:BuildInParallel=false -v:minimal`, exit 1, NETSDK1100 before case execution. **Zero cases executed**; no pass rate inferred. |
| Python protocol/backend-mock suite | **FAIL** | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s src/DesktopAgent.PythonWorker/tests -v`, exit 1, 21 tests, 19 passed, two errors caused by missing pywinauto imports. |
| Python health command | **FAIL** | `PYTHONDONTWRITEBYTECODE=1 python3 src/DesktopAgent.PythonWorker/worker.py --health`, process exit 0 but JSON status Failed, error MissingPackage:pywinauto, verified=false, input_dispatched=false. Semantic failure overrides process exit. |
| Environment/package/interpreter inspection | **PASS** | /etc/os-release, uname, session env, dotnet --info, Python version/pip list, dpkg-query, socket metadata inspected. Exact observed versions below. Some wildcard dpkg names have no installed version; these are not counted as installed packages. |
| Sandbox D-Bus/systemd checks | **BLOCKED** | busctl/systemctl --user returned Operation not permitted inside sandbox. This is a sandbox limitation, not evidence that desktop services are absent. |
| Approved read-only user bus/systemd checks | **PASS** | Session bus/service inventory, portal properties, accessibility status/address and GI binding import succeeded outside sandbox. No permission session opened, settings changed or tree content inspected. |
| PipeWire/WirePlumber/portal/GNOME portal active-state check | **PASS** | Four service checks returned active. No frames captured. |
| AT-SPI binding and bus metadata | **PASS** | `gi.require_version('Atspi','2.0')` import succeeds; bus address and registry process present. Both toolkit accessibility and IsEnabled are false. This does not certify usable trees. |
| Real accessibility-tree/control-action test | **NOT RUN** | No selected fixture app/tree was inspected; enabling accessibility was not authorized as an audit mutation. |
| Capture/RemoteDesktop/input sessions | **NOT RUN** | Only advertised interfaces read. No consent dialog, capture stream, EIS connection, keyboard/mouse injection or window manipulation. |
| Existing WPF desktop smoke/startup | **NOT RUN** | No launch attempted. The installed Linux runtime has no WindowsDesktop runtime and WPF targets are unchanged; this is a platform incompatibility finding, not an observed launch-command failure. |
| Avalonia native Wayland startup/feature matrix | **NOT RUN** | No Avalonia host/package exists or was installed. Research is not runtime acceptance. |
| Live AI/provider/credential test | **NOT RUN** | No secrets read, provider requests sent, or billing incurred. Historical Windows provider tests do not certify this environment. |
| Data/import/backup/restore | **NOT RUN** | Read-only metadata checks of three candidate directories only; all absent. No actual old profile established, no configuration files loaded or migrated. |
| Source preservation fingerprint check | **PASS** | 208 manifest-listed inputs verified unchanged after document creation. Excludes generated bin/obj, scratch CLI state and documentation; no claim of pristine Git state. |
| Documentation deliverable/link check | **PASS** | Four required Markdown files and referenced relative diagnostic/document links verified present; no implementation phase started. |

## Commands and reproducibility

Build/test environment used an isolated CLI home and disabled telemetry/first-time prompts:

```bash
export DOTNET_CLI_HOME=/tmp/desktop-agent-phase1/dotnet
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build DesktopAgent.sln --no-restore -m:1 -v:normal
dotnet run --project tests/DesktopAgent.Tests/DesktopAgent.Tests.csproj \
  --no-restore -p:BuildInParallel=false -v:minimal
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
  -s src/DesktopAgent.PythonWorker/tests -v
PYTHONDONTWRITEBYTECODE=1 python3 src/DesktopAgent.PythonWorker/worker.py --health
```

Fresh Core build command (the original Core project remained net10.0):

```bash
DOTNET_GENERATE_ASPNET_CERTIFICATE=false dotnet build \
  src/DesktopAgent.Core/DesktopAgent.Core.csproj -m:1 --ignore-failed-sources \
  -p:BaseIntermediateOutputPath=/tmp/desktop-agent-phase1/core-obj/ \
  -p:MSBuildProjectExtensionsPath=/tmp/desktop-agent-phase1/core-obj/ \
  -p:OutputPath=/tmp/desktop-agent-phase1/core-bin/ \
  -p:DefaultItemExcludes='obj/**'
```

The isolated fresh Core restore completed in 143 ms; compilation completed with zero warnings/errors. This bypassed stale Windows-generated assets without deleting them. `--ignore-failed-sources` is not proof NuGet network access works; no new application dependency was required by Core. The first CLI invocation emitted its first-use HTTPS development-certificate message; subsequent clean Core invocation disabled certificate generation. No app settings or user data were intentionally loaded/written. Failed build probes may refresh generated intermediate/cache files; those are not product source changes.

Raw logs: [solution build](../diagnostics/phase-1/solution-build-serial.log), [original Core assets failure](../diagnostics/phase-1/core-build.log), [fresh Core build](../diagnostics/phase-1/core-clean-build.log), [C# harness](../diagnostics/phase-1/csharp-tests.log), [Python tests](../diagnostics/phase-1/python-tests.log), [source manifest](../diagnostics/phase-1/source-manifest.json).

### Exact Python failures

1. `ChromeVerificationTests.test_chrome_typing_requires_recent_connected_find_workflow`: windows.py:type_text imports `pywinauto.keyboard.send_keys`; ModuleNotFoundError before expected workflow assertion.
2. `ChromeVerificationTests.test_find_typing_pins_field_without_per_character_discovery`: unittest.mock.patch resolves `pywinauto.keyboard.send_keys`; ModuleNotFoundError.

The other 19 cases passed on Linux: strict envelope/duplicate/unknown-field validation, unicode/literal keys, app allowlist, integer identity/timeout/text bounds, pre-cancel, health argument schema, unsupported action, editor identity changes, pinned process/tab checks, shortcut/scroll bounds, owned browser popup/spoof exclusion, backend reuse, and directional scroll evidence. These tests include fake Windows objects; a pass does not imply a real Windows or Linux backend works. Installing pywinauto on Ubuntu is not the proposed fix; isolate provider mocks from dependency imports and create real Linux backend tests in the appropriate phase.

## Environment observed

| Item | Confirmed value |
| --- | --- |
| OS / architecture | Ubuntu 26.04.1 LTS (Resolute), x86_64 |
| Kernel | 7.0.0-43-generic |
| Desktop / session | GNOME Shell 50.1; Mutter 50.1 package; wayland; ubuntu:GNOME; DESKTOP_SESSION=ubuntu |
| Display variables | WAYLAND_DISPLAY=wayland-0, DISPLAY=:0, XDG_RUNTIME_DIR=/run/user/1000 |
| .NET | SDK 10.0.115, MSBuild 18.0.37, Microsoft.NETCore.App/Microsoft.AspNetCore.App 10.0.15; no WindowsDesktop runtime/workloads/global.json |
| Python | 3.14.4, pip 25.1.1 |
| Automation-relevant Python | PyGObject 3.56.2, dbus-python 1.4.0, dasbus 1.7, Pillow 12.1.1, psutil 7.1.0, pyxdg 0.28; Atspi GI import available |
| Missing tested imports | pyatspi, pywinauto, pyautogui, win32gui; these do not all need installation |
| AT-SPI2 | 2.60.4; bus launcher/registry and GI binding present; toolkit-accessibility=false, IsEnabled=false |
| PipeWire / policy manager | PipeWire 1.6.2; WirePlumber 0.5.13; active |
| Portals | xdg-desktop-portal 1.21.1, GNOME 50.0, GTK 1.15.3; core and GNOME active |
| Portal interface properties | RemoteDesktop v2, AvailableDeviceTypes u7 (keyboard/pointer/touch); ScreenCast v5, AvailableSourceTypes u7 (monitor/window/virtual advertised) |
| Other relevant libraries | libei/libeis 1.5.0, libsecret 0.21.7; Secret Service bus name present |
| Compatibility/tray | XWayland 24.1.10 package and X0 socket; StatusNotifierWatcher bus name present. Neither application compatibility nor tray display tested. |
| Socket metadata | session bus, wayland-0, pipewire-0 and X0 sockets exist. Socket existence alone is not authorization or protocol success. |

Read-only commands included `gnome-shell --version`, `dpkg-query -W`, `python3 -m pip list`, `busctl --user list`, service `is-active`, `gsettings get org.gnome.desktop.interface toolkit-accessibility`, portal version/AvailableDeviceTypes/AvailableSourceTypes properties and `org.a11y.Bus.GetAddress`/IsEnabled. Broad bus inventory included application names, not window titles or contents; no personal bus inventory is committed to diagnostics. No secret value was queried.

## Existing tests and missing coverage

Static registration count is 267: Program 55, Part2 29, Part3 39, Part4 42, Part5 19, Part6 28, Part62 9, Part7 33, Part71 13 (including seven generated UI-contract cases). This count is source inventory, not test execution output. `dotnet test` alone would not run this dependency-free console suite. Part72 health/smoke and live UI/provider diagnostics are special command modes. `--integration`, `--catalog-probe`, `--ui-live`, `--windows-diagnostics`, `--python-worker-smoke` were not run; some launch/edit disposable documents or interact with the actual desktop/provider.

Coverage already includes fake-provider protocol/schema/error handling, typed tools, policy, workspace sequencing/cancel/partial results, persistence, project discovery/search, file privacy/links/redaction/retrieval budgets, developer events/expiry/installer, UI targeting and Computer Use safety/task limits. Extract these rather than replacing the test suite. No App reference means no current automated shell/navigation/view-lifecycle/native STOP-hook coverage.

Missing Linux tests: portable runnable C# harness, XDG overrides and import/non-overwrite, Secret Service lock/unavailability, Linux path case/symlink/hardlink/TOCTOU security, desktop entries/opener/editor argv, Unix peer credentials, strict worker response/version/error recovery, real portal grants/revocation/focus/takeover, AT-SPI application coverage, and native Avalonia startup/IME/clipboard/companion limitations. Source risks also need regression cases for approval mutation/Allow→Ask races, future-version workspace preservation, concurrent writers and small monitor geometry.

Historical Windows case counts/results (84/123/184/207/267), prior smoke results and Application Control blockers refer to particular earlier revisions. None supersedes today's Linux build/test failures or certifies live capabilities. Build success is only compilation evidence. No Phase 2 implementation or production-readiness claim follows from this report.

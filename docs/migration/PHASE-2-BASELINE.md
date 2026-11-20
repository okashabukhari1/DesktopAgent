# Phase 2 baseline

Date: 2026-11-21, Asia/Karachi. Read the four Phase 1 documents before starting. The user has approved the specified Phase 2 foundation/qualification work, with a mandatory stop before screen migration if native Wayland cannot be qualified.

Git status, branch and commit queries all return **not a git repository** in the supplied mount; branch/commit/uncommitted diff are unavailable. No Git metadata was initialized or reset. Additive project files plus [baseline fingerprints](../diagnostics/phase-2/baseline-source-manifest.json) isolate this work; all 208 original inputs must remain unchanged unless a later explicit ownership map records an authorized extraction.

Existing Core is src/DesktopAgent.Core/DesktopAgent.Core.csproj, net10.0 with no project/package references and no WindowsDesktop framework dependency. Directory.Build.props supplies nullable/implicit usings/latest language/warnings-as-errors. Existing copied Core obj assets carry Windows fallback paths; fresh isolated restore metadata is required for Linux compilation. This is not a source dependency on WPF.

Existing WPF startup App.xaml/App.xaml.cs uses explicit lifetime and Generic Host DI, initializes settings/policy/activity/catalog/workspaces/projects and credential migration, then native foreground/UIA events, tray/companion/STOP hooks and optional developer adapters. Linux prototype must not invoke it. Main screens are Home/Assistant/Applications/Projects/Workspaces/Permissions/Activity/Developer, plus Settings/onboarding and compact companion chat. Purple/orb identity and actual navigation/service semantics are retained as the later shell reference.

Existing singleton provider/router/conversation, policy, workspace/project services and Core contracts must be reused, not recreated. Native Windows adapters are registered in WPF composition; old Infrastructure targets net10.0-windows with WPF/Forms, so merely referencing it from a Linux host is not valid isolation. Native credential/input/file-security/developer IPC dependencies remain unchanged at baseline.

## Fresh baseline checks

| Check | Result | Evidence |
| --- | --- | --- |
| Original solution build | FAIL | [Log](../diagnostics/phase-2/baseline-build.log): five errors; NETSDK1100 and MSB4018 Windows NuGet fallback path. |
| Existing C# harness | BLOCKED | [Log](../diagnostics/phase-2/baseline-csharp-tests.log): NETSDK1100 before tests execute. |
| Existing Python suite | FAIL | [Log](../diagnostics/phase-2/baseline-python-tests.log): 21 tests, 19 pass, two errors importing pywinauto. |
| Session environment | PASS | wayland, ubuntu:GNOME, WAYLAND_DISPLAY=wayland-0, DISPLAY=:0; SDK 10.0.115. Environment alone is not app-backend proof. |

No original UI, user configuration, credentials, workspaces, project source, hooks or worker runtime was started or modified during baseline checks. Build-generated obj/cache files are excluded from the source manifest. Existing failures must not be described as newly introduced migration regressions.

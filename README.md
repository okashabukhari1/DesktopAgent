# Desktop Agent — Windows AI Assistant Foundation

A Windows desktop companion with application discovery, safe computer control, saved workspaces, a text-first AI assistant, explicit permissions, onboarding, and local activity history. It is built with C#, WPF, MVVM, dependency injection, Windows APIs, local JSON settings, and local logging. The desktop app does not activate microphone, wake-word, speech recognition, or speech output services. It has no screen capture, arbitrary keyboard/mouse control, unrestricted terminal, or remote access.

## Requirements

- Windows 10/11
- .NET 10 SDK (the solution can be retargeted to .NET 8 by changing each `TargetFramework`)

## Build and run

```powershell
dotnet build DesktopAgent.sln
dotnet run --project src/DesktopAgent.App/DesktopAgent.App.csproj
```

Create the recommended Windows single-file build:

```powershell
dotnet publish src/DesktopAgent.App/DesktopAgent.App.csproj -p:PublishProfile=WindowsSingleFile -m:1
```

Run `src\DesktopAgent.App\bin\Release\net10.0-windows\win-x64\publish\DesktopAgent.App.exe`.

Run the dependency-free non-UI test suite:

```powershell
dotnet run --project tests/DesktopAgent.Tests/DesktopAgent.Tests.csproj
```

## Architecture

- `DesktopAgent.Core` — character, automation, workspace, application catalog, capability/policy, and AI provider contracts.
- `DesktopAgent.Infrastructure` — Windows application discovery, JSON persistence, Windows Credential Manager integration, the OpenAI-compatible streaming provider, structured tool routing, safe workspace orchestration, policy enforcement, and window control.
- `DesktopAgent.App` — WPF Home, Assistant, Applications, Workspaces, Permissions, Activity, AI settings, onboarding, character, and tray surfaces.
- `DesktopAgent.Tests` — dependency-free automated and optional Windows integration test harness.

User data is stored under `%LOCALAPPDATA%\DesktopAgent`:

- `settings.json`
- `workspaces.json`
- `applications.json`
- `projects.json` (versioned, local project metadata; no source code)
- `permissions.json`
- `activity.json` (latest 200 actions)
- `logs\desktop-agent-YYYY-MM-DD.log`

## Projects (Part 3)

Use Settings → Projects to add development locations, enable subfolders, set maximum depth (0–8), and Scan Now. Nothing scans entire drives or automatically adds locations. Projects → Add Existing Project detects an explicitly chosen folder, including shallow backend/frontend manifests; Remove from catalog never deletes files. Locate Project repairs moved/missing folders while retaining identity and preferences.

Projects have technologies, signature-file names, editor/terminal preferences and optional workspace associations. Supported editor adapters use installed VS Code, Visual Studio or Android Studio entries from the application catalog. Terminal preferences are stored, not executed. No project variables, terminal AI, source reading, Git writes or modification monitoring are enabled.

The Assistant adds eight typed tools: list_projects, search_projects, get_project, get_recent_projects, get_project_technologies, open_project, associate_project_workspace, get_current_project. Opening a project prepares its associated workspace first, then sends the folder/solution to its editor. Child workspace permissions are enforced, and changes during approval invalidate the plan. Current project is session state with Clear; recents record only agent-initiated project openings.

See [Part 3 implementation report](PART3_IMPLEMENTATION_REPORT.md) for verification and remaining interactive Windows scenarios.

## Character assets

The placeholder is a vector WPF control in `src/DesktopAgent.App/Controls/CharacterControl.xaml`, so it stays crisp at any size. State-specific asset folders already exist under `Assets/Character`. A later asset provider can load PNG sequences, sprite sheets, GIF/WebP, or Lottie into the reusable control without changing window, tray, settings, or controller services.

## Direct commands

Click the character and enter one of these case-insensitive commands:

```text
help
apps
windows
open notepad
open calculator
open vscode
open chrome
open terminal
focus vscode
close notepad
minimize chrome
maximize vscode
restore chrome
move vscode left
move chrome right
move vscode top-left
move chrome bottom-right
move vscode 100 100
resize vscode 1200 800
move vscode monitor 2
open folder D:\Projects
open file D:\Projects\notes.txt
open url https://github.com
workspaces
launch workspace Morning Focus
stop workspace Morning Focus
search app visual
refresh apps
```

Application aliases are centralized in `ApplicationResolver`. Unknown names are rejected unless they are an existing absolute `.exe` path. File opening blocks executable and script extensions, and URLs are limited to HTTP/HTTPS.

## Workspaces

Open **Workspaces** from the desktop app sidebar, character menu, tray menu, or compact panel. A workspace can contain ordered applications, folders, files, HTTP/HTTPS pages, window layouts, target monitors, launch delays, safe before/after actions, and a final application to focus. Launches are cancellable and report Ready, Partial, Cancelled, or Failed with per-step details.

Workspace definitions are local, portable JSON data. No machine-specific sample workspace is created automatically. Deleting a workspace removes only its definition—it never deletes files, folders, browser data, or installed applications.

## Application catalog

The catalog combines Start Menu shortcuts, registered App Paths, conservative installed-desktop registry entries, Windows Shell AppsFolder entries for packaged applications, and visible running applications. Entries are normalized and deterministically deduplicated, cached locally, and refreshed only when stale or explicitly requested. The Applications screen provides immediate deterministic search, real executable icons where available, favorites, running state, Open/Focus, and existing Part 2 window actions.

Normal `open <application>` commands resolve only catalog applications or established safe aliases. Ambiguous matches return selectable choices. An absolute executable may still be chosen explicitly from the Workspace editor's file picker, but a typed `open` command does not treat arbitrary paths as catalog applications.

## Text-first AI assistant

The full Assistant and character panel share one in-memory conversation. Exact direct commands work without a cloud request; natural-language requests use a configured OpenAI-compatible model with a bounded tool loop. Registered tools call typed catalog/controller/workspace services directly. They validate arguments and enforce local permissions before execution. Action cards and completion summaries reflect actual tool results. Ambiguous app choices preserve the original action.

Configure Provider, Model, API key, Base URL and Request Timeout in Settings → AI or first-run onboarding. Keys remain in Windows Credential Manager; the UI sees only credential existence. Test Connection checks the configured model with a real inert function-call request. Fetch Models does not establish a working model connection.

Assistant requests, including direct commands in chat, follow Allow/Ask/Deny rules. Explicit Applications and Workspaces page buttons remain user actions. Workspace launches also check embedded action permissions. Stop cancels provider/tool/workspace work where supported and does not undo completed actions.

Cloud AI can be disabled without disabling direct commands. Context is bounded to recent complete turns and structured references/results. Conversation text stays in memory; bounded local activity records action/policy/provider metadata and actual usage where supplied. Part 4 adds registered-project read-only file intelligence; no voice, local model, screen capture, arbitrary clicking, unrestricted terminal, remote access or autonomous agent was added.

See [Part 2 implementation report](PART2_IMPLEMENTATION_REPORT.md) for the tool list, implementation details and verification limits.

## Manual verification checklist

Use the character and tray menu to check dragging, panel open/close, command results, hide/restore, Always on Top, settings persistence, restart position, Windows startup registration, animations, and clean exit. Configure a provider credential before testing natural-language requests. Without a provider, exact commands continue to work. A Windows UI smoke test is available with `--smoke-test`. A read-only catalog probe is:

```powershell
dotnet run --project tests/DesktopAgent.Tests/DesktopAgent.Tests.csproj -c Release -- --catalog-probe
```

The optional `--integration` test mode opens and closes real Windows applications and should be run only when the desktop is available for that test.

## Current limitations

- The character artwork is a replaceable vector placeholder.
- Natural-language requests require an API credential and compatible provider endpoint. The default endpoint and model are editable.
- Provider tests use fake responses. The final Part 5 suite passed 184/184 cases (165 earlier and 19 developer tests) under the normal Windows user. The sandbox denied the secure pipe test; it and the complete suite passed with approved normal-user execution. Desktop launch remains blocked by Windows Application Control (0x800711C7). See the Part 5 report for live verification limits.
- The tool catalog has bounded read-only project file tools, but no unrestricted filesystem access, shell, file writes, browser automation, or screen inspection.
- The Part 4 WPF smoke test passed at its prior revision. The Part 5 smoke test is blocked by Windows Application Control; interactive acceptance remains unverified.
- Activity history is local and bounded to the latest 200 computer actions; it is not a complete transcript or policy audit log.
- Windows can reject foreground activation in a non-interactive/background test process. Commands issued from the active character panel normally qualify for foreground activation; otherwise the result explains that the taskbar must be used.
- Applications running at higher integrity may reject operations; the controller does not elevate or bypass Windows security.
- Voice/SAPI code and settings were removed from the active source. Older `settings.json` files may still contain a `Voice` field; it is ignored and removed on the next settings save.
- WPF interaction, tray behavior, mixed-DPI monitor movement, and login startup still require target-machine verification.
- Appearance changes are applied when Settings closes.
- The published `.exe` is unsigned. Windows Smart App Control can block it until it has a trusted publisher signature; building another unsigned executable does not remove that requirement.

## Part 4: file and code intelligence

Select a registered project in **Projects → Files**. List the root, inspect a bounded tree, search filenames/code/symbols, inspect a line range, or open a result in its configured editor. Settings → Files controls retrieval limits and additional excluded directory names. List/Search/Read/Open permissions apply to these UI actions and assistant tools; secret/private/generated paths cannot be permitted by overriding those capabilities.

Assistant examples: “Find Program.cs”, “Where is authentication configured?”, “Explain AuthService”, and “Open the file you just mentioned”. A current project is the default; searching an explicitly named second project does not switch it. Actual tool results create compact file/line cards with Open buttons. Source is untrusted data: after retrieval the tool loop cannot launch unrelated apps, mutate workspaces, or execute source instructions.

Source reads are locally redacted and bounded; the API never receives an entire project. Defaults: 12 search results, four retrieved files and 24,000 retrieved characters per human turn, 120 lines/12,000 source characters per read, a 2 MiB file-prefix ceiling, and a 16 MiB local search byte budget. UTF-8 and BOM-marked UTF-16 are supported. Lexical symbols/routes are not a semantic index; incomplete searches are not proof of absence. All appsettings files are conservatively excluded. No file watcher or automatic source scan is added.

See [Part 4 implementation report](PART4_IMPLEMENTATION_REPORT.md) for the earlier file-intelligence implementation.

## Part 5: developer supervision (verification pending)

Developer supervision is experimental and disabled by default. Changing its enabled state requires an app restart. Disabled integrations do not start background adapters or appear as available assistant tools. Claude hook Install/Repair/Remove shows a preview and requires confirmation before changing user settings. No hooks are installed automatically. The relay must remain beside the app with its DLL, dependency and runtime files. Sessions are associated with registered project roots without changing the current project.

The Developer page shows observed sessions, recent activity, bounded file/command summaries and eligible Allow Once/Deny requests. The assistant has eight read-only developer-query tools. It cannot approve developer actions or kill coding agents through STOP. Build/test requests are not success: absent structured exit evidence remains Unknown. Full diffs, sensitive/unknown commands and command arguments stay in the developer tool's native review flow.

OpenCode has an optional read-only **v1 plugin event** adapter for start/busy/idle/error only. Installation is previewed and opt-in; it never attributes session-less file events, handles permissions, or captures source/output. OpenCode v2 APIs are unsupported. Cursor and VS Code are detection-only; no extensions or private protocols are used. No live integration settings were modified during development.

See [Part 5 implementation report](PART5_IMPLEMENTATION_REPORT.md). Its live acceptance remains incomplete; Part 6 preserves and isolates it rather than repairing it.

## Part 6: read-only Windows UI awareness (manual acceptance pending)

Open **Settings → UI Inspector**, click **Refresh**, and approve read-only inspection locally if prompted. Actual foreground and remembered last external window are displayed separately; agent windows do not replace the external target. Explicit window selection is also available. Requests pin the selected window before approval. Search the captured tree by name, type, automation ID or class. This is an on-demand snapshot, not screen recording.

The assistant has seven read-only UI tools for windows, focus, controls, search and dialogs. Ask “What window is active?”, “Is there a Save button?” or “What buttons are in this window?”. Inspection permission defaults to Ask; previously saved Deny remains Deny. Approving inspection may send a small, redacted subset to the configured AI provider. The local inspector does not send its results to cloud AI. Screen input, screenshots, OCR, scrolling, typing and clicking remain unavailable.

Password values are never requested. Ordinary text/value content is also omitted; sensitive window classes/titles are blocked conservatively. Names are best-effort redacted, not a guarantee that every application labels private information correctly. Trees are limited to 200 visited controls/eight levels, browser-like windows to 100/four; cloud context includes at most 12 relevant controls. A single MTA worker gives callers a three-second timeout and cannot multiply hung providers. References expire after two seconds and are revalidated against process/window/runtime identities.

Release build/publish, managed desktop smoke verification and the final published EXE smoke test succeeded. Final automated regression: **207/207**. The six-app live acceptance checklist is still pending because Windows Computer Use did not expose the running Desktop Agent window. See [Part 6 implementation report](PART6_IMPLEMENTATION_REPORT.md) for the evidence and limitations. The EXE remains unsigned; a successful test launch does not establish publisher trust or guarantee every Smart App Control launch context.

## Part 6.2: raw Windows diagnostics

**Settings → Advanced → Windows Diagnostics** provides Run Window Test, Test Calculator, Test Notepad and Test Windows Settings. Each Win32 API, UIA root creation, root properties, immediate children and bounded depth-3 tree is reported independently, with a correlation ID, timings and exact exception/HRESULT details. Diagnostic metadata stays local; normal logs omit titles, executable paths, exception messages and control names. Protected windows/values remain blocked. UIA runs on one persistent, windowless MTA worker with a three-second deadline and a busy guard.

Calculator on this machine uses an ApplicationFrameHost top-level HWND with a CalculatorApp child process. Window detection identifies the hosted app by child PID, not title matching. Raw host PID/process remain visible. Application questions use Win32 alone; provider failures or a root-only provider retain the app identity and explicitly say detailed controls are unavailable.

The last published EXE predates Part 6.1. Actual Part 6.2 service-level results and the Application Control launch blocker are documented in [the diagnostic report](PART6_2_DIAGNOSTIC_REPORT.md). Do not treat old successful build/smoke statements as verification of this new build. In-app focus acceptance remains pending if Windows refuses to launch the new app.

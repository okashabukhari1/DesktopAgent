# Part 6 Implementation Report

Status: implemented and built; **not yet complete under the requested definition of done** because the live Windows acceptance checklist remains unverified. No next phase was started.

## Architecture

Core contains provider-independent window, element, dialog, snapshot, query and inspection interfaces. Infrastructure combines the existing deterministic window/application services with a read-only Windows UI Automation provider. The existing controller retains launch, close, focus, move, resize and workspace responsibilities. `IUiSnapshotService` provides on-demand capture; `UiContextBuilder` supplies small task-relevant AI responses. Settings has a local-only inspector. There is no screenshot parser, vision provider or input-action layer.

## Windows APIs

Actual additional APIs: `GetForegroundWindow`, `IsWindow`, `IsWindowEnabled`, `GetWindowThreadProcessId`, `OpenInputDesktop`, `GetUserObjectInformationW`, `CloseDesktop`, `SetWinEventHook` and `UnhookWinEvent`. Existing `IExternalWindowService` supplies `EnumWindows`, title, bounds, visibility, minimized/maximized and process metadata. Process creation time pins HWND/PID identities against reuse. Input desktops are only queried, never switched or modified. The guard requires an accessible desktop named Default; otherwise inspection is unavailable. [Microsoft OpenInputDesktop documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-openinputdesktop).

## UI Automation

Uses the supported Windows Desktop .NET assemblies `UIAutomationClient`, `UIAutomationTypes` and `WindowsBase`, with `System.Windows.Automation.AutomationElement`, `TreeWalker.ControlViewWalker`, property reads and runtime IDs. All provider traversal runs on a separate MTA worker, not WPF's UI thread. Pattern availability and read-only/selection/modal state are inspected; action methods are never called. Unsupported properties remain null. [Microsoft .NET 10 TreeWalker API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.treewalker?view=windowsdesktop-10.0), [Microsoft threading guidance](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-threading-issues).

## Window Detection

Foreground is measured from actual Windows state. Open windows reuse the existing service, limited to 64 local results and 20 AI inventory results. Application ID is returned only when the cached catalog has a unique process-name mapping; otherwise it is unknown. Switching to this assistant changes foreground. The system explicitly distinguishes a selected window from the currently active window; it does not silently relabel a previously focused application as active.

## UI Elements

`UiElement` includes name, automation ID, class, type, bounds, nullable visible/enabled/focused/selected state, pattern capabilities, parent and children. Common UIA control types retain their actual programmatic names; modal windows can be represented as Dialog. `CanSetValue` requires a supported non-read-only value pattern, without reading its value. Snapshot-local references use `ui:1` etc.; no AI element coordinates are included. Unknown properties are never synthesized.

## UI Tree

Defaults: depth five, 160 visited elements. Hard ceilings: depth eight, 200 visited elements. Browser-like Chrome/Edge/Firefox/Code windows are additionally bounded to depth four and 100 elements. Filtered-out nodes still consume traversal budget. Visible-only, enabled-only and interactive-only modes are supported. Limit-boundary results are marked incomplete; absence from a bounded tree is not proof that a control does not exist. Traversal is control-view navigation inside one selected window, not a desktop-wide subtree search.

## Element Search

Core search supports name, type, automation ID, class and non-sensitive model value. Exact name ranks before partial matches, then visibility, enabled state, foreground, type/ID matches and focus proximity. Multiple matching candidates remain ambiguous even when presentation ranking differs. Results include incompleteness. Actual editable/text values are intentionally not read, so live value-based search is limited by that privacy restriction. Inspector search works locally against the captured bounded snapshot.

## Dialog Detection

Detects exposed modal WindowPattern elements and classic `#32770` dialog classes in the selected subtree. Exposed modal application dialogs are classified Normal; class-only evidence remains Unknown. Buttons are actual detected controls. Message text and default-button identity remain null because this implementation does not retrieve private text or infer defaults. Separate top-level/owned dialogs require explicit window selection. Windows security-sensitive/password-manager/authentication windows are conservatively blocked; unavailable Secure Desktop is never bypassed. This is not universal dialog detection.

## Sensitive Fields

`IsPassword` is read before any name. Protected controls return `Password field` and `[PROTECTED]`, omit identifying strings and children, and are excluded from search. The model itself protects password name/value serialization. No `ValuePattern.Value`, TextPattern text ranges, selected text, document text or screenshots are requested. Ordinary Edit/Text/Document names are also omitted because some providers put editable/private content in Name. Unknown password metadata causes conservative name omission. [Microsoft IsPassword API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.ispasswordproperty?view=windowsdesktop-10.0).

Known security/password-manager/messaging processes and authentication/banking/private-message title markers are blocked. Remaining metadata uses the existing secret redactor and length limits. These heuristics can over-block and cannot identify every personal datum or mislabeled provider. Local inspection requires the same permission policy but sends nothing to cloud AI. Requested AI tools send a small subset only after approval. Activity/logs contain generic operation metadata, not window/control names or values. No UI snapshot is saved to disk. UI tool payloads are removed from cross-turn provider history/reference context; displayed conversation answers remain in memory.

## AI Tools

Seven typed, strict-schema, observational tools:

- `get_foreground_window`
- `list_open_windows`
- `inspect_window`
- `get_focused_element`
- `get_interactive_elements`
- `find_ui_element`
- `get_active_dialogs`

Inspection permission is Available with an Ask default; existing saved Deny remains effective. Allow Once is supported; an Always Allow response does not automatically change this sensitive capability. Explicit window targets are pinned across approval and revalidated. UI results are untrusted data and cannot authorize subsequent app/workspace mutations or editor opening. Fresh, relevant successful observations are required before answering UI questions; prior-turn data is not accepted as new evidence. Clicking/typing/scrolling requests are explicitly refused. No click, type, key, mouse, scroll, drag or permission-approval tools were added.

## UI Inspector

Settings → UI Inspector has explicit Refresh, window selection, Inspect selected window, Stop, filters and snapshot search. It displays actual foreground metadata, selected-window metadata, captured focus, counts and a bounded indented hierarchy. Historical data is labeled as a snapshot, with expired references marked historical. The assistant/compact companion displays `Inspecting window` during traversal and a timestamped last-inspection summary afterward, not a continuously current or recorded screen. Existing Working state is used for assistant observation tools.

## Performance

One MTA provider lane; no unbounded queue. Cancellation and a three-second deadline bound callers. If native UIA hangs, the background lane remains busy until that call returns: subsequent inspections fail ProviderBusy, rather than spawning extra hung workers. Restart may be needed. Cooperative cancellation checks precede each property/navigation operation. STOP cancels assistant/provider/tool work and current inspection; it never kills an external app.

Snapshot references expire after two seconds. Re-reading properties verifies HWND/PID/process creation time and recaptures the selected tree, resolving actual runtime ID rather than coordinates or old ordinal IDs. Events invalidate retained references for foreground/focus/window create/destroy changes, without reading UI content or polling. No automatic snapshot reuse or deep scan is performed. Cloud context contains at most 12 relevant controls; dialogs are bounded to two with six buttons each.

## Part 5 Isolation

Preserved the existing subsystem without redesigning or repairing it. Existing default remains OFF. Disabled developer integrations no longer start supervisor/adapters at normal startup, advertise assistant tools, accept provider-requested developer tools, refresh diagnostic adapter status, or show compact session cards. Settings marks them experimental/unreliable and explains restart requirements. Explicit legacy opt-in settings are respected, with existing startup exception isolation. No live Claude/OpenCode hooks/configuration were installed or modified.

## Tests

Final suite: **207/207 passed**, including 23 new UI tests and all prior regressions. Coverage includes unknown-property serialization, protected password serialization, three filters, exact ranking, ambiguity, field search, partial search, depth/count limits, cancellation, two-second expiration, cloud context limits, all seven strict tools, stale/secure/unsupported errors, timeout/busy-lane recovery, policy denial, activity privacy, input refusal, missing-evidence refusal, retrieval injection prevention, fresh-turn evidence and STOP.

An initial restricted run passed 206/207, with the unchanged Part 5 current-user pipe test access-denied. Its first normal-user retry was also denied while validation app processes existed. After closing only the two validation processes started during this phase, the isolated pipe test passed, and the full normal-user regression suite passed. Windows Application Control also transiently blocked a test assembly in the restricted context. No file renaming, policy change, signing-policy bypass or Windows security changes were used.

## Manual Tests

The managed desktop smoke test and the **final published EXE smoke test** both passed (exit 0), exercising app/settings/assistant and existing views. An ordinary development launch exposed `Desktop Agent` through Windows process metadata, but the computer-use interface did not return that window in its target inventory. Under the [computer-use skill](C:/Users/TESLA/.codex/plugins/cache/openai-bundled/computer-use/26.930.21537/skills/computer-use/SKILL.md), a returned target is required; no guessed handle or alternate UI automation script was used. Only app inventory was read; no screenshots, typing or system/privacy-setting changes were made. Both phase-owned validation app processes were stopped afterward.

| Application | Actual evidence | End-to-end Part 6 acceptance |
| --- | --- | --- |
| Notepad | Returned in installed app inventory, not launched | Pending |
| Calculator | Returned in installed app inventory, not launched | Pending |
| Windows Settings | No usable target selected in this session | Pending |
| File Explorer | Two real windows returned in inventory | Pending |
| Chrome | Real open window returned in inventory | Pending |
| VS Code | No usable target selected in this session | Pending |

The ten interactive scenarios (foreground Notepad, Calculator buttons, Settings search, click refusal, dialog, protected password, closing mid-inspection, complex Chrome, Save search and similar-control ambiguity) have **not** been declared passed. Synthetic automated cases are not substitutes for those tests. On the user's desktop, refresh the local inspector, approve inspection yourself if asked, select each test window and inspect it. Do not change Windows security settings or use an actual password for a test fixture.

## Regression

All 165 automated Parts 1–4 cases passed, covering application discovery/commands, windows, workspaces, projects/current context, guarded file/code retrieval and AI routing. Part 5's 19 existing cases also passed in the final suite. Controllers/file/workspace implementations were preserved. The requested live Chrome/PowerLens/project/workspace command sequence was not run end-to-end; no claim is made that automated fake-service tests verify those live applications or the configured cloud provider.

## Known Limitations

Live provider behavior and the six-app acceptance checklist remain unverified. Accessibility quality depends on application providers; games/canvas/custom/remote UI may be limited. Focus outside a visited tree is attempted only through a bounded parent chain belonging to the selected window. Browsers are heavily truncated. Dialog message/default button and live value search are deliberately limited. Privacy heuristics are best effort; explicit permission is still required. A hung native call cannot be forcibly aborted safely within this process. Some UI changes without supported Windows events are detected only on reinspection. Previous conversation answers can contain historical UI labels and must not be treated as live state.

The rebuilt EXE is unsigned. Its final smoke launch succeeded in this execution context, but this does not establish publisher trust or guarantee Smart App Control will allow every launch/distribution context. No certificate was supplied and no security protection was disabled.

## Build Result

Release solution build succeeded with **zero warnings and zero errors**. Windows x64 single-file publish succeeded. Final output: `src/DesktopAgent.App/bin/Release/net10.0-windows/win-x64/publish/DesktopAgent.App.exe`. Retain the published directory, including the existing developer relay files, when distributing it. Managed desktop and final published EXE smoke verification passed; these are distinct from live UIA/provider acceptance and publisher trust.

## Files Changed

New: `Core/UiAwareness/UiModels.cs`, `UiAlgorithms.cs`; `Infrastructure/UiAwareness/WindowsUiAutomationService.cs`, `UiObservationWorker.cs`, `UiTools.cs`; `App/Views/UiInspectorView.cs`; `Tests/Part6Tests.cs`; this report.

Updated: Infrastructure and Tests project framework references; `Core/Permissions/AgentCapabilities.cs`; `Infrastructure/Ai/ComputerAgentToolRouter.cs`, `AssistantConversationService.cs`; `App/App.xaml.cs`; Settings/Assistant XAML and code-behind; `App/Views/DeveloperSettingsView.cs`; test runner registration; README; release/publish artifacts. Working Parts 1–4 and Part 5 integration code were not deleted. No subsequent phase was started.

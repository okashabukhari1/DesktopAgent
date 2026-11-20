# Part 7 Implementation Report

Date: 2026-10-07. Status: **INCOMPLETE — implementation preview, not runtime-accepted.**

The controlled-action foundation builds. The pre-repair revision passed 28/28 Computer Use tests and 246/247 whole-suite tests. The subsequent repair build has 31 Computer Use cases, but Windows Application Control prevented them from loading; those tests were NOT executed. On the latest live test, `artifacts/part7-repair/DesktopAgent.App.exe` launched normally, but Calculator and Notepad tasks failed at AI tool argument validation before controlled UI input. Part 7 is NOT runtime-accepted. No Part 8 work was started.

## Architecture

Explicit user request → bounded AI planning → observed logical window/snapshot/element reference → local target and policy checks → single action → local verification → further observation, one safe stale-reference recovery, or stop. The existing typed application/workspace services remain preferred. No generic script/input-sequence tool was added.

## Action Executor

Added `ComputerAction`, `ComputerInteractionResult`, `IComputerActionExecutor`, `IComputerInteractionBackend`, `ComputerActionExecutor`, and `ComputerUseSession`. The differently named result avoids colliding with the existing typed-command result. A single action lane enforces enabled/onboarded state, a visible assistant/indicator, registered STOP and input monitor, an active user scope, bounded actions/time/failures, policy and consequence checks. Targets are revalidated again after approval.

## UI Automation Actions

Implemented Invoke, Value, Toggle, SelectionItem, ExpandCollapse, Scroll and RangeValue pattern paths, plus element focus. Native UIA elements remain on the existing persistent MTA worker. Range-value controls are now identified in observations. These paths compile; live pattern execution has not been accepted against the required Windows fixtures.

## Pointer Input

Internal `WindowInput` provides left-click and wheel fallbacks from freshly resolved element bounds. It checks foreground, desktop, bounds, virtual-screen normalization, held modifiers and the window underneath the point. There is no AI coordinate argument. Unsupported Invoke can fall back to a click only for `click_ui_element`; it does not retry a possibly dispatched Invoke. Right-click, double-click, a general MovePointer API and a separately injectable `IPointerInputService` are unfinished.

## Keyboard Input

Uses Windows SendInput with per-character/action guards and key-up cleanup in finally blocks. Supports navigation keys, Ctrl+A/C/F/S, Alt+Tab, Shift+Tab and Calculator arithmetic keys. Ctrl+V is intentionally unavailable until clipboard provenance/sensitivity handling is implemented; the service never reads clipboard contents. There is no separately injectable `IKeyboardInputService` yet. Windows input remains constrained by UIPI; no elevation workaround exists. [Microsoft SendInput documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).

## Target Resolution

AI actions use `window:ApplicationName`, snapshot GUID and `ui:N`. Observation tools accept logical window references through their legacy-named `window_handle` argument; raw numeric handles remain accepted for local backward compatibility, not requested from AI. Local identity includes HWND/PID/process creation identity and hosted process. Runtime IDs are retained in at most eight leases for at most 60 seconds and resolved anew on the MTA worker; old coordinates are never reused. Window title, element identity/type/name/automation ID, enabled/visible/sensitivity and ancestor protections are checked. Multiple same-application windows are refused rather than guessed; a complete disambiguation workflow is still needed.

## Verification

Focus uses runtime-ID equality. Toggle, selection, expansion, scrolling and range value use their actual pattern states. Value entry compares only the explicitly selected non-password field locally. Typed text compares a bounded range immediately preceding the caret. Ctrl+A compares selection endpoints to document endpoints without reading document text; Ctrl+C checks the clipboard change counter without reading clipboard contents. Alt+Tab checks an external foreground change. Generic Invoke/click/shortcut/wheel paths compare bounded control state; a change is not proof of the user's entire goal. If verification cannot establish an effect, the result is `Unverified`, not success, and subsequent input stops. Exact Calculator result/page/file-save semantic verification is not finished.

## Recovery

Expired references detected before dispatch may trigger one fresh observation and one retry within the same authorized application. Changed/sensitive targets, ambiguous windows, denied permissions, timeouts and possibly dispatched but unverified actions stop. There is no blind retry using old coordinates. Modal workflows currently tend to hand off rather than provide complete save/dialog recovery.

The repair revision adds one schema-only retry for a single malformed read-only UI call (not mutation calls), and one bounded continuation when the planner ends without any controlled UI action. The Notepad runtime test exercised the observation retry, but its second read tool also failed validation. This is not a successful recovery.

## Multi-Step Tasks

The existing assistant loop now offers computer tools only for recognized explicit computer requests and plans at most three calls per round. Settings bound actions (1–16), consecutive failures (1–2), duration (10–180 seconds) and planning rounds (1–12; default 8). Calls also have a task-dependent total budget. Retrieved file/developer content cannot authorize computer actions. App targets are restricted to applications mentioned by the user, or the original external window if none is named. Typed text must be supplied in the user's request. These restrictions mean generated/transformed form content and some natural-language requests still need refinement.

## Policy

Added `ComputerUse.Actions` to the existing Allow/Ask/Deny system. Deny prevents observation/mutation by the action executor. Ask and consequential actions use single-action confirmation; there is no Always Allow button for these approvals. Policy is checked again after approval and at mutation guards. Save/send/delete/install and unknown confirmation controls receive stronger treatment; terminals, authentication/security controls and protected windows are denied. Policy classification is conservative and is not claimed to understand every application's consequences.

## Autonomy Modes

Observe never acts. Assist permits low-risk operations and asks for consequential ones. Trusted Actions optionally trusts selected ordinary pattern operations in configured process names, while unknown button actions still ask and high-risk actions never auto-approve. Unknown applications retain Assist behavior. Computer Use is disabled until explicit onboarding.

## Emergency Stop

Implemented a message-pump-owned global **Ctrl+Alt+Shift+F11** registration with no-repeat. F12 was not used because Microsoft reserves it for debuggers at all times. Registration failure disables action eligibility. The earlier staging build's settings panel actually reported successful STOP and input-monitor registration; an active-task shortcut cancellation test remains pending. STOP cancels planning/task tokens and prevents subsequent input; it does not kill unrelated apps, shut down Windows or delete data. A native action already dispatched cannot be undone. [Microsoft RegisterHotKey documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).

## User Interruption

Low-level hooks collect only physical-input presence/epoch, not key codes or typed text. Injected input is excluded. Physical activity pauses the task when configured; expected permission-dialog interactions are exempt while waiting for the user's decision. Foreground and element focus are checked before physical input regardless of that preference. Resume requires a new request. Real takeover during typing is not yet runtime-verified.

## Sensitive Input

Unknown/password field status fails closed. Secret-like submitted text, terminal/console ancestors, authentication, security/privacy and CAPTCHA controls are blocked. Password/PIN/OTP/credential requests are refused. No credential store, unrelated clipboard contents, document contents or typed text are added to action history. Local bounded text/value comparisons are used only for the explicitly selected known non-password field.

## UAC

The normal input desktop is checked before action. Protected/elevated targets and inaccessible process tokens are refused. UAC, Secure Desktop and Windows security protections are not automated or bypassed. No live UAC prompt was triggered for testing. Pre-repair test runs were permitted; subsequent repair test loading was blocked by Code Integrity. No security setting, trust store, application-control policy or executable renaming workaround was used.

## UI

Added Settings → Computer Use with enable/acknowledgment, mode, trusted processes, takeover pause and bounded limits. The action indicator is mandatory while acting. Companion/full assistant views show status and always-enabled STOP. The latest source also adds a non-activating topmost STOP/status card so an activated target app cannot simply obscure the inline controls; action eligibility checks indicator visibility. This newest card is compiled but not live-verified. First-launch onboarding opens the full desktop app unless Start minimized is selected. Live inspection of the earlier staging build confirmed the full desktop window and Computer Use panel. The computer-use skill was used for that inspection and kept consent/permission choices under the user's control.

## AI Tools

New typed tools: `invoke_ui_element`, `set_ui_value`, `toggle_ui_element`, `select_ui_element`, `focus_ui_element`, `scroll_ui`, `click_ui_element`, `type_text`, `press_key`, `press_shortcut`, `expand_ui_element`, `collapse_ui_element`. Strict schemas reject raw coordinates, extra arguments and malformed references. They call the typed executor directly; none can approve its own permission dialog.

## Tests

Actual automated test chronology; these results do not establish native runtime acceptance:

- Initial action tests: **20/20 passed**.
- Expanded action tests: **25/25 passed** in a later iteration.
- Earlier whole suite: **238/239 passed**. The existing developer current-user named-pipe fixture failed with Access denied; its integration was not repaired in this phase.
- Pre-repair source contains **28 Computer Use cases**, including assistant/tool routing, false-completion refusal and planner takeover cancellation. That isolated run **passed 28/28**. Several preceding runs were blocked before execution by Windows Application Control; no security settings were changed to obtain the permitted run.
- Pre-repair whole regression suite: **246/247 passed**, including all 28 Computer Use tests. The same existing developer current-user named-pipe roundtrip/disconnect fixture failed with `Access to the path is denied.` No other failures were reported.
- Repair source contains **31 Computer Use cases**, adding premature-plan completion and bounded read-only/mutation schema-retry tests. The repair solution compiled with zero warnings/errors. Both the repair-turn test attempt and the resumed live-testing attempt were **BLOCKED before execution by Windows Application Control**. No new passing test count is claimed.

## Runtime Tests

The repaired executable was launched through the computer-use skill on the resumed testing turn. Its full Assistant page opened, and its AI provider returned HTTP 200. Calculator and Notepad requests were submitted through the product, not performed by the testing assistant directly. Their requested interaction did not occur.

| Required runtime case | Actual result |
| --- | --- |
| Calculator 578 × 24 = 13,872 | FAILED: typed launch tool reported AlreadyRunning; observation tool returned InvalidArguments; direct visual inspection showed Calculator still at 0 |
| Notepad text entry/correct target | FAILED: Notepad opened, both initial observation and bounded read-only retry returned InvalidArguments; direct screenshot/UIA inspection showed an unmodified document with 0 characters |
| Settings Bluetooth page | Not run; not verified |
| Scrolling | Not run; not verified |
| Keyboard selection/copy | Not run; not verified |
| Visible STOP during a task | Not run; final fake-backed cancellation tests passed, not native acceptance |
| Emergency STOP during a task | Ctrl+Alt+Shift+F11 produced the product STOP status. The task had already reported validation failures by the resulting screenshot, so active-task cancellation is NOT established |
| Stale UI | Earlier fake-backed rejection tests passed; native test pending |
| User interruption | Earlier fake-backed scope cancellation passed; native typing takeover pending |
| Save/dialog/UAC/multi-step/natural language | Not runtime-accepted |

The user manually opted in; stored settings showed Enabled=true, OnboardingAcknowledged=true and Assist mode. No consent checkbox was automated. The latest native action tests remain failed/unaccepted. Settings navigation, scrolling, shortcuts, stale UI and physical user interruption were not exercised because the observation-to-action path failed first.

Actual logs at 22:17:19 and 22:18:30–31 show rejected `get_foreground_window` and `get_interactive_elements` arguments with expected field kinds `window_handle=String,query=Null,control_type=Null`. The log deliberately omits values; it does not distinguish invalid reference content/length from extra properties. Thus exact malformed content is not asserted. Calculator's typed focus log also named ApplicationFrameHost while Settings became the listed foreground window: application target selection needs independent investigation, not a claim of correct focus.

## Regression

The pre-repair source regression run passed **246/247**, including application/workspace/project/file-intelligence/AI, all 35 UI-awareness checks and all 28 Computer Use cases. The sole failure was the existing developer current-user named-pipe fixture (`Access to the path is denied.`). The repair tests cannot be confirmed because their loader was blocked. These automated checks do not confirm requested end-to-end workflows. Existing PowerLens/project/workspace data was not edited for runtime fixtures. No developer/Claude integration repair was performed.

## Known Limitations

Part 7 remains incomplete. AI observation argument compatibility is a reproduced runtime blocker, and the Calculator typed target/focus path also needs investigation. Required live tests, exact task-result verification, robust owned-dialog/save workflows, full pointer capabilities, Ctrl+V handling, separate input-service interfaces and better disambiguation/intent handling remain outstanding. Bounded generic UI-state changes do not prove semantic task completion. Providers can remain busy after a native timeout; no extra worker is spawned to evade that state. The executable is unsigned. Windows permitted the repaired EXE in this test, but blocked the unsigned test assemblies.

Code Integrity events **3077/3033 at 2026-10-07 22:11:43–44 local time** identified the rebuilt `DesktopAgent.Infrastructure.dll` loaded by the test executable as failing active policy `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`. The subsequent resumed test command again reported the loader block. No protection was bypassed.

Code Integrity events **3077 and 3033 at 2026-10-07 21:36:44–45 (local time)** identified an intermediate `tests/DesktopAgent.Tests/bin/Release/net10.0-windows/DesktopAgent.Core.dll` as failing the enterprise signing requirements/active policy. Policy ID: `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`. This establishes a historical OS policy rejection, not a failed test assertion or proof that every later build is blocked. The final isolated test command was permitted; the trust/reputation change was not diagnosed and no policy or signing change was made. Reliable distribution still needs a signing/trust arrangement accepted by Windows; self-signing alone is not a Smart App Control solution. [Microsoft signing guidance](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control).

## Build Result

Release solution build succeeded with zero warnings and zero errors in the final build check. Framework-dependent Windows x64 single-file preview is published to `artifacts/part7-preview/DesktopAgent.App.exe`; requires the appropriate .NET 10 Windows Desktop runtime. It is not digitally signed and is not a completed/accepted Part 7 release. `artifacts/part7/` is the older live-inspected staging build, not the newest executable. No bypass was attempted and no certificate/trust modification was made.

Final preview size: 1,574,627 bytes. SHA-256: `B803448E503A0EE78511F53C39F13993C512E7B4BA2281F2728B331ECB43D464` (artifact identity, not a safety certification).

Latest repair build: `artifacts/part7-repair/DesktopAgent.App.exe`, **1,579,235 bytes**, SHA-256 `D7825AD116A375BC55F24FE6638F45BC1CB0D2158CB031EFFB64E5518C61D80F`. Publish succeeded using the existing WindowsSingleFile profile and consistent framework-dependent settings after ordinary NuGet restore access. This separate output preserves the earlier running preview; it is not a signing/trust workaround. The repair EXE launched normally but failed the live tasks above.

## Files Changed

New files:

- `src/DesktopAgent.Core/ComputerUse/ComputerUseModels.cs`
- `src/DesktopAgent.Infrastructure/ComputerUse/ComputerUseSession.cs`
- `src/DesktopAgent.Infrastructure/ComputerUse/ComputerActionExecutor.cs`
- `src/DesktopAgent.Infrastructure/ComputerUse/ComputerActionTools.cs`
- `src/DesktopAgent.Infrastructure/ComputerUse/WindowInput.cs`
- `src/DesktopAgent.Infrastructure/UiAwareness/WindowsComputerActions.cs`
- `src/DesktopAgent.App/Services/ComputerUseSafety.cs`
- `src/DesktopAgent.App/Views/ComputerUseSettingsView.cs`
- `tests/DesktopAgent.Tests/Part7Tests.cs`
- `PART7_IMPLEMENTATION_REPORT.md`

Updated integration files: Core AppSettings, AgentCapabilities and UiModels; Infrastructure JsonSettingsService, WindowsUiAutomationService, UiTools, AssistantConversationService and ComputerAgentToolRouter; App startup/DI, AgentActionApprovalService, AssistantView XAML/code, SettingsWindow XAML/code and SettingsViewModel; test Program and the disabled-input expectation in Part6Tests. Generated preview EXE/dependencies are under `artifacts/part7-preview/`.

Stop here. Do not start Part 8. User onboarding is complete. Resolve the reproduced AI argument/target bridge failures before repeating native action acceptance; resolve test signing/trust legitimately rather than bypassing protections.

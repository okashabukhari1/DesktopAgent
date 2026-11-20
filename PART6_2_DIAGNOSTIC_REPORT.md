# Part 6.2 — Windows diagnostic evidence

Status: service diagnostics verified; full in-app acceptance NOT COMPLETE.

## Confirmed findings

1. The running DesktopAgent.App process (PID 9416) used the previously published EXE, timestamp 2026-10-04 22:27:51 local time. The Part 6.1 app DLL was built at 23:07:48. The published executable did not contain those later changes. The old generic error message is absent from current source.
2. CalculatorApp (PID 7788) has no top-level window. The actual Calculator top-level HWND is `0x200996`, owned by ApplicationFrameHost (PID 11960). Direct lookup by CalculatorApp alone failed at enumeration, before UIA ran. The repair derives the hosted application from the child-window PID, without guessing from the title. Raw host HWND/PID/process are retained.
3. UI Automation root creation works for all three requested applications in the final service tests. This is NOT a universal UIA/root-creation or wrong-apartment failure.
4. Settings currently exposes a valid CoreWindow root but zero immediate children and zero interactive elements. No exception was thrown. Its control-level observation is Limited, while its Win32 window metadata remains available. This test does not establish why the current Settings provider exposes no descendants.
5. Launching the newly built WPF application DLL was blocked by Windows Application Control. Exact exception: `System.IO.FileLoadException`, HRESULT `0x800711C7`, message: `An Application Control policy has blocked this file.` This prevents verification of the new diagnostic page and the real Calculator → companion focus sequence. No policy was disabled or bypassed.
6. The existing application's 2026-10-04 log has no matching UIA/Win32 stage diagnostics. The exact operation behind its old generic assistant message cannot be reconstructed from that log. The confirmed direct-lookup failure above must not be presented as proof of the cause of every earlier generic error.

## Implementation

- Settings → Advanced → Windows Diagnostics: Run Window Test, Test Calculator, Test Notepad and Test Windows Settings.
- Independent results for GetForegroundWindow, IsWindow, IsWindowVisible, GetWindowThreadProcessId, process name, executable, GetWindowTextLength, GetWindowText and GetWindowRect.
- Separate current foreground, last external identity/metadata and explicit/default target resolution.
- UIA HWND validation, FromHandle, root properties and immediate children each have their own result/error. A depth-3, 100-element tree runs only after those stages succeed.
- Correlation IDs, durations, exact exception type/message/HRESULT and stack methods in local developer diagnostics. Normal logs omit titles, executable paths, exception messages and control names. Password values and text values are never requested; protected windows and secure desktop remain blocked.
- One persistent, windowless MTA worker replaces per-request threads. The three-second deadline and one-lane busy guard remain. Native UIA elements never leave this worker.
- Win32 application questions do not require cloud AI or UIA. Provider failures and root-only providers retain app identity and return a deterministic controls-unavailable answer, not guessed controls or a total window-awareness failure.
- No changes to developer integrations; no clicking, typing, screenshots, OCR or other Computer Use feature was added.

Threading rationale: Microsoft's [current UI Automation threading guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading) recommends a separate, windowless MTA thread and describes apartment-lifetime risks. Runtime root diagnostics report MTA; the regression verifies that successive jobs use the same worker thread.

## Required runtime diagnostic output

These are direct tests on the actual Windows desktop, not simulated UIA results. At final capture time the actual foreground was Brave, not Desktop Agent. The direct test process observed Brave as its current/last external window. It did NOT reproduce the requested companion focus transition. Browser title text is intentionally omitted from this summary; protected/bounded native metadata is in the local JSON reports.

### Calculator

```text
WINDOW DIAGNOSTIC
Correlation: UI-20261004-184353-d9ec797a

Current Foreground
PASS
Process: brave
Title: omitted from summary

Last External
PASS (direct diagnostic process only; not companion focus acceptance)
Process: brave
Title: omitted from summary

Target Resolver
PASS — explicit target from direct enumeration, not foreground history
Target: Calculator
HWND: 0x200996
Host process: ApplicationFrameHost, PID 11960
Hosted process: CalculatorApp, PID 7788

Win32 Metadata
PASS — validation, visibility, PID, process, executable, title and bounds
Title: Calculator

UIA Root
PASS — MTA
Control Type: Window
Class Name: ApplicationFrameWindow
Name: omitted because password-status property is unavailable
Exception: none
HRESULT: not applicable

Immediate Children
PASS
Count: 4

Interactive Elements
PASS — bounded collection, not full calculator-key coverage
Count: 3
Depth: 3
Max Elements: 100
Truncated: True
Observed named controls include Open Navigation and Keep on top.
```

Raw report: [Calculator-direct.json](artifacts/windows-diagnostics/Calculator-direct.json).

### Notepad

```text
WINDOW DIAGNOSTIC
Correlation: UI-20261004-184354-7629a4f1

Current Foreground
PASS
Process: brave
Title: omitted from summary

Last External
PASS (direct diagnostic process only; not companion focus acceptance)
Process: brave
Title: omitted from summary

Target Resolver
PASS — explicit target from direct enumeration
Target: Notepad
HWND: 0x608EC
Process: Notepad, PID 22076

Win32 Metadata
PASS
Title: Untitled - Notepad

UIA Root
PASS — MTA
Control Type: Window
Class Name: Notepad
Name: omitted because password-status property is unavailable
Exception: none
HRESULT: not applicable

Immediate Children
PASS
Count: 5

Interactive Elements
PASS — bounded collection
Count: 16
Depth: 3
Max Elements: 100
Truncated: True
```

Raw report: [Notepad-direct.json](artifacts/windows-diagnostics/Notepad-direct.json).

### Windows Settings

```text
WINDOW DIAGNOSTIC
Correlation: UI-20261004-184354-69239be1

Current Foreground
PASS
Process: brave
Title: omitted from summary

Last External
PASS (direct diagnostic process only; not companion focus acceptance)
Process: brave
Title: omitted from summary

Target Resolver
PASS — explicit target from direct enumeration
Target: Settings
HWND: 0x304D2
Process: SystemSettings, PID 1316

Win32 Metadata
PASS
Title: Settings

UIA Root
PASS — MTA
Control Type: Pane
Class Name: Windows.UI.Core.CoreWindow
Name: omitted because password-status property is unavailable
Exception: none
HRESULT: not applicable

Immediate Children
PASS — enumeration completed, but exposes no children
Count: 0

Interactive Elements
LIMITED — detailed controls NOT available in this capture
Count: 0
Depth: 3
Max Elements: 100
Provider/root exception: none
```

Raw report: [Settings-direct.json](artifacts/windows-diagnostics/Settings-direct.json).

## Regression and build evidence

- Final UI-specific suite: 35/35 passed, including Calculator → Agent remembering, self exclusion, closed/reused/invalid HWND handling, fallback selection, persistent MTA execution, Win32 without UIA, local application questions and controls-unavailable fallback.
- Final full suite: 218/219 passed. The unrelated Part 5 current-user pipe fixture failed with sandbox AccessDenied; an earlier normal-user run failed with “All pipe instances are busy.” It was not repaired or disabled.
- Release solution build: zero warnings/errors. WindowsSingleFile publish succeeded.
- New executable: `artifacts/part6.2/DesktopAgent.App.exe`, 1,500,899 bytes, unsigned. SHA-256: `89D04ECD185CE8C35B488917E13AEC92E1862FA0C92556812C5F8C46F8BC3D39`.
- The old running published executable was not overwritten or terminated. The replacement was placed in a separate output directory to avoid changing that running instance.
- Newly built WPF DLL launch: blocked by Application Control, as above. The new EXE has not been launched; publishing it is not evidence that Windows will permit it.

## Remaining acceptance

The new app must be permitted by the existing Windows application-control policy before the diagnostic page can be runtime verified. Then open Calculator, focus it, open this build's companion/diagnostic page and run Window Test. Actual foreground must be the same agent process, last external must be Calculator and default resolution must select its host HWND. Until that real sequence is observed, requirement 7 and full Part 6.2 completion remain pending. No fake “fixed” claim or later Computer Use phase is made.

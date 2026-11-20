# Part 7.2 — partial implementation, NOT COMPLETE

## Latest milestone evidence (2026-10-11)

User's latest live D screenshot proves actual dispatch: Notepad contains `He`, then worker stopped with TargetChanged. Modern Notepad renamed its Untitled tab/window to entered text, violating the strict title guard. Added selected TabItem runtime identity pinned before text dispatch, checked on every character alongside HWND/PID/process creation, foreground, visibility, modal and exact editor runtime checks. Classic Notepad without tabs retains the strict title check. No retry after partial input. Python tests 14/14 PASS, including title auto-rename allowed only with same pinned tab and process; tab/process changes and unpinned title changes rejected. Updated source and packaged worker in artifacts/part72-editor-repair; app restart required to unload existing Python child. D not yet CompletedAndVerified; E not begun.

Latest live D retry still failed before input with two PythonEditorRequired results. Read-only diagnostic against explicitly selected Notepad proved the native root contains ui:3 Document / RichEditD2DPT beneath NotepadTextBox, but IsVisible is unknown; VisibleOnly omitted it. Added narrowly scoped Notepad read-only candidate augmentation from the actual bounded root, preserving issued IDs and unknown flags; inspect_window queries and find_ui_element now use this candidate set. No global visibility filter change or input authorization relaxation. Hidden, disabled, protected, unknown-class and other-application controls remain excluded. Python preparation additionally requires explicit verified is_visible=true and is_enabled=true for the exact runtime identity. Build PASS (zero warnings/errors); focused UI tests 37/37 PASS and CU tests 32/32 PASS. Broad substring UI run included unrelated temporary-file tests and failed four sandbox access checks (49/53), so it is not a full-suite PASS. Updated executable reload and real chat verification are pending. D remains FAIL/not yet reverified; E and Part 8 not begun.

Next live D attempt reached the provider successfully and returned PythonEditorRequired before dispatch, proving the planner selected a non-editor control. Added type_text-specific tool guidance and exactly one bounded re-observation opportunity for this rejection, only when ComputerInteractionResult.InputMayHaveOccurred=false. Protected-editor/application failures and potentially dispatched input do not recover; repeated identical actions remain blocked. 32/32 CU tests PASS, including fresh-reference repair bounded to two failed calls and no repair for protected/application/dispatch-uncertain cases. Reload and live verification are still pending; D is NOT PASS.

Repaired-build D retry was submitted through the real Assistant UI. It failed BEFORE actions because the actual OpenRouter completion request raised HttpRequestException: No such host is known (openrouter.ai:443), recorded at 18:51:06 local time. No type action was dispatched. The observation fix remains runtime-unverified; this network failure is not an HTTP 429 or evidence that API credit limits are exhausted.

Latest D runtime attempt: FAIL before Python dispatch with PythonUnsupportedTarget after successful provider HTTP 200, Notepad launch and read-only inspection. Source diagnosis found inspect_window's default context used only InteractiveElements, excluding non-interactive Document editors despite the planner instruction. Repaired inspect_window to use observed Elements, prioritizing Edit/Document references within the same twelve-control limit. Added actual tool-response regression test with a Document behind 24 toolbar controls; 52/52 UI-filter tests PASS. Rejections now distinguish unsupported application, protected editor and non-editor selection. All original editor identity/password/read-only checks remain. Published artifacts/part72-editor-repair/DesktopAgent.App.exe; live retry pending. No shortcut or Part 8 work begun. Confirmed zero DesktopAgent.App.exe processes before launching this repair, resolving the previously observed duplicate-process/STOP conflict.

Latest update: milestone C PASS through the live Settings policy test and existing ComputerActionExecutor, with explicit editor-runtime identity preserved across native observation and Python execution. Result a10fd2d7-d345-4bcd-80d2-742b1d9695d4 is CompletedAndVerified, Verified=true, InputMayHaveOccurred=true; independent Notepad observation showed Hello World in agent-policy-smoke-4e0156e9b6a54d81be370d7e46cc6462.txt. A and B remain PASS. D is implemented but awaiting the actual assistant-chat runtime test. E has not begun, respecting milestone order.

The chat-integrated build published successfully to artifacts/part72-chat/DesktopAgent.App.exe with packaged Python sources. Only Notepad TypeText selects Python; other actions keep the native backend. No automatic native retry follows Python dispatch. Execution Backend settings expose interpreter path, health/version/status/error and the policy test. Existing permissions, session guard, STOP and input-monitor checks are retained; live STOP/takeover are not yet proven. Python validation tests: 13/13 PASS. Provider contract tests: 13/13 PASS outside the sandbox (restricted sandbox denied test temporary-file writes). The remaining sections below describe earlier milestones and are superseded by this update where noted; this is still NOT COMPLETE.

Milestone A PASS: user supplied worker JSON `CompletedAndVerified`, `input_dispatched: true`, `verified: true`, and confirmed Hello World appeared in the disposable Notepad. Its identity was HWND 591692, PID 13404, creation 134361968782976590, title agent-smoke-ce58d8e7ba524e56b9d442a350d32d24.txt - Notepad. This proves the standalone worker, not assistant chat execution.

Milestone B PASS: user supplied C# result ID fb162c2836634a78acf8f3488f8acfef, `CompletedAndVerified`, `InputDispatched: true`, `Verified: true`, and confirmed visible Hello World. Target HWND 722904, PID 15392, creation 134361972365843107, disposable document agent-csharp-smoke-5f8ef7c3d3404745869dfa6ddc83c568.txt. `PythonWorkerClient.cs` adds configurable/discoverable Python, correlated NDJSON private child-process transport, strict action request shape, serialized calls, bounded responses, hard timeout/cancel termination, EOF shutdown and conservative dispatch uncertainty. Actual C# health/repeated requests, argument rejection, pre-cancellation, missing-Python, simulated worker crash, hung worker and wrong-ID checks PASS. The solution rebuilt with zero warnings/errors; all 11 Python validation tests and 31 existing Computer Use tests PASS. `Part72Diagnostics.cs` and Program switches provide the user-run fixed C# Hello World test. No C#→policy or AI integration has begun. No EXE was republished.

## Root cause

The preceding live native Notepad test rejected its UIA target as `ChangedOrSensitiveControl`. Diagnostics recorded `PasswordKnownFalse=False`, `IdChanged=True`, and `NameChanged=True`; they did not establish a stable, explicitly non-password editor. Earlier provider argument/schema failures were repaired in Part 7.1, but that does not prove the current complete AI-to-input path works. The user's independent Python keyboard test succeeded; the new guarded worker still requires its own desktop runtime proof.

## Python worker

Standalone Notepad-only development backend under `src/DesktopAgent.PythonWorker`, using installed Python 3.14.7 and pywinauto 0.6.9. UTF-8 NDJSON over stdin/stdout, fixed action allowlist, request IDs, strict validation, one active request, cancellation events, fixed diagnostic categories, no network listener and no arbitrary code execution. Focus/type are disabled in ordinary worker mode. The explicit user-run smoke test opens a disposable file and has a parent-process hard timeout.

Implemented initial actions: health, find_window, focus_window, get_window_info, get_focused_control, inspect_editor, type_text. Window resolution validates visible Notepad identity independently of the launching PID. Text dispatch checks foreground/editor identity between characters and distinguishes dispatch from verification. The C# private child-process client is registered for policy-authorized Notepad TypeText only.

## AI integration

IMPLEMENTED, awaiting actual chat runtime verification. The existing AI/router/policy/executor now chooses Python only for Notepad TypeText. Native observation runtime ID/class/type must match Python's explicitly writable, non-password editor; the prepared HWND/PID/creation/runtime identity is pinned through execution. A, B and live C passed; D awaits the visible Assistant window, and E has not begun. Cancellation and session guard monitoring reach the private worker; actual STOP/takeover tests remain outstanding. No fallback automatically repeats potentially dispatched input.

## Actual runtime tests

| Required acceptance test | Result | Evidence / limitation |
|---|---|---|
| Worker health check | PASS | Actual worker reports Python 3.14.7, pywinauto 0.6.9, no input dispatched |
| Window discovery | PASS | User-run A/B found uniquely named disposable visible Notepad |
| Focus | PASS | User-run A/B typed into the intended disposable Notepad |
| Fixed C# request | PASS | C# → Python Hello World verified and visually confirmed |
| AI chat request | NOT RUN | Chat-integrated build launched; Assistant must be opened by user |
| Correct target | NOT RUN | Guards exist; no real wrong-window test |
| Keyboard shortcut | NOT RUN | Not implemented in worker |
| Scrolling | NOT RUN | Not implemented in worker |
| STOP | NOT RUN | Existing session cancellation reaches worker; live input cancellation pending |
| User takeover | NOT RUN | Per-character guards exist; live test pending |
| Invalid arguments | PASS | Actual execute() unsupported-action request returns Failed with no dispatch; strict validation unit cases also pass |
| Worker crash | PASS | Fixed non-interactive fixture exits during health; C# returns failure without hanging |
| Missing Python | PASS | Missing interpreter fixture returns MissingPython; Execution Backend settings added |

## Verification

Health and all 13 protocol/validation unit tests passed. Unicode/key escaping was parsed without dispatch; this is not a Unicode desktop input test. Standalone Python, fixed C#, and live policy Hello World tests returned local text-range verification with independent visible-text confirmation. C# transport failure/timeout fixtures passed. Current C# suite: 262/263 passed outside the sandbox, including all 31 Computer Use and 13 provider contract tests. One real named-pipe roundtrip test failed because all pipe instances were busy; the live app also reported that pipe conflict. This is not a full-suite PASS. Emergency STOP, shortcut, scrolling, takeover and assistant-chat acceptance remain unverified.

## Known limitations

Unsupported or unknown editor/password/read-only state fails closed. A top-level window focus alone is not accepted as document focus. Execution Backend settings, policy binding and approved-editor identity bridge are implemented. Shortcuts, mouse/scroll and D–E remain unfinished. Transport failures terminate the client's own child; a later explicit request can create a fresh child, but a potentially dispatched request is never retried automatically. Cancellation is tested before dispatch and via hung-process timeout, not during actual typing. An older background process still locks the default build DLL; a separate output tree avoids overwriting it. The character-only startup requires user tray → Open Desktop App → Assistant before the chat test.

## Build result

Current publish succeeded using `dotnet publish src/DesktopAgent.App/DesktopAgent.App.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:BaseOutputPath=bin/Part72/ -o artifacts/part72-chat -v:minimal`. Duplicate relay publish injection was removed; the existing post-publish copy still ships its four required files. The new DesktopAgent.App.exe and python-worker files are present and startup is recorded in the local log. Test-project build succeeded with zero warnings/errors. Default solution rebuild is blocked by an old locked DLL. A successful publish is not runtime acceptance.

## Files changed

- src/DesktopAgent.PythonWorker/worker.py
- src/DesktopAgent.PythonWorker/windows.py
- src/DesktopAgent.PythonWorker/validation.py
- src/DesktopAgent.PythonWorker/requirements.txt
- src/DesktopAgent.PythonWorker/tests/test_protocol.py
- src/DesktopAgent.PythonWorker/README.md
- src/DesktopAgent.Infrastructure/ComputerUse/PythonWorkerClient.cs
- tests/DesktopAgent.Tests/Part72Diagnostics.cs
- tests/DesktopAgent.Tests/Program.cs
- tests/DesktopAgent.Tests/PythonFixtures/crash.py
- tests/DesktopAgent.Tests/PythonFixtures/hang.py
- tests/DesktopAgent.Tests/PythonFixtures/wrong_id.py
- PART72_IMPLEMENTATION_REPORT.md

Additional integration changes: src/DesktopAgent.Infrastructure/ComputerUse/PythonComputerBackend.cs, HybridComputerBackend.cs; src/DesktopAgent.Infrastructure/UiAwareness/WindowsComputerActions.cs; src/DesktopAgent.Infrastructure/Ai/AssistantConversationService.cs; src/DesktopAgent.Core/ComputerUse/ComputerUseModels.cs; src/DesktopAgent.App/App.xaml.cs, DesktopAgent.App.csproj, Views/ComputerUseSettingsView.cs. Part 8 was not started.

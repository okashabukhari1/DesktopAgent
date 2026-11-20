# Part 7.3 — implementation in progress; NOT COMPLETE

## Root causes

Source-confirmed shortcut path: all actions previously required snapshot + ui:N and routed Notepad keyboard shortcuts to WindowsComputerActions.ResolveActionElement. That resolver rejects changed window titles before sending keys. Modern Notepad auto-renames untitled documents after typing. Window keyboard actions now resolve fresh local window identity and validate the exact editor through Python instead of a cached element. Native element actions retain their validation.

Comma-separated requests were not classified as explicit computer tasks. The new regression reproduced zero router calls and “Computer actions require a separate explicit user task.” Added comma/semicolon action boundaries.

Chrome had no Python scrolling action. Its only scroll tool required a UIA element. The planner could terminate after observation or one successful action, and WindowOnly observation ended tasks early. Added explicit scroll_window and missing-step continuation checks. The exact historical Chrome provider decision has not been reproduced yet; do not treat this source diagnosis as a completed runtime test.

## Python worker

Fixed shortcut allowlist: Ctrl+A/C/F/S, Escape, Enter, Tab, Shift+Tab, arrows. Notepad shortcuts independently validate writable, non-password editor, window/process identity, foreground, selected tab and editor runtime. Ctrl+A checks UIA document selection. Ctrl+C checks clipboard sequence only; exact content is not read or asserted. Other shortcuts without verifiable evidence stop as InputDispatchedUnverified, with no repeat.

Chrome process allowlist limited to normal installed Google Chrome paths. Scrolling resolves visible browser Document with explicit non-password evidence, pins runtime, checks foreground, occlusion and hit-test ancestry, dispatches at most three wheel increments, and checks ScrollPattern percentage where available. If unsupported, reports dispatched/unverified without retry. No coordinates accepted from provider. Chrome Ctrl+F verifies a focused browser-owned Find Edit outside web Document ancestry. Chrome text is limited to that validated Find box; page forms and address bars unsupported. Chrome shortcuts other than Ctrl+F currently fail safely.

## Router, policy and planner

Window-action schemas allow null element/snapshot; optional locally issued snapshot pins a specific window. Raw HWND/model coordinates remain rejected. Fresh window resolution is ambiguous when multiple windows cannot be pinned. Existing permission, high-risk Enter/Ctrl+S approval, STOP, takeover and task text/application scope remain. Added safe TaskId/ActionId/type/backend/Python invocation/window HWND/PID/result/dispatch/verification/category and stale rejection reason diagnostics; no entered text, clipboard contents, UI names or credentials logged.

Planner checks requested typing, scrolling and Ctrl+A/C/F/S completion, permits one missing-step continuation, never repeats successful calls and reports incomplete tasks. This is a bounded supported-request checklist, not a universal semantic plan verifier.

## Verification (2026-10-12)

- Python protocol/identity tests: 16/16 PASS.
- Focused Computer Use tests: 33/33 PASS.
- Focused UI tests: 37/37 PASS.
- Build: PASS, zero warnings/errors.
- Initial full regression: 266/267, new comma-separated sequence test failed; repaired and final full rerun 267/267 PASS.
- Publish: PASS to artifacts/part73-repair/DesktopAgent.App.exe, Python sources packaged. Scrolling uses bounded SendInput pointer/wheel instead of pywinauto's SetCursorPos so injected movement does not trigger physical-user takeover detection. Actual runtime remains pending.
- Actual chat A Notepad type/select/copy: NOT RUN.
- Actual chat B Chrome down: NOT RUN.
- Actual chat C Chrome up: NOT RUN.
- Actual chat D Chrome Find/Python: NOT RUN.
- Actual chat E wrong-window protection: NOT RUN.
- Actual chat F stale-reference change: NOT RUN.
- Actual chat G STOP midway: NOT RUN.
- Actual chat H user takeover: NOT RUN.

Part 7.2 single Notepad typing remains previously user-verified; that is not evidence for Part 7.3 multi-actions.

## Files changed

src/DesktopAgent.Core/ComputerUse/ComputerUseModels.cs
src/DesktopAgent.Infrastructure/ComputerUse/ComputerActionTools.cs
src/DesktopAgent.Infrastructure/ComputerUse/ComputerActionExecutor.cs
src/DesktopAgent.Infrastructure/ComputerUse/HybridComputerBackend.cs
src/DesktopAgent.Infrastructure/ComputerUse/PythonComputerBackend.cs
src/DesktopAgent.Infrastructure/ComputerUse/PythonWorkerClient.cs
src/DesktopAgent.Infrastructure/UiAwareness/WindowsComputerActions.cs
src/DesktopAgent.Infrastructure/Ai/AssistantConversationService.cs
src/DesktopAgent.PythonWorker/windows.py
src/DesktopAgent.PythonWorker/worker.py
src/DesktopAgent.PythonWorker/validation.py
src/DesktopAgent.PythonWorker/tests/test_protocol.py
tests/DesktopAgent.Tests/Part7Tests.cs
artifacts/part73-scroll-test.html

No Part 8, vision or developer/Claude integration changes.

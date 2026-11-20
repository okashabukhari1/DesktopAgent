# Part 7.4 — Python repair applied; live acceptance pending

Latest runtime C: FAIL. User screenshot reports verified Chrome Ctrl+F followed by ChromeFindEditorUnavailableOrAmbiguous; user confirms only `P` entered. This establishes partial dispatch and failure in repeated global field discovery after initial validation, not failure to dispatch Ctrl+F. No automatic repeat. Repaired typing to retain the exact validated live field and independently check runtime ID, automation ID/type, eligibility, focus, explicit password/read-only state, process and browser-owned/non-page ancestry before every character. Foreground/popup guard uses this pinned field instead of re-enumerating the entire Find surface. Distinct error categories report identity, focus, owner, eligibility or protection failures. 21/21 Python regressions PASS, including simulated disappearing global discovery after first character while the pinned field stays valid, and immediate safe stop when its live focus changes after `Py`. No rebuild. Runtime C retest still pending; do not mark complete.

## Causes and evidence

Scroll: the existing code sent one bounded wheel event after validating Chrome process/window identity, foreground, page Document runtime, bounds and pointer hit-test. Verification recognized only a directional change in UIA ScrollPattern.CurrentVerticalScrollPercent. Missing/unsupported data or no observed change produced InputDispatchedUnverified. That is not evidence of no movement. User previously reported visually working scrolling; this repair does not replace or retry wheel dispatch.

Find: read-only inspection of the manually opened Chrome Find bar shows a separate browser-owned surface containing Edit Find, AutomationId view_1029, and Close find bar, view_1032. The old resolver enumerated only the main window descendants and required the ancestor chain to reach that exact HWND. It could verify Ctrl+F on one capture but fail subsequent field resolution. Additionally execute() constructed a new backend each request, losing connected workflow state.

## Changes

- Read-only fallback checks coherent directional translation of at least three stable accessible text/link/image runtime identities, with unchanged viewport and horizontal layout/element height. Percentage remains supported. No screenshot vision, DOM injection, browser-debugging listener, text logging or retries added.
- No readable movement signal still returns InputDispatchedUnverified. Worker data explicitly states the page may have moved.
- Find resolution includes same-process surfaces whose Win32 root owner is the validated Chrome HWND, pairs the live-observed editor and Close identifiers, excludes page Document ancestry, and requires explicit non-password/writable evidence. Duplicate traversal is de-duplicated by runtime identity. Foreign-process/foreign-owner roots cannot qualify.
- Chrome-owned Find focus/popup can satisfy foreground/modal checks only through validated field ownership; unrelated popups remain blocked.
- Same existing private worker now reuses backend state between requests. Verified Ctrl+F establishes a 45-second, exact HWND/PID/creation/title/editor-runtime context. Chrome typing requires and consumes that context once, revalidates field/focus for every character, and verifies ValuePattern query equality where available. Missing/expired/changed context fails before text. No blind webpage typing fallback.
- Escape is supported specifically for the established Find surface. Closure requires absence of its owned Close marker, not merely inability to resolve an Edit.
- Existing C# policy, action text/application scope, STOP/takeover monitor and timeout/cancellation termination of the worker are unchanged. No C# or executable rebuild.

## Files changed

src/DesktopAgent.PythonWorker/windows.py
src/DesktopAgent.PythonWorker/worker.py
src/DesktopAgent.PythonWorker/tests/test_protocol.py
artifacts/part73-chat-keys/python-worker/windows.py
artifacts/part73-chat-keys/python-worker/worker.py
PART74_IMPLEMENTATION_REPORT.md

Source and packaged Python hashes match. Existing child must be unloaded by exiting/restarting the app.

## Actual runtime tests

| Test | Status | Evidence |
| --- | --- | --- |
| A: assistant Chrome down 3 | NOT RUN after repair | Requires user-run actual chat verification |
| B: assistant Chrome up 3 | NOT RUN after repair | Requires reverse movement evidence |
| C: assistant Ctrl+F then Python | FAIL, retest pending | User observed partial `P`; pinned-field correction not yet live verified |
| D: assistant Escape | NOT RUN after repair | Closure proof pending |
| E: assistant Notepad Hello World | NOT RUN after repair | Prior Part 7.2/7.3 functionality does not substitute for this regression |
| F: STOP midway | NOT RUN after repair | Must be exercised during actual task |

The computer-use skill's runtime safety check repeatedly stopped automated Chrome keyboard input because URL enforcement could not establish the current URL, even on Python.org. No alternate automation was used to bypass that check. Live inputs must be performed by the user in the actual agent; read-only diagnostics remain available.

## Regression verification and limitations

Python tests 20/20 PASS, including coherent movement threshold/direction, unchanged/missing/insufficient geometry, browser-owned popup discovery, webpage spoof rejection, foreign-process rejection, no typing without Find context, expired-context rejection, and reuse of one backend across private-worker requests.

No build/publish performed, as requested. No Part 8. Chromium view identifiers are version-dependent and intentionally fail closed if changed. Incomplete accessibility can still prevent verification; multiple moving content elements can be an imperfect signal on highly animated pages. No exact scroll offset is claimed from geometry. No blind keyboard continuation is provided when Find ownership/focus cannot be established. Actual acceptance is NOT COMPLETE.

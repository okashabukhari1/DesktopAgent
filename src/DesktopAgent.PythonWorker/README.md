# Python Computer Worker — Milestone B in progress, not a production backend

The existing WPF execution path is unchanged. This is a Notepad-only development worker, not a replacement AI brain or a production authorization system. A C# private child-process client now exists in Infrastructure, but is not registered for app actions. Do not enable it as a production execution backend until milestones A–C have passed.

## Environment

Use your configured Python interpreter. Development machine: Python 3.14.7 with pywinauto 0.6.9 already installed. `requirements.txt` records the dependency; startup never installs packages. pyautogui is not needed for this first milestone.

From the Agent repository:

```powershell
& "$env:LOCALAPPDATA\Python\pythoncore-3.14-64\python.exe" .\src\DesktopAgent.PythonWorker\worker.py --health
& "$env:LOCALAPPDATA\Python\pythoncore-3.14-64\python.exe" -m unittest discover -s .\src\DesktopAgent.PythonWorker\tests -v
```

These commands do not discover, focus, click, or type into desktop windows.

## User-run standalone acceptance test

```powershell
& "$env:LOCALAPPDATA\Python\pythoncore-3.14-64\python.exe" .\src\DesktopAgent.PythonWorker\worker.py --notepad-smoke-test
```

This explicitly starts Notepad with a uniquely named, empty disposable file under `artifacts/part72-smoke`. It finds the visible window independently of the launch PID and types **Hello World** only after verifying the process image, process creation identity, top-level HWND, document identity, normal input desktop, non-elevated state, lack of another active popup, and the known Notepad editor's focus/read-only/password state. `Document` as well as `Edit` controls are supported; arbitrary top-level keyboard fallback is deliberately forbidden. Unsupported editor classes or unknown read-only state stop before text input.

Leave input untouched during the test. Foreground/editor takeover is checked between individual literal Unicode characters. Ctrl+C in the console cancels the child; the parent terminates only its own worker after 25 seconds, including a hung accessibility provider. A potentially dispatched action is never automatically retried. The document and its window are left in place; no unrelated files are read, saved, or closed. The document's short range immediately preceding the caret is compared locally; content is not logged or returned.

Success requires **both** visible Hello World and a `CompletedAndVerified` result. `InputDispatchedUnverified` is not success. If verification is unsupported, record the exact error/status and inspect the disposable document yourself. Do not weaken focus/security checks to make a test pass.

## Protocol

UTF-8 newline-delimited JSON over private process stdin/stdout. Diagnostic output goes to stderr. No network listener, eval, script arguments, arbitrary executable paths, shell commands, raw pointer coordinates or general application access.

```json
{"id":"health-1","action":"health","target":{},"arguments":{},"timeout_ms":10000}
```

Initial allowlisted actions: `health`, `find_window`, `focus_window`, `get_window_info`, `get_focused_control`, `type_text`. Keyboard shortcuts, clicking and scrolling intentionally await proof of the first three required real actions. Standard worker mode rejects focus/type operations unless explicitly started with `--allow-input-for-development`. That flag is a standalone development opt-in, **not** a substitute for the existing C# policy/STOP/onboarding checks.

Targets require `application: Notepad`; locally issued HWND/PID/creation/title identity may pin a request. Ambiguity is rejected. Requests are limited to 8192 characters, text to 2000 characters and timeout to 100–15000ms. JSON duplicates/unknown properties are refused. Only one request can be active; another request receives Busy. A concurrent `{"id":"active-id","action":"cancel"}` sets its cancellation event. EOF revokes the current action. Native calls already dispatched cannot be undone. A future C# client must enforce its own hard process timeout and kill/restart only after it has recorded whether input may have occurred.

Result states include `CompletedAndVerified`, `InputDispatchedUnverified`, `Failed`, `Cancelled`, `TimedOut`, `TargetAmbiguous`, and `PermissionDenied`. Returned errors are fixed categories, never raw exception text or typed contents.

## Next gate

Milestone A passed with the user's real desktop result: `CompletedAndVerified`, `input_dispatched: true`, and visible Hello World. Milestone B's C# NDJSON health, repeated correlated requests, invalid arguments, pre-cancellation, missing-Python, crash, timeout and wrong-response-ID checks passed. The fixed C# typing test also returned `CompletedAndVerified`, and the user confirmed visible Hello World. Policy execution (C), AI chat input (D), and multi-action selection (E) remain unfinished and must proceed in that order.

The C# client discovers/configures Python, redirects UTF-8 private pipes, bounds responses, serializes requests, applies a hard timeout, terminates only its own child on cancellation/transport failure, and never retries potentially dispatched input. EOF requests graceful shutdown; an unresponsive child is terminated. No raw request/diagnostic content is logged. These safeguards are not yet wired to the production permission engine or emergency STOP.

From the repository, harmless C# transport check:

```powershell
dotnet .\tests\DesktopAgent.Tests\bin\Release\net10.0-windows\DesktopAgent.Tests.dll --python-worker-health .
```

User-run fixed C# typing test (development-only; new disposable Notepad):

```powershell
dotnet .\tests\DesktopAgent.Tests\bin\Release\net10.0-windows\DesktopAgent.Tests.dll --python-worker-smoke .
```

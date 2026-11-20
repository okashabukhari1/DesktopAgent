"""One-action NDJSON worker. Milestone A only; production C# integration is not enabled."""
import argparse
import contextlib
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import threading
import time
import uuid
from validation import WorkerError, decode, validate


def execute(request, cancelled, allow_input=False):
    dispatched = [False]
    identity = None
    request_id = request.get("id") if isinstance(request, dict) else None
    request_id = request_id if isinstance(request_id, str) and len(request_id) <= 64 else None
    try:
        validate(request)
        deadline = time.monotonic() + request["timeout_ms"] / 1000
        def check():
            if cancelled.is_set():
                raise WorkerError("Cancelled")
            if time.monotonic() >= deadline:
                raise WorkerError("TimedOut")
        check()
        from windows import health, NotepadWindows
        action = request["action"]
        if action == "health":
            return {"id": request_id, "status": "CompletedAndVerified", "input_dispatched": False, "verified": True, "data": health()}
        if action in ("focus_window", "type_text") and not allow_input:
            raise WorkerError("PermissionDenied")
        backend = NotepadWindows()
        window, identity = backend.resolve(request["target"])
        check()
        verified = True
        data = identity
        if action == "focus_window":
            backend.focus(window, identity, check)
        elif action == "inspect_editor":
            editor = backend.checked_editor(window, request["target"].get("editor_runtime"))
            data = {"target": identity, "editor_runtime": list(editor.element_info.element.GetRuntimeId()),
                    "control_type": editor.element_info.control_type, "class_name": editor.element_info.class_name,
                    "is_password": False, "is_readonly": False, "is_enabled": True, "is_visible": True}
        elif action == "get_focused_control":
            backend.guard(window, identity, check)
            editor = backend.editor(window, focused_only=True)
            data = {"target": identity, "control_type": editor.element_info.control_type, "editor_focused": True}
        elif action == "type_text":
            verified = backend.type_text(window, identity, request["arguments"]["text"], check, dispatched, request["target"].get("editor_runtime"))
        return {"id": request_id, "status": "CompletedAndVerified" if verified else "InputDispatchedUnverified", "input_dispatched": dispatched[0], "verified": verified, "data": data}
    except WorkerError as error:
        code = error.code
    except ImportError:
        code = "MissingPackage"
    except Exception:
        code = "BackendFailure"  # never echo provider/OS exception text or input
    status = code if code in ("Cancelled", "TimedOut", "TargetAmbiguous", "PermissionDenied") else "Failed"
    return {"id": request_id, "status": status, "input_dispatched": dispatched[0], "verified": False, "error": code}


def serve(allow_input):
    inbox = queue.Queue(maxsize=1)
    active = {"id": None, "cancel": threading.Event()}
    lock = threading.Lock()
    output_lock = threading.Lock()
    def emit(value):
        with output_lock:
            print(json.dumps(value, ensure_ascii=True), flush=True)
    def read():
        while True:
            line = sys.stdin.readline(8194)
            if not line:
                with lock:
                    active["cancel"].set()
                inbox.put(None)
                return
            try:
                if len(line) > 8192:
                    while line and not line.endswith("\n"):
                        line = sys.stdin.readline(8194)
                    raise WorkerError("RequestTooLarge")
                request = decode(line)
                if isinstance(request, dict) and set(request) == {"id", "action"} and request["action"] == "cancel":
                    with lock:
                        if request["id"] == active["id"]:
                            active["cancel"].set()
                    continue
                validate(request)
                with lock:
                    if active["id"] is not None:
                        raise queue.Full()
                    active["id"], active["cancel"] = request["id"], threading.Event()
                    inbox.put_nowait(request)
            except queue.Full:
                emit({"id": request.get("id"), "status": "Failed", "verified": False, "input_dispatched": False, "error": "Busy"})
            except WorkerError as error:
                emit({"id": None, "status": "Failed", "verified": False, "input_dispatched": False, "error": error.code})
    threading.Thread(target=read, daemon=True).start()
    while (request := inbox.get()) is not None:
        with lock:
            cancelled = active["cancel"]
        with contextlib.redirect_stdout(sys.stderr):
            response = execute(request, cancelled, allow_input)
        with lock:
            active["id"] = None
        emit(response)


def smoke_test():
    # User-run, fixed disposable artifact; no unrelated documents are read/saved/closed.
    from windows import health
    health()
    root = Path(__file__).resolve().parents[2] / "artifacts" / "part72-smoke"
    root.mkdir(parents=True, exist_ok=True)
    document = root / ("agent-smoke-" + uuid.uuid4().hex + ".txt")
    document.touch(exist_ok=False)
    executable = Path(os.environ.get("SystemRoot", "C:\\Windows")) / "System32" / "notepad.exe"
    subprocess.Popen([str(executable), str(document)])
    title = document.name + " - Notepad"
    for attempt in range(30):
        request = {"id": "standalone-smoke", "action": "find_window", "target": {"application": "Notepad", "title": title}, "arguments": {}, "timeout_ms": 15000}
        with contextlib.redirect_stdout(sys.stderr):
            result = execute(request, threading.Event())
            if result.get("error") == "TargetNotFound":
                request["target"]["title"] = document.stem + " - Notepad"
                result = execute(request, threading.Event())
        if result["status"] == "CompletedAndVerified":
            break
        time.sleep(0.25)
    else:
        print(json.dumps(result)); return 1
    print("Disposable Notepad found. Leave keyboard/mouse untouched. Ctrl+C in this console cancels.", file=sys.stderr)
    request.update(action="type_text", target=result["data"], arguments={"text": "Hello World"})
    try:
        with contextlib.redirect_stdout(sys.stderr):
            result = execute(request, threading.Event(), allow_input=True)
    except KeyboardInterrupt:
        result = {"status": "Cancelled", "verified": False, "input_dispatched": True}
    print(json.dumps(result))
    return 0 if result["status"] == "CompletedAndVerified" else 1


def bounded_smoke_test():
    # Hard outer timeout also covers an unresponsive accessibility provider. Kill only our worker.
    process = subprocess.Popen([sys.executable, str(Path(__file__).resolve()), "--notepad-smoke-test-child"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
    try:
        output, diagnostics = process.communicate(timeout=25)
        print(output.strip())
        print(diagnostics.strip(), file=sys.stderr)
        return process.returncode
    except (subprocess.TimeoutExpired, KeyboardInterrupt) as error:
        process.kill()
        process.communicate()
        print(json.dumps({"status": "TimedOut" if isinstance(error, subprocess.TimeoutExpired) else "Cancelled", "verified": False, "input_dispatched": True, "error": "WorkerTerminatedNoRetry"}))
        return 1


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("--health", action="store_true")
    parser.add_argument("--notepad-smoke-test", action="store_true")
    parser.add_argument("--notepad-smoke-test-child", action="store_true", help=argparse.SUPPRESS)
    parser.add_argument("--allow-input-for-development", action="store_true", help="Not a production authorization mechanism; C# integration is not complete.")
    options = parser.parse_args()
    if options.health:
        print(json.dumps(execute({"id": "health", "action": "health", "target": {}, "arguments": {}, "timeout_ms": 15000}, threading.Event())))
    elif options.notepad_smoke_test:
        raise SystemExit(bounded_smoke_test())
    elif options.notepad_smoke_test_child:
        raise SystemExit(smoke_test())
    else:
        serve(options.allow_input_for_development)

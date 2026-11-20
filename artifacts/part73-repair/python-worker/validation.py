"""Fixed protocol; no Python, shell commands, coordinates or executable paths accepted."""
import json
import re


class WorkerError(Exception):
    def __init__(self, code):
        self.code = code
        super().__init__(code)


def decode(line):
    if len(line) > 8192:
        raise WorkerError("RequestTooLarge")
    def unique(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise WorkerError("DuplicateProperty")
            value[key] = item
        return value
    try:
        return json.loads(line, object_pairs_hook=unique)
    except (ValueError, RecursionError):
        raise WorkerError("InvalidJson") from None


def validate(request):
    if not isinstance(request, dict) or set(request) != {"id", "action", "target", "arguments", "timeout_ms"}:
        raise WorkerError("InvalidEnvelope")
    if not isinstance(request["id"], str) or not re.fullmatch(r"[A-Za-z0-9_-]{1,64}", request["id"]):
        raise WorkerError("InvalidRequestId")
    if type(request["timeout_ms"]) is not int or not 100 <= request["timeout_ms"] <= 15000:
        raise WorkerError("InvalidTimeout")
    action = request["action"]
    if action not in {"health", "find_window", "focus_window", "get_window_info", "get_focused_control", "inspect_editor", "type_text", "press_shortcut", "inspect_scroll", "scroll"}:
        raise WorkerError("UnsupportedAction")
    target, args = request["target"], request["arguments"]
    if not isinstance(target, dict) or not isinstance(args, dict):
        raise WorkerError("InvalidArguments")
    if action == "health":
        if target or args:
            raise WorkerError("HealthTakesNoArguments")
        return request
    if set(target) - {"application", "hwnd", "pid", "created", "title", "editor_runtime"} or target.get("application") not in {"Notepad", "Chrome"}:
        raise WorkerError("UnsupportedTarget")
    for field in ("hwnd", "pid", "created"):
        if field in target and (type(target[field]) is not int or target[field] <= 0):
            raise WorkerError("InvalidTargetIdentity")
    if "title" in target and (not isinstance(target["title"], str) or len(target["title"]) > 256):
        raise WorkerError("InvalidTargetIdentity")
    if "editor_runtime" in target and (not isinstance(target["editor_runtime"], list) or not 1 <= len(target["editor_runtime"]) <= 32 or any(type(x) is not int for x in target["editor_runtime"])):
        raise WorkerError("InvalidEditorIdentity")
    if action == "type_text":
        if set(args) != {"text"} or not isinstance(args["text"], str) or not 1 <= len(args["text"]) <= 2000:
            raise WorkerError("InvalidText")
        if any(ord(c) < 32 and c not in "\n\t" or 0xD800 <= ord(c) <= 0xDFFF for c in args["text"]):
            raise WorkerError("InvalidText")
    elif action == "press_shortcut":
        if set(args) != {"key"} or not isinstance(args["key"], str) or args["key"] not in SHORTCUT_KEYS:
            raise WorkerError("UnsupportedShortcut")
    elif action == "scroll":
        if target["application"] != "Chrome" or set(args) != {"direction", "amount"} or args["direction"] not in ("up", "down") or type(args["amount"]) is not int or not 1 <= args["amount"] <= 3:
            raise WorkerError("InvalidScroll")
    elif args:
        raise WorkerError("UnexpectedArguments")
    return request


SHORTCUT_KEYS = {"Ctrl+A", "Ctrl+C", "Ctrl+F", "Ctrl+S", "Escape", "Enter", "Tab", "Shift+Tab", "Left", "Right", "Up", "Down"}


def literal_keys(text):
    # pywinauto interprets these symbols as key syntax unless escaped.
    escapes = {"{": "{{}", "}": "{}}", "+": "{+}", "^": "{^}", "%": "{%}", "~": "{~}", "(": "{(}", ")": "{)}", "\n": "{ENTER}", "\t": "{TAB}"}
    return "".join(escapes.get(c, c) for c in text)

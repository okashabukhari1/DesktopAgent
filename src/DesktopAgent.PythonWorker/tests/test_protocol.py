import json
from pathlib import Path
import sys
import threading
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from validation import WorkerError, decode, validate, literal_keys
from worker import execute


class ProtocolTests(unittest.TestCase):
    def request(self, action="type_text"):
        return {"id": "test-1", "action": action, "target": {"application": "Notepad"}, "arguments": {"text": "Hello World"} if action == "type_text" else {}, "timeout_ms": 10000}

    def test_valid_unicode(self):
        request = self.request(); request["arguments"]["text"] = "Hello + {World} اردو"
        self.assertEqual(validate(decode(json.dumps(request))), request)

    def test_literal_key_syntax(self):
        self.assertEqual(literal_keys("+^%{}"), "{+}{^}{%}{{}{}}")

    def test_unknown_fields(self):
        for field in ("code", "script", "coordinates"):
            request = self.request(); request[field] = "bad"
            with self.assertRaises(WorkerError): validate(request)

    def test_duplicates(self):
        with self.assertRaises(WorkerError): decode('{"id":"a","id":"b"}')

    def test_arbitrary_app_denied(self):
        request = self.request(); request["target"]["application"] = "PowerShell"
        with self.assertRaises(WorkerError): validate(request)

    def test_bool_not_integer_identity(self):
        request = self.request(); request["target"]["hwnd"] = True
        with self.assertRaises(WorkerError): validate(request)

    def test_timeout_bounds(self):
        for value in (0, 15001, True, "1000"):
            request = self.request(); request["timeout_ms"] = value
            with self.assertRaises(WorkerError): validate(request)

    def test_text_bounds(self):
        for value in ("", "a" * 2001, "\x00", "\ud800", None):
            request = self.request(); request["arguments"]["text"] = value
            with self.assertRaises(WorkerError): validate(request)

    def test_cancel_before_dependencies_or_input(self):
        cancelled = threading.Event(); cancelled.set()
        result = execute(self.request(), cancelled, allow_input=True)
        self.assertEqual(result["status"], "Cancelled")
        self.assertFalse(result["input_dispatched"])

    def test_health_arguments(self):
        request = self.request("health"); request["target"] = {}
        self.assertEqual(validate(request), request)
        request["arguments"] = {"text": "not allowed"}
        with self.assertRaises(WorkerError): validate(request)

    def test_invalid_action(self):
        result = execute(self.request("exec"), threading.Event())
        self.assertEqual(result["error"], "UnsupportedAction")
        self.assertFalse(result["verified"])

    def test_editor_identity_is_strict(self):
        request = self.request("inspect_editor")
        request["target"]["editor_runtime"] = [42, 1, -7]
        self.assertEqual(validate(request), request)
        for invalid in ([], [True], ["42"], [1] * 33, "42"):
            request["target"]["editor_runtime"] = invalid
            with self.assertRaises(WorkerError): validate(request)

    def test_editor_runtime_changes_fail_closed(self):
        from windows import NotepadWindows
        class Element:
            def GetRuntimeId(self): return [42, 2]
        class Info:
            element = Element()
        class Editor:
            element_info = Info()
        backend = object.__new__(NotepadWindows)
        backend.editor = lambda window: Editor()
        with self.assertRaises(WorkerError) as error:
            backend.checked_editor(object(), [42, 1])
        self.assertEqual(error.exception.code, "EditorIdentityChanged")

    def test_auto_title_requires_same_pinned_tab_and_process(self):
        from windows import NotepadWindows
        from types import SimpleNamespace
        backend = object.__new__(NotepadWindows)
        identity = {"hwnd": 10, "pid": 20, "created": 30, "title": "Untitled - Notepad"}
        current = dict(identity, title="He - Notepad")
        backend.identity = lambda window: current
        backend.check_desktop = lambda: None
        backend.selected_tab_runtime = lambda window: (42, 1)
        backend.user = SimpleNamespace(GetForegroundWindow=lambda: 10,
            GetLastActivePopup=lambda hwnd: hwnd, GetAsyncKeyState=lambda key: 0)
        window = SimpleNamespace(is_visible=lambda: True, is_enabled=lambda: True)
        backend.guard(window, identity, lambda: None, (42, 1))
        # Title changes without a pinned tab (classic mode) must still stop.
        with self.assertRaises(WorkerError): backend.guard(window, identity, lambda: None)
        backend.selected_tab_runtime = lambda window: (42, 2)
        with self.assertRaises(WorkerError): backend.guard(window, identity, lambda: None, (42, 1))
        backend.selected_tab_runtime = lambda window: (42, 1)
        current["pid"] = 21
        with self.assertRaises(WorkerError): backend.guard(window, identity, lambda: None, (42, 1))

    def test_shortcuts_fixed_allowlist(self):
        from validation import SHORTCUT_KEYS
        for key in SHORTCUT_KEYS:
            request = self.request("press_shortcut")
            request["arguments"] = {"key": key}
            self.assertEqual(validate(request), request)
        for key in ("Win+R", "Alt+Tab", "Ctrl+V", "__import__('os')", None, [], "{ENTER}"):
            request["arguments"] = {"key": key}
            with self.assertRaises(WorkerError): validate(request)

    def test_chrome_scroll_is_bounded(self):
        request = self.request("scroll")
        request["target"] = {"application": "Chrome"}
        for direction in ("up", "down"):
            request["arguments"] = {"direction": direction, "amount": 3}
            self.assertEqual(validate(request), request)
        for args in ({"direction": "down", "amount": 4}, {"direction": "down", "amount": True},
                     {"direction": "left", "amount": 1}, {"direction": "down", "amount": 1, "x": 5}):
            request["arguments"] = args
            with self.assertRaises(WorkerError): validate(request)


class ChromeVerificationTests(unittest.TestCase):
    def test_find_typing_pins_field_without_per_character_discovery(self):
        from windows import NotepadWindows
        from types import SimpleNamespace as NS
        from unittest.mock import patch
        import time
        for interrupt in (False, True):
            backend = object.__new__(NotepadWindows)
            backend.application = "Chrome"
            identity = {"hwnd": 10, "pid": 7, "created": 9, "title": "test"}
            backend._find_context = (identity.copy(), (42, 1), time.monotonic())
            window = NS(handle=10, process_id=lambda: 7)
            parent = NS(handle=10, element_info=NS(control_type="Window"))
            value = NS(CurrentIsReadOnly=False, CurrentValue="")
            field = NS(element_info=NS(control_type="Edit", automation_id="view_1029",
                element=NS(GetRuntimeId=lambda: [42, 1], GetCurrentPropertyValueEx=lambda *args: False)),
                iface_value=value, process_id=lambda: 7, parent=lambda: parent, set_focus=lambda: None,
                is_visible=lambda: True, is_enabled=lambda: True,
                has_keyboard_focus=lambda: not interrupt or len(value.CurrentValue) < 2)
            backend.user = NS(GetAncestor=lambda handle, mode: 10)
            backend.guard = lambda *args, **kwargs: None
            backend.focus = lambda *args: None
            backend.checked_editor = lambda *args: field
            def discover(*args, **kwargs):
                if value.CurrentValue: raise WorkerError("ChromeFindEditorUnavailableOrAmbiguous")
                return field
            backend.find_editor = discover
            def send(keys, **kwargs):
                value.CurrentValue = "" if keys == "^a" else value.CurrentValue + keys
            dispatched = [False]
            with patch("pywinauto.keyboard.send_keys", send):
                if interrupt:
                    with self.assertRaises(WorkerError) as error:
                        backend.type_text(window, identity, "Python", lambda: None, dispatched)
                    self.assertEqual(error.exception.code, "ChromeFindFocusChanged")
                    self.assertEqual(value.CurrentValue, "Py")
                else:
                    self.assertTrue(backend.type_text(window, identity, "Python", lambda: None, dispatched))
                    self.assertEqual(value.CurrentValue, "Python")
            self.assertTrue(dispatched[0])
            self.assertIsNone(backend._find_context)

    def test_private_worker_reuses_backend_between_requests(self):
        from unittest.mock import patch
        class Backend:
            instances = 0
            def __init__(self, application):
                Backend.instances += 1
                self.context = "retained"
            def resolve(self, target):
                return object(), {"application": "Chrome", "hwnd": 1, "pid": 2, "created": 3, "title": "test"}
        request = {"id": "context", "action": "get_window_info", "target": {"application": "Chrome"}, "arguments": {}, "timeout_ms": 1000}
        cache = {}
        with patch("windows.NotepadWindows", Backend):
            self.assertTrue(execute(request, threading.Event(), backends=cache)["verified"])
            self.assertTrue(execute(request, threading.Event(), backends=cache)["verified"])
        self.assertEqual(Backend.instances, 1)
        self.assertEqual(cache["Chrome"].context, "retained")

    def test_scroll_requires_coherent_directional_translation(self):
        from windows import NotepadWindows
        before = {(1, i): (10, i * 80, 110, i * 80 + 20) for i in range(4)}
        after = {k: (v[0], v[1] - 120, v[2], v[3] - 120) for k, v in before.items()}
        self.assertTrue(NotepadWindows.anchors_moved(before, after, "down"))
        self.assertFalse(NotepadWindows.anchors_moved(before, after, "up"))
        self.assertFalse(NotepadWindows.anchors_moved(before, before, "down"))
        self.assertFalse(NotepadWindows.anchors_moved(before, dict(list(after.items())[:2]), "down"))
        self.assertFalse(NotepadWindows.anchors_moved({}, {}, "down"))

    def test_find_owned_popup_and_page_spoof_exclusion(self):
        from windows import NotepadWindows
        from types import SimpleNamespace as NS
        class Control:
            def __init__(self, runtime, kind, automation_id="", parent=None, handle=0):
                self.element_info = NS(control_type=kind, automation_id=automation_id,
                    element=NS(GetRuntimeId=lambda: runtime, GetCurrentPropertyValueEx=lambda *args: False))
                self.handle, self._parent = handle, parent
                self.iface_value = NS(CurrentIsReadOnly=False)
            def is_visible(self): return True
            def is_enabled(self): return True
            def has_keyboard_focus(self): return True
            def parent(self): return self._parent
        root = Control([1], "Window", handle=10)
        root.process_id = lambda: 7
        root.descendants = lambda: []
        popup = Control([2], "Window", handle=20)
        popup.process_id = lambda: 7
        field = Control([3], "Edit", "view_1029", popup)
        close = Control([4], "Button", "view_1032", popup)
        popup.descendants = lambda: [field, close]
        backend = object.__new__(NotepadWindows)
        backend.desktop = NS(windows=lambda **args: [root, popup])
        backend.user = NS(GetAncestor=lambda handle, mode: 10 if handle in (10, 20) else handle)
        self.assertIs(backend.find_editor(root, True), field)
        document = Control([5], "Document", parent=popup)
        field._parent = document
        with self.assertRaises(WorkerError): backend.find_editor(root)
        field._parent = popup
        popup.process_id = lambda: 8
        with self.assertRaises(WorkerError): backend.find_editor(root)

    def test_chrome_typing_requires_recent_connected_find_workflow(self):
        from windows import NotepadWindows
        backend = object.__new__(NotepadWindows)
        backend.application = "Chrome"
        dispatched = [False]
        with self.assertRaises(WorkerError) as error:
            backend.type_text(object(), {"hwnd": 1, "pid": 2, "created": 3, "title": "test"}, "Python", lambda: None, dispatched)
        self.assertEqual(error.exception.code, "ChromeFindWorkflowNotEstablished")
        self.assertFalse(dispatched[0])
        import time
        identity = {"hwnd": 1, "pid": 2, "created": 3, "title": "test"}
        backend._find_context = (identity, (42, 1), time.monotonic() - 46)
        with self.assertRaises(WorkerError): backend.type_text(object(), identity, "Python", lambda: None, dispatched)
        self.assertFalse(dispatched[0])


if __name__ == "__main__": unittest.main()

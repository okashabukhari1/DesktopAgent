"""Notepad-only milestone A backend. Identity is independently revalidated before input."""
import ctypes
from ctypes import wintypes as w
import os
import sys
import time
from validation import WorkerError


class MouseInput(ctypes.Structure):
    _fields_ = [("dx", w.LONG), ("dy", w.LONG), ("mouseData", w.DWORD),
                ("dwFlags", w.DWORD), ("time", w.DWORD), ("dwExtraInfo", ctypes.c_size_t)]


class Input(ctypes.Structure):
    _fields_ = [("type", w.DWORD), ("mouse", MouseInput)]


def health():
    try:
        import pywinauto
        return {"available": sys.platform == "win32", "python_version": sys.version.split()[0], "pywinauto_version": pywinauto.__version__}
    except ImportError:
        raise WorkerError("MissingPackage:pywinauto") from None


class NotepadWindows:
    def __init__(self, application="Notepad"):
        if application not in ("Notepad", "Chrome"):
            raise WorkerError("UnsupportedTarget")
        self.application = application
        health()
        if sys.platform != "win32":
            raise WorkerError("WindowsRequired")
        from pywinauto import Desktop
        self.desktop = Desktop(backend="uia")
        self.user = ctypes.WinDLL("user32", use_last_error=True)
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.advapi = ctypes.WinDLL("advapi32", use_last_error=True)
        self.user.GetForegroundWindow.restype = w.HWND
        self.user.GetAncestor.argtypes = [w.HWND, w.UINT]
        self.user.GetAncestor.restype = w.HWND
        self.user.SendInput.argtypes = [w.UINT, ctypes.POINTER(Input), ctypes.c_int]
        self.user.SendInput.restype = w.UINT
        self.user.GetCursorPos.argtypes = [ctypes.POINTER(w.POINT)]
        self.user.GetLastActivePopup.argtypes = [w.HWND]
        self.user.GetLastActivePopup.restype = w.HWND
        self.user.OpenInputDesktop.argtypes = [w.DWORD, w.BOOL, w.DWORD]
        self.user.OpenInputDesktop.restype = w.HANDLE
        self.user.CloseDesktop.argtypes = [w.HANDLE]
        self.user.GetUserObjectInformationW.argtypes = [w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD, ctypes.POINTER(w.DWORD)]
        self.kernel.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
        self.kernel.OpenProcess.restype = w.HANDLE
        self.kernel.CloseHandle.argtypes = [w.HANDLE]
        self.kernel.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, ctypes.POINTER(w.DWORD)]
        self.kernel.GetProcessTimes.argtypes = [w.HANDLE] + [ctypes.POINTER(w.FILETIME)] * 4
        self.advapi.OpenProcessToken.argtypes = [w.HANDLE, w.DWORD, ctypes.POINTER(w.HANDLE)]
        self.advapi.GetTokenInformation.argtypes = [w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD, ctypes.POINTER(w.DWORD)]

    def check_desktop(self):
        desktop = self.user.OpenInputDesktop(0, False, 1)
        if not desktop:
            raise WorkerError("SecureDesktopUnavailable")
        try:
            name, needed = ctypes.create_unicode_buffer(128), w.DWORD()
            if not self.user.GetUserObjectInformationW(desktop, 2, name, ctypes.sizeof(name), ctypes.byref(needed)) or name.value.casefold() != "default":
                raise WorkerError("SecureDesktopUnavailable")
        finally:
            self.user.CloseDesktop(desktop)

    def identity(self, window):
        pid = window.process_id()
        process = self.kernel.OpenProcess(0x1000, False, pid)
        if not process:
            raise WorkerError("ProcessIdentityUnavailable")
        try:
            path, size = ctypes.create_unicode_buffer(32768), w.DWORD(32768)
            if not self.kernel.QueryFullProcessImageNameW(process, 0, path, ctypes.byref(size)):
                raise WorkerError("ProcessIdentityUnavailable")
            exe = path.value.casefold()
            system = os.path.join(os.environ.get("SystemRoot", "C:\\Windows"), "System32", "notepad.exe").casefold()
            packaged = os.path.join(os.environ.get("ProgramFiles", "C:\\Program Files"), "WindowsApps", "Microsoft.WindowsNotepad_").casefold()
            trusted_notepad = exe == system or (exe.startswith(packaged) and exe.endswith("\\notepad.exe"))
            chrome_paths = [os.path.join(os.environ.get(variable, ""), "Google", "Chrome", "Application", "chrome.exe").casefold()
                            for variable in ("ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA") if os.environ.get(variable)]
            if not (trusted_notepad if self.application == "Notepad" else exe in chrome_paths):
                raise WorkerError("UnsupportedTarget")
            token = w.HANDLE()
            if not self.advapi.OpenProcessToken(process, 8, ctypes.byref(token)):
                raise WorkerError("ElevationUnknown")
            try:
                elevated, needed = w.DWORD(), w.DWORD()
                if not self.advapi.GetTokenInformation(token, 20, ctypes.byref(elevated), ctypes.sizeof(elevated), ctypes.byref(needed)) or elevated.value:
                    raise WorkerError("ElevatedTargetDenied")
            finally:
                self.kernel.CloseHandle(token)
            times = [w.FILETIME() for _ in range(4)]
            if not self.kernel.GetProcessTimes(process, *(ctypes.byref(t) for t in times)):
                raise WorkerError("ProcessIdentityUnavailable")
            created = (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime
            return {"application": self.application, "hwnd": int(window.handle), "pid": pid, "created": created, "title": window.window_text()[:256]}
        finally:
            self.kernel.CloseHandle(process)

    def resolve(self, target):
        self.check_desktop()
        candidates = []
        for window in self.desktop.windows(control_type="Window", visible_only=True):
            if target.get("hwnd") and int(window.handle) != target["hwnd"]:
                continue
            try:
                identity = self.identity(window)
            except WorkerError as error:
                if error.code == "UnsupportedTarget":
                    continue
                if target.get("hwnd"):
                    raise
                continue
            if all(identity[k] == v for k, v in target.items() if k != "editor_runtime") and window.is_enabled():
                candidates.append((window, identity))
        if len(candidates) != 1:
            raise WorkerError("TargetAmbiguous" if len(candidates) > 1 else "TargetNotFound")
        return candidates[0]

    def selected_tab_runtime(self, window):
        tabs = window.descendants(control_type="TabItem")
        if not tabs:
            return None  # Classic Notepad retains its strict document-title guard.
        selected = []
        for tab in tabs:
            try:
                if tab.iface_selection_item.CurrentIsSelected:
                    selected.append(tuple(tab.element_info.element.GetRuntimeId()))
            except Exception:
                raise WorkerError("DocumentIdentityUnavailable") from None
        if len(selected) != 1:
            raise WorkerError("DocumentIdentityUnavailable")
        return selected[0]

    def guard(self, window, identity, check, document_runtime=None, find_control=None):
        check()
        self.check_desktop()
        current = self.identity(window)
        # Notepad's unsaved marker may change after the first character; the document name must not change.
        same_document = current["title"].lstrip("*") == identity["title"].lstrip("*")
        if document_runtime is not None:
            # Modern Notepad auto-renames untitled tabs while typing. A locally
            # pinned selected-tab runtime, not its mutable text, identifies it.
            same_document = self.selected_tab_runtime(window) == document_runtime
        if any(current[k] != identity[k] for k in ("hwnd", "pid", "created")) or not same_document or not window.is_visible() or not window.is_enabled():
            raise WorkerError("TargetChanged")
        foreground = int(self.user.GetForegroundWindow() or 0)
        if foreground != identity["hwnd"]:
            field = find_control if find_control is not None else self.find_editor(window, focused_only=True) if self.application == "Chrome" else None
            if self.application != "Chrome" or int(self.user.GetAncestor(foreground, 3) or 0) != identity["hwnd"] or field.top_level_parent().handle != foreground:
                raise WorkerError("UserTakeover")
        popup = int(self.user.GetLastActivePopup(identity["hwnd"]) or 0)
        if popup not in (0, identity["hwnd"]):
            field = find_control if find_control is not None else self.find_editor(window) if self.application == "Chrome" else None
            if self.application != "Chrome" or field.top_level_parent().handle != popup:
                raise WorkerError("ModalDialogDenied")
        if any(self.user.GetAsyncKeyState(key) & 0x8000 for key in (16, 17, 18, 91, 92)):
            raise WorkerError("UserKeysHeld")

    def editor(self, window, focused_only=False):
        if self.application == "Chrome":
            return self.find_editor(window, focused_only)
        # Do not assume a top-level focus is an editor. Modern Notepad may expose Document, not Edit.
        candidates = []
        for control in window.descendants():
            info = control.element_info
            if info.control_type not in ("Edit", "Document") or not control.is_visible() or not control.is_enabled():
                continue
            if info.class_name not in ("Edit", "RichEditD2DPT", "RichEditD2D", "RichEdit50W"):
                continue
            # UIA's default false is not evidence that a missing password property is safe.
            try:
                password = info.element.GetCurrentPropertyValueEx(30019, True)  # UIA IsPasswordPropertyId
            except Exception:
                continue
            if type(password) not in (bool, int) or password != 0 or control.top_level_parent().handle != window.handle:
                continue
            if focused_only and not control.has_keyboard_focus():
                continue
            try:
                readonly = control.iface_value.CurrentIsReadOnly
            except Exception:
                try:
                    readonly = control.iface_text.DocumentRange.GetAttributeValue(40015)  # UIA IsReadOnlyAttributeId
                except Exception:
                    continue
            if readonly is not False and readonly != 0:
                continue
            candidates.append(control)
        if len(candidates) != 1:
            raise WorkerError("EditorFocusUnavailable" if focused_only else "EditorUnavailableOrAmbiguous")
        return candidates[0]

    def find_surfaces(self, window):
        roots = [window]
        for candidate in self.desktop.windows(visible_only=True):
            if candidate.handle != window.handle and candidate.process_id() == window.process_id() and int(self.user.GetAncestor(candidate.handle, 3) or 0) == window.handle:
                roots.append(candidate)
        return roots

    def find_bar_present(self, window):
        return any(c.element_info.automation_id == "view_1032" and c.is_visible()
                   for root in self.find_surfaces(window) for c in root.descendants())

    def find_editor(self, window, focused_only=False):
        # Browser-owned Find only. Web-page Edit controls and address bars cannot
        # become typing targets merely because they have focus or matching text.
        candidates = []
        roots = self.find_surfaces(window)
        controls = []
        for root in roots:
            descendants = root.descendants()
            if not any(c.element_info.automation_id == "view_1032" and c.is_visible() for c in descendants):
                continue
            controls.extend(c for c in descendants if c.element_info.control_type == "Edit")
        seen = set()
        for control in controls:
            runtime = tuple(control.element_info.element.GetRuntimeId())
            if runtime in seen:
                continue
            seen.add(runtime)
            if control.element_info.automation_id != "view_1029":
                continue
            if not control.is_visible() or not control.is_enabled() or focused_only and not control.has_keyboard_focus():
                continue
            parent, in_page, reached_window = control.parent(), False, False
            for _ in range(32):
                if parent is None:
                    break
                if parent.element_info.control_type == "Document":
                    in_page = True
                    break
                if parent.handle and int(self.user.GetAncestor(parent.handle, 3) or 0) == window.handle:
                    reached_window = True
                    break
                parent = parent.parent()
            if in_page or not reached_window:
                continue
            try:
                password = control.element_info.element.GetCurrentPropertyValueEx(30019, True)
                readonly = control.iface_value.CurrentIsReadOnly
                if type(password) in (bool, int) and password == 0 and readonly == 0:
                    candidates.append(control)
            except Exception:
                continue
        if len(candidates) != 1:
            raise WorkerError("ChromeFindEditorUnavailableOrAmbiguous")
        return candidates[0]

    def validate_pinned_find(self, window, field, runtime):
        # Validate the live field itself, not a fresh global Find search whose tree
        # can be transient while Chrome updates match results after each character.
        if tuple(field.element_info.element.GetRuntimeId()) != tuple(runtime) or field.element_info.automation_id != "view_1029" or field.element_info.control_type != "Edit":
            raise WorkerError("ChromeFindIdentityChanged")
        if not field.is_visible() or not field.is_enabled():
            raise WorkerError("ChromeFindNotEligible")
        if not field.has_keyboard_focus():
            raise WorkerError("ChromeFindFocusChanged")
        password = field.element_info.element.GetCurrentPropertyValueEx(30019, True)
        readonly = field.iface_value.CurrentIsReadOnly
        if type(password) not in (bool, int) or password != 0 or type(readonly) not in (bool, int) or readonly != 0:
            raise WorkerError("ChromeFindProtectionUnknown")
        if field.process_id() != window.process_id():
            raise WorkerError("ChromeFindOwnerChanged")
        parent = field.parent()
        for _ in range(32):
            if parent is None or parent.element_info.control_type == "Document":
                raise WorkerError("ChromeFindOwnerChanged")
            if parent.handle and int(self.user.GetAncestor(parent.handle, 3) or 0) == window.handle:
                return
            parent = parent.parent()
        raise WorkerError("ChromeFindOwnerChanged")

    @staticmethod
    def scroll_anchors(document):
        # No text values retained. Compare stable accessible content identities and
        # bounds; viewport/fixed controls and nested documents are not anchors.
        anchors = {}
        pending = [(document, 0)]
        visited = 0
        while pending and visited < 160:
            control, depth = pending.pop(0)
            visited += 1
            if depth > 5:
                continue
            try:
                if control is not document and control.element_info.control_type == "Document":
                    continue
                if control.element_info.control_type in ("Text", "Hyperlink", "Image"):
                    r = control.rectangle()
                    if r.width() > 0 and r.height() > 0:
                        anchors[tuple(control.element_info.element.GetRuntimeId())] = (r.left, r.top, r.right, r.bottom)
                pending.extend((child, depth + 1) for child in control.children())
            except Exception:
                continue
        return anchors

    @staticmethod
    def anchors_moved(before, after, direction):
        offsets = []
        for runtime in before.keys() & after.keys():
            old, new = before[runtime], after[runtime]
            if old[0] == new[0] and old[2] == new[2] and old[3] - old[1] == new[3] - new[1]:
                dy = new[1] - old[1]
                if (dy < -3 if direction == "down" else dy > 3):
                    offsets.append(dy)
        # Multiple stable elements must translate coherently; one animation is not proof.
        return any(sum(abs(other - delta) <= 2 for other in offsets) >= 3 for delta in offsets)

    def focus(self, window, identity, check):
        check()
        if self.identity(window) != identity:
            raise WorkerError("TargetChanged")
        window.set_focus()
        self.guard(window, identity, check)

    def checked_editor(self, window, expected=None):
        editor = self.editor(window)
        runtime = tuple(editor.element_info.element.GetRuntimeId())
        if expected is not None and runtime != tuple(expected):
            raise WorkerError("EditorIdentityChanged")
        return editor

    def scroll_document(self, window, expected=None):
        if self.application != "Chrome":
            raise WorkerError("UnsupportedTarget")
        candidates = []
        for document in window.descendants(control_type="Document"):
            if not document.is_visible() or not document.is_enabled() or document.top_level_parent().handle != window.handle:
                continue
            password = document.element_info.element.GetCurrentPropertyValueEx(30019, True)
            if type(password) not in (bool, int) or password != 0:
                continue
            bounds = document.rectangle()
            area = bounds.width() * bounds.height()
            if bounds.width() >= 100 and bounds.height() >= 100:
                candidates.append((area, document))
        candidates.sort(key=lambda x: x[0], reverse=True)
        if not candidates or len(candidates) > 1 and candidates[0][0] == candidates[1][0]:
            raise WorkerError("ScrollRegionUnavailableOrAmbiguous")
        document = candidates[0][1]
        if expected is not None and tuple(document.element_info.element.GetRuntimeId()) != tuple(expected):
            raise WorkerError("ScrollRegionChanged")
        return document

    def scroll(self, window, identity, direction, amount, check, dispatched, expected=None):
        document = self.scroll_document(window, expected)
        runtime = tuple(document.element_info.element.GetRuntimeId())
        self.focus(window, identity, check)
        document = self.scroll_document(window, runtime)
        bounds = document.rectangle()
        x, y = (bounds.left + bounds.right) // 2, (bounds.top + bounds.bottom) // 2
        hit = self.desktop.from_point(x, y)
        if hit.top_level_parent().handle != window.handle:
            raise WorkerError("ScrollRegionOccluded")
        # Require the hit to be inside the pinned document subtree, not browser chrome.
        ancestor = hit
        for _ in range(32):
            if tuple(ancestor.element_info.element.GetRuntimeId()) == runtime:
                break
            ancestor = ancestor.parent()
            if ancestor is None:
                raise WorkerError("ScrollRegionOccluded")
        else:
            raise WorkerError("ScrollRegionOccluded")
        before = None
        anchors_before = self.scroll_anchors(document)
        viewport = document.rectangle()
        viewport_before = (viewport.left, viewport.top, viewport.right, viewport.bottom)
        try:
            before = document.iface_scroll.CurrentVerticalScrollPercent
        except Exception:
            pass
        self.guard(window, identity, check)
        # SendInput marks our pointer move injected. pywinauto.mouse.scroll uses
        # SetCursorPos first, which can look like user takeover to the real monitor.
        left, top = self.user.GetSystemMetrics(76), self.user.GetSystemMetrics(77)
        width, height = self.user.GetSystemMetrics(78), self.user.GetSystemMetrics(79)
        if width <= 1 or height <= 1 or not left <= x < left + width or not top <= y < top + height:
            raise WorkerError("ScrollRegionUnavailableOrAmbiguous")
        move = Input(0, MouseInput(round((x - left) * 65535 / (width - 1)), round((y - top) * 65535 / (height - 1)), 0, 0xC001, 0, 0))
        dispatched[0] = True
        if self.user.SendInput(1, ctypes.byref(move), ctypes.sizeof(Input)) != 1:
            raise WorkerError("InputDispatchFailed")
        self.guard(window, identity, check)
        cursor = w.POINT()
        if not self.user.GetCursorPos(ctypes.byref(cursor)) or abs(cursor.x - x) > 2 or abs(cursor.y - y) > 2:
            raise WorkerError("UserTakeover")
        if self.desktop.from_point(cursor.x, cursor.y).top_level_parent().handle != window.handle:
            raise WorkerError("ScrollRegionOccluded")
        wheel = Input(0, MouseInput(0, 0, (-amount if direction == "down" else amount) * 120, 0x0800, 0, 0))
        self.guard(window, identity, check)
        if self.user.SendInput(1, ctypes.byref(wheel), ctypes.sizeof(Input)) != 1:
            raise WorkerError("InputDispatchFailed")
        for _ in range(10):
            check()
            self.guard(window, identity, check)
            self.scroll_document(window, runtime)
            viewport = document.rectangle()
            if (viewport.left, viewport.top, viewport.right, viewport.bottom) == viewport_before and self.anchors_moved(anchors_before, self.scroll_anchors(document), direction):
                return True, {"evidence": "AccessibleContentTranslated", "direction": direction}
            try:
                after = document.iface_scroll.CurrentVerticalScrollPercent
                if before is not None and before >= 0 and after >= 0 and (after > before if direction == "down" else after < before):
                    return True, {"evidence": "ScrollPercentChanged"}
            except Exception:
                pass
            time.sleep(0.03)
        return False, {"evidence": "WheelDispatchedOnly", "limitation": "Movement could not be established; the page may have moved."}

    def press_shortcut(self, window, identity, key, check, dispatched, expected=None):
        from pywinauto.keyboard import send_keys
        keys = {"Ctrl+A": "^a", "Ctrl+C": "^c", "Ctrl+F": "^f", "Ctrl+S": "^s",
                "Escape": "{ESC}", "Enter": "{ENTER}", "Tab": "{TAB}", "Shift+Tab": "+{TAB}",
                "Left": "{LEFT}", "Right": "{RIGHT}", "Up": "{UP}", "Down": "{DOWN}"}
        if key not in keys:
            raise WorkerError("UnsupportedShortcut")
        if self.application == "Chrome":
            if key not in ("Ctrl+F", "Escape"):
                raise WorkerError("ChromeShortcutUnsupported")
            self.scroll_document(window, expected)
            self.focus(window, identity, check)
            self.scroll_document(window, expected)
            self.guard(window, identity, check)
            if key == "Escape":
                self.find_editor(window)
            dispatched[0] = True
            send_keys(keys[key], pause=0, turn_off_numlock=False)
            for _ in range(10):
                check()
                self.guard(window, identity, check)
                if key == "Escape":
                    try:
                        self.find_editor(window)
                    except WorkerError:
                        if not self.find_bar_present(window):
                            self._find_context = None
                            return True, {"evidence": "BrowserFindClosed"}
                    time.sleep(0.03)
                    continue
                try:
                    field = self.find_editor(window, focused_only=True)
                    self._find_context = (identity.copy(), tuple(field.element_info.element.GetRuntimeId()), time.monotonic())
                    return True, {"evidence": "BrowserFindEditorFocused"}
                except WorkerError:
                    time.sleep(0.03)
            return False, {"evidence": "KeysDispatchedOnly"}
        editor = self.checked_editor(window, expected)
        runtime = tuple(editor.element_info.element.GetRuntimeId())
        self.focus(window, identity, check)
        editor = self.checked_editor(window, runtime)
        editor.set_focus()
        document_runtime = self.selected_tab_runtime(window)
        self.guard(window, identity, check, document_runtime)
        if tuple(self.editor(window, focused_only=True).element_info.element.GetRuntimeId()) != runtime:
            raise WorkerError("EditorFocusChanged")
        clipboard_before = self.user.GetClipboardSequenceNumber()
        dispatched[0] = True
        send_keys(keys[key], pause=0, turn_off_numlock=False)
        # Bounded polling performs observation only; the shortcut is never repeated.
        for _ in range(10):
            check()
            self.guard(window, identity, check, document_runtime)
            if key == "Ctrl+C" and self.user.GetClipboardSequenceNumber() != clipboard_before:
                return True, {"evidence": "ClipboardSequenceChanged", "exact_content_verified": False}
            if key == "Ctrl+A":
                try:
                    selection = editor.iface_text.GetSelection()
                    if selection.Length == 1:
                        document = editor.iface_text.DocumentRange
                        selected = selection.GetElement(0)
                        if selected.CompareEndpoints(0, document, 0) == 0 and selected.CompareEndpoints(1, document, 1) == 0:
                            return True, {"evidence": "DocumentRangeSelected"}
                except Exception:
                    pass
            time.sleep(0.03)
        return False, {"evidence": "KeysDispatchedOnly"}

    def type_text(self, window, identity, text, check, dispatched, expected=None):
        from pywinauto.keyboard import send_keys
        from validation import literal_keys
        if self.application == "Chrome":
            context = getattr(self, "_find_context", None)
            if context is None or time.monotonic() - context[2] > 45 or any(context[0][k] != identity[k] for k in ("hwnd", "pid", "created", "title")):
                raise WorkerError("ChromeFindWorkflowNotEstablished")
            if tuple(self.find_editor(window).element_info.element.GetRuntimeId()) != context[1]:
                raise WorkerError("ChromeFindFocusChanged")
        # Validate approved editor before even changing foreground focus.
        self.checked_editor(window, expected)
        self.focus(window, identity, check)
        document_runtime = self.selected_tab_runtime(window) if self.application == "Notepad" else None
        editor = self.checked_editor(window, expected)
        editor.set_focus()
        original_runtime = tuple(editor.element_info.element.GetRuntimeId())
        if self.application == "Chrome":
            self._find_context = None  # One continuation only; no reuse after uncertain input.
            self.validate_pinned_find(window, editor, original_runtime)
            self.guard(window, identity, check, find_control=editor)
            dispatched[0] = True
            send_keys("^a", pause=0, turn_off_numlock=False)
        for character in text:
            self.guard(window, identity, check, document_runtime, editor if self.application == "Chrome" else None)
            if self.application == "Chrome":
                self.validate_pinned_find(window, editor, original_runtime)
            else:
                focused = self.editor(window, focused_only=True)
                if tuple(focused.element_info.element.GetRuntimeId()) != original_runtime:
                    raise WorkerError("EditorFocusChanged")
            dispatched[0] = True  # conservative even if the native call throws
            send_keys(literal_keys(character), with_spaces=True, pause=0, vk_packet=True, turn_off_numlock=False)
            time.sleep(0.01)
        self.guard(window, identity, check, document_runtime, editor if self.application == "Chrome" else None)
        if self.application == "Chrome":
            self.validate_pinned_find(window, editor, original_runtime)
            try:
                return editor.iface_value.CurrentValue == text
            except Exception:
                return False
        try:
            selections = editor.iface_text.GetSelection()
            if selections.Length != 1:
                return False
            selection = selections.GetElement(0).Clone()
            selection.MoveEndpointByRange(0, selection, 1)
            selection.MoveEndpointByUnit(0, 0, -len(text))
            return selection.GetText(len(text) + 1) == text
        except Exception:
            if self.application == "Chrome":
                try:
                    return editor.iface_value.CurrentValue == text
                except Exception:
                    pass
            return False

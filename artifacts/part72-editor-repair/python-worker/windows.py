"""Notepad-only milestone A backend. Identity is independently revalidated before input."""
import ctypes
from ctypes import wintypes as w
import os
import sys
import time
from validation import WorkerError


def health():
    try:
        import pywinauto
        return {"available": sys.platform == "win32", "python_version": sys.version.split()[0], "pywinauto_version": pywinauto.__version__}
    except ImportError:
        raise WorkerError("MissingPackage:pywinauto") from None


class NotepadWindows:
    def __init__(self):
        health()
        if sys.platform != "win32":
            raise WorkerError("WindowsRequired")
        from pywinauto import Desktop
        self.desktop = Desktop(backend="uia")
        self.user = ctypes.WinDLL("user32", use_last_error=True)
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.advapi = ctypes.WinDLL("advapi32", use_last_error=True)
        self.user.GetForegroundWindow.restype = w.HWND
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
            if exe != system and not (exe.startswith(packaged) and exe.endswith("\\notepad.exe")):
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
            return {"application": "Notepad", "hwnd": int(window.handle), "pid": pid, "created": created, "title": window.window_text()[:256]}
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

    def guard(self, window, identity, check, document_runtime=None):
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
        if int(self.user.GetForegroundWindow() or 0) != identity["hwnd"]:
            raise WorkerError("UserTakeover")
        popup = int(self.user.GetLastActivePopup(identity["hwnd"]) or 0)
        if popup not in (0, identity["hwnd"]):
            raise WorkerError("ModalDialogDenied")
        if any(self.user.GetAsyncKeyState(key) & 0x8000 for key in (16, 17, 18, 91, 92)):
            raise WorkerError("UserKeysHeld")

    def editor(self, window, focused_only=False):
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

    def type_text(self, window, identity, text, check, dispatched, expected=None):
        from pywinauto.keyboard import send_keys
        from validation import literal_keys
        # Validate approved editor before even changing foreground focus.
        self.checked_editor(window, expected)
        self.focus(window, identity, check)
        document_runtime = self.selected_tab_runtime(window)
        editor = self.checked_editor(window, expected)
        editor.set_focus()
        original_runtime = tuple(editor.element_info.element.GetRuntimeId())
        for character in text:
            self.guard(window, identity, check, document_runtime)
            focused = self.editor(window, focused_only=True)
            if tuple(focused.element_info.element.GetRuntimeId()) != original_runtime:
                raise WorkerError("EditorFocusChanged")
            dispatched[0] = True  # conservative even if the native call throws
            send_keys(literal_keys(character), with_spaces=True, pause=0, vk_packet=True, turn_off_numlock=False)
            time.sleep(0.01)
        self.guard(window, identity, check, document_runtime)
        try:
            selections = editor.iface_text.GetSelection()
            if selections.Length != 1:
                return False
            selection = selections.GetElement(0).Clone()
            selection.MoveEndpointByRange(0, selection, 1)
            selection.MoveEndpointByUnit(0, 0, -len(text))
            return selection.GetText(len(text) + 1) == text
        except Exception:
            return False

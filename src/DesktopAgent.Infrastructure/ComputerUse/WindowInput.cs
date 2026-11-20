using System.Runtime.InteropServices;
using System.Windows;
using DesktopAgent.Core.UiAwareness;

namespace DesktopAgent.Infrastructure.ComputerUse;

/// <summary>Internal only: there is deliberately no AI tool accepting arbitrary coordinates or virtual-key numbers.</summary>
internal static class WindowInput
{
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Union { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Key Key; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Key { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetLastActivePopup(nint hwnd);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber(); // Change counter only; never clipboard content.
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(nint token, int type, out int value, int length, out int needed);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    internal static bool IsElevated(int pid)
    {
        var p = OpenProcess(0x1000, false, pid); if (p == 0) return true;
        try { if (!OpenProcessToken(p, 8, out var t)) return true; try { return !GetTokenInformation(t, 20, out var value, 4, out _) || value != 0; } finally { CloseHandle(t); } }
        finally { CloseHandle(p); }
    }
    internal static bool HasOtherActivePopup(long hwnd) { var popup = GetLastActivePopup((nint)hwnd); return popup != 0 && popup != (nint)hwnd; }
    internal static bool TryFocus(long hwnd) => SetForegroundWindow((nint)hwnd) && GetForegroundWindow() == (nint)hwnd;
    private static void Send(Input input) { if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1) throw new UiObservationException("InputRejected"); }
    private static void NoHeldModifiers()
    {
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }) if ((GetAsyncKeyState(key) & 0x8000) != 0) throw new UiObservationException("UserKeysHeld");
    }
    internal static void Click(Rect bounds, long hwnd, Action guard)
    {
        NoHeldModifiers();
        if (bounds.IsEmpty || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || bounds.Width < 2 || bounds.Height < 2) throw new UiObservationException("InvalidBounds");
        var point = new Point { X = (int)(bounds.X + bounds.Width / 2), Y = (int)(bounds.Y + bounds.Height / 2) };
        guard(); if (GetAncestor(WindowFromPoint(point), 2) != (nint)hwnd) throw new UiObservationException("PointOccluded");
        var left = GetSystemMetrics(76); var top = GetSystemMetrics(77); var width = GetSystemMetrics(78); var height = GetSystemMetrics(79);
        if (width < 2 || height < 2 || point.X < left || point.Y < top || point.X >= left + width || point.Y >= top + height) throw new UiObservationException("InvalidBounds");
        Send(new() { Type = 0, Data = new() { Mouse = new() { X = (int)((point.X - left) * 65535L / (width - 1)), Y = (int)((point.Y - top) * 65535L / (height - 1)), Flags = 0xC001 } } });
        var down = false;
        try { guard(); if (GetAncestor(WindowFromPoint(point), 2) != (nint)hwnd) throw new UiObservationException("PointOccluded"); Send(new() { Type = 0, Data = new() { Mouse = new() { Flags = 2 } } }); down = true; }
        finally { if (down) Send(new() { Type = 0, Data = new() { Mouse = new() { Flags = 4 } } }); }
    }
    internal static void Wheel(Rect bounds, long hwnd, int direction, Action guard)
    {
        NoHeldModifiers();
        if (bounds.IsEmpty || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || bounds.Width < 2 || bounds.Height < 2) throw new UiObservationException("InvalidBounds");
        var point = new Point { X = (int)(bounds.X + bounds.Width / 2), Y = (int)(bounds.Y + bounds.Height / 2) };
        guard(); if (GetAncestor(WindowFromPoint(point), 2) != (nint)hwnd) throw new UiObservationException("PointOccluded");
        var left = GetSystemMetrics(76); var top = GetSystemMetrics(77); var width = GetSystemMetrics(78); var height = GetSystemMetrics(79);
        if (width < 2 || height < 2 || point.X < left || point.Y < top || point.X >= left + width || point.Y >= top + height) throw new UiObservationException("InvalidBounds");
        Send(new() { Type = 0, Data = new() { Mouse = new() { X = (int)((point.X - left) * 65535L / (width - 1)), Y = (int)((point.Y - top) * 65535L / (height - 1)), Flags = 0xC001 } } });
        guard(); if (GetAncestor(WindowFromPoint(point), 2) != (nint)hwnd) throw new UiObservationException("PointOccluded");
        Send(new() { Type = 0, Data = new() { Mouse = new() { Data = unchecked((uint)(direction < 0 ? 120 : -120)), Flags = 0x800 } } });
    }
    internal static void Type(string text, Action guard)
    {
        NoHeldModifiers();
        foreach (var ch in text)
        {
            guard(); var down = false;
            try { Send(new() { Type = 1, Data = new() { Key = new() { Scan = ch, Flags = 4 } } }); down = true; }
            finally { if (down) Send(new() { Type = 1, Data = new() { Key = new() { Scan = ch, Flags = 6 } } }); }
        }
    }
    internal static ushort[] ParseKeys(string key) => key switch
    {
        { Length: 1 } when char.IsAsciiDigit(key[0]) => [(ushort)key[0]],
        "+" => [0x6B], "-" => [0x6D], "*" => [0x6A], "/" => [0x6F], "." => [0x6E], "=" => [13],
        "Enter" => [13], "Escape" => [27], "Tab" => [9], "Shift+Tab" => [16, 9], "Alt+Tab" => [18, 9],
        "Ctrl+A" => [17, 65], "Ctrl+C" => [17, 67], "Ctrl+V" => [17, 86], "Ctrl+F" => [17, 70], "Ctrl+S" => [17, 83],
        "Left" => [37], "Up" => [38], "Right" => [39], "Down" => [40], "PageUp" => [33], "PageDown" => [34],
        _ => throw new ArgumentException("Unsupported key or shortcut")
    };
    internal static void Keys(string key, Action guard)
    {
        // Pasting clipboard contents is not supported: the service cannot establish their sensitivity or provenance.
        if (key == "Ctrl+V") throw new UiObservationException("ClipboardPasteUnavailable");
        var keys = ParseKeys(key); NoHeldModifiers(); var held = new List<ushort>();
        try { foreach (var vk in keys) { guard(); Send(new() { Type = 1, Data = new() { Key = new() { Vk = vk } } }); held.Add(vk); } }
        finally { foreach (var vk in held.AsEnumerable().Reverse()) Send(new() { Type = 1, Data = new() { Key = new() { Vk = vk, Flags = 2 } } }); }
    }
}

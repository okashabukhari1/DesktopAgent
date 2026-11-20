using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DesktopAgent.Infrastructure.Developer;

public static class DeveloperPipeProtocol
{
    public const int MaxBytes = 262144;
    public static string PipeName => "desktop-agent-dev-v1-" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("User unavailable"))))[..24] + "-" + Process.GetCurrentProcess().SessionId;
    public static bool PeerInCurrentSession(PipeStream pipe, bool server)
    {
        try
        {
            uint pid;
            if (!(server ? GetNamedPipeClientProcessId(pipe.SafePipeHandle, out pid) : GetNamedPipeServerProcessId(pipe.SafePipeHandle, out pid))) return false;
            using var peer = Process.GetProcessById(checked((int)pid));
            using var current = Process.GetCurrentProcess();
            return peer.SessionId == current.SessionId;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException) { return false; }
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    public static async Task<byte[]> ReadAsync(Stream stream, int max, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, ct);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size <= 0 || size > max) throw new InvalidDataException("Invalid developer message size");
        var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, ct); return bytes;
    }
    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        if (bytes.Length is 0 or > MaxBytes) throw new InvalidDataException("Invalid developer message size");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
}

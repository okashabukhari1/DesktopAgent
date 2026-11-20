using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DesktopAgent.Core.Ai;

namespace DesktopAgent.Infrastructure.Ai;

public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint GenericCredential = 1;
    private const uint LocalMachinePersistence = 2;
    private const int NotFound = 1168;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public bool Exists(string name)
    {
        EnsureWindows();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!CredRead(name, GenericCredential, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == NotFound) return false;
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Credential status unavailable.");
        }
        try { return Marshal.PtrToStructure<NativeCredential>(pointer).CredentialBlobSize > 0; }
        finally { CredFree(pointer); }
    }

    public void Save(string name, string secret)
    {
        EnsureWindows();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var blobBytes = Encoding.Unicode.GetByteCount(secret);
        if (blobBytes > 5120) throw new ArgumentOutOfRangeException(nameof(secret), "Credential is too large for Windows Credential Manager.");
        var blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = name,
                Comment = "Desktop Agent AI provider credential",
                CredentialBlobSize = (uint)blobBytes,
                CredentialBlob = blob,
                Persist = LocalMachinePersistence,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not save the provider key.");
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }

    public string? Read(string name)
    {
        EnsureWindows();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!CredRead(name, GenericCredential, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error, "Windows Credential Manager could not read the provider key.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            return credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0
                ? null : Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / sizeof(char)));
        }
        finally { CredFree(pointer); }
    }

    public void Delete(string name)
    {
        EnsureWindows();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (CredDelete(name, GenericCredential, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != NotFound) throw new Win32Exception(error, "Windows Credential Manager could not remove the provider key.");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Credential Manager is available only on Windows.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string targetName, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);
}

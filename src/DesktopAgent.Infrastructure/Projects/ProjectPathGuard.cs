using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using DesktopAgent.Core.Projects;

namespace DesktopAgent.Infrastructure.Projects;

/// <summary>Windows directory handles pin ancestors (no FILE_SHARE_DELETE); final file
/// handles are checked before any bytes are read. Reparse points/ADS/device paths denied.</summary>
internal sealed class ProjectPathGuard : IDisposable
{
    private readonly List<SafeFileHandle> _directories = [];
    public string Root { get; }
    public string FullPath { get; }
    public ProjectPathGuard(string root, string requested, bool directory, Func<string, bool> excluded, ISecretProtection secrets)
    {
        try
        {
            Root = ProjectDiscoveryService.ValidatePath(root);
            RejectPrivateRoot(Root);
            if (excluded(Path.GetFileName(Root)) || secrets.IsSensitiveName(Path.GetFileName(Root)))
                throw new ProjectFileException("ProtectedPath", "This private, sensitive, or generated project root is excluded from file intelligence.");
            if (!Directory.Exists(Root)) throw new ProjectFileException("ProjectUnavailable", "The project folder is unavailable. Locate it on Projects.");
            if (requested.Length > 1024 || requested.Any(char.IsControl) || requested.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0
                || requested.StartsWith("\\\\") || requested.StartsWith("//")) Deny();
            var relative = requested.Replace('/', '\\');
            if (relative.Split('\\').Any(s => s == ".." || s.EndsWith(' ') || (s.EndsWith('.') && s != "."))) Deny();
            if (Path.IsPathRooted(relative) && !Path.IsPathFullyQualified(relative)) Deny();
            FullPath = Path.GetFullPath(Path.IsPathFullyQualified(relative) ? relative : Path.Combine(Root, relative));
            if (FullPath.AsSpan(2).Contains(':') || !(FullPath.Equals(Root, StringComparison.OrdinalIgnoreCase) || FullPath.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) Deny();
            var parts = Path.GetRelativePath(Root, FullPath).Split(Path.DirectorySeparatorChar);
            foreach (var part in parts.Where(p => p != "."))
                if (excluded(part) || secrets.IsSensitiveName(part)) throw new ProjectFileException("ProtectedPath", "This private, sensitive, or generated path is excluded. It will not be sent to AI.");
            var targetDirectory = directory ? FullPath : Path.GetDirectoryName(FullPath)!;
            var ancestors = new Stack<string>();
            for (var info = new DirectoryInfo(targetDirectory); info is not null; info = info.Parent) ancestors.Push(info.FullName);
            while (ancestors.TryPop(out var path))
            {
                var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero); // BACKUP_SEMANTICS | OPEN_REPARSE_POINT, no delete sharing
                if (handle.IsInvalid) { handle.Dispose(); throw Error(); }
                _directories.Add(handle);
                if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0 || !SamePath(FinalPath(handle), path)) Deny();
            }
        }
        catch { Dispose(); throw; }
    }
    public FileStream OpenRead()
    {
        SafeFileHandle? handle = null;
        try
        {
            handle = File.OpenHandle(FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);
            if (!GetFileInformationByHandle(handle, out var info)) throw Error();
            if ((info.Attributes & 0x400) != 0 || info.Links > 1 || !SamePath(FinalPath(handle), FullPath)) Deny();
            var stream = new FileStream(handle, FileAccess.Read, 4096, true); handle = null; return stream;
        }
        finally { handle?.Dispose(); }
    }
    public void Dispose() { foreach (var handle in _directories.AsEnumerable().Reverse()) handle.Dispose(); _directories.Clear(); }
    private static bool SamePath(string a, string b) => Path.TrimEndingDirectorySeparator(a).Equals(Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
    private static void Deny() => throw new ProjectFileException("OutsideProject", "The path is outside the authorized physical project boundary or uses an unsafe link/path form.");
    private static ProjectFileException Error() => new("Unavailable", "The file or directory is missing, locked, or access is denied.");
    private static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(32768); var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw Error();
        var result = buffer.ToString(); if (result.StartsWith("\\\\?\\")) result = result[4..]; return result;
    }
    private static void RejectPrivateRoot(string root)
    {
        if (!OperatingSystem.IsWindows()) throw new ProjectFileException("UnsupportedPlatform", "Project file access requires Windows.");
        if (root.StartsWith("\\\\")) throw new ProjectFileException("UnsupportedPath", "Network/device project roots are not supported for source retrieval.");
        var containers = new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        if (containers.Any(p => !string.IsNullOrEmpty(p) && SamePath(p, root))) Deny();
        var privateRoots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
        if (privateRoots.Any(p => !string.IsNullOrEmpty(p) && (SamePath(p, root) || root.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))) Deny();
        if (root.Split(Path.DirectorySeparatorChar).Any(p => new[] { ".ssh", ".aws", ".codex", "browser profiles" }.Contains(p, StringComparer.OrdinalIgnoreCase))) Deny();
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfoNative
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
}

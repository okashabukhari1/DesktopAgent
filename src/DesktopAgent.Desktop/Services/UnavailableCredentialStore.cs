using DesktopAgent.Core.Ai;

namespace DesktopAgent.Desktop.Services;

public sealed class UnavailableCredentialStore : ICredentialStore
{
    public bool IsAvailable => false;
    public bool Exists(string name) => false;
    public void Save(string name, string secret) => throw Unavailable();
    public string? Read(string name) => throw Unavailable();
    public void Delete(string name) => throw Unavailable();
    private static PlatformNotSupportedException Unavailable() =>
        new("Linux credential storage is not connected in this foundation. No credential operation was performed.");
}

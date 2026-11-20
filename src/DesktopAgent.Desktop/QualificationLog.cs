using System.Text.Json;

namespace DesktopAgent.Desktop;

internal static class QualificationLog
{
    public static void Write(string name, object? data = null) =>
        Console.WriteLine(JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, name, data }));
}

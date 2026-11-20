namespace DesktopAgent.Desktop.Services;

public static class StartupFailure
{
    public static void Report(Exception exception, TextWriter output)
    {
        var category = new string(exception.GetType().Name.Where(c => char.IsLetterOrDigit(c) || c == '_').Take(100).ToArray());
        output.WriteLine($"Desktop Agent could not start ({category}). No exception payload was recorded.");
    }
}

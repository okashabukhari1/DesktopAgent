using System.Diagnostics;
using System.Text.Json;
using DesktopAgent.Infrastructure.ComputerUse;

internal static class Part72Diagnostics
{
    public static async Task<int> Run(string root, bool desktopInput)
    {
        root = Path.GetFullPath(root);
        var python = PythonWorkerClient.DiscoverPython();
        if (python is null) { Console.WriteLine("FAIL MissingPython: configure a full Python executable path."); return 1; }
        var worker = Path.Combine(root, "src", "DesktopAgent.PythonWorker", "worker.py");
        await using var client = new PythonWorkerClient(python, worker, desktopInput);
        var health = await client.SendAsync(PythonWorkerAction.Health);
        Console.WriteLine(JsonSerializer.Serialize(health));
        if (!health.Verified) return 1;
        if (!desktopInput)
        {
            var health2 = await client.SendAsync(PythonWorkerAction.Health);
            if (!health2.Verified || health.Id == health2.Id) throw new InvalidOperationException("PersistentWorkerCorrelation");
            var invalid = await client.SendAsync((PythonWorkerAction)999);
            if (invalid.Error != "InvalidArguments" || invalid.InputDispatched) throw new InvalidOperationException("ValidationFailure");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var cancellation = await client.SendAsync(PythonWorkerAction.Health, ct: cancelled.Token);
            if (cancellation.Status != "Cancelled" || cancellation.InputDispatched) throw new InvalidOperationException("CancellationFailure");
            await using var missing = new PythonWorkerClient(Path.Combine(root, "missing-python-for-test.exe"), worker);
            if ((await missing.SendAsync(PythonWorkerAction.Health)).Error != "MissingPython") throw new InvalidOperationException("MissingPythonFailure");
            foreach (var (fixture, expected) in new[] { ("crash.py", "Failed"), ("hang.py", "TimedOut"), ("wrong_id.py", "Failed") })
            {
                await using var broken = new PythonWorkerClient(python, Path.Combine(root, "tests", "DesktopAgent.Tests", "PythonFixtures", fixture));
                var result = await broken.SendAsync(PythonWorkerAction.Health, timeoutMs: 100);
                if (result.Status != expected || result.Verified || result.InputDispatched)
                    throw new InvalidOperationException("WorkerFixtureFailure:" + fixture);
                Console.WriteLine("PASS worker transport " + fixture);
            }
            Console.WriteLine("PASS C# child-worker health, sequential request IDs, invalid arguments, pre-cancellation, missing Python, crash, timeout, wrong response ID. No desktop input.");
            return 0;
        }
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            var folder = Path.Combine(root, "artifacts", "part72-smoke");
            Directory.CreateDirectory(folder);
            var document = Path.Combine(folder, "agent-csharp-smoke-" + Guid.NewGuid().ToString("N") + ".txt");
            using (File.Create(document)) { }
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe")) { UseShellExecute = false };
            start.ArgumentList.Add(document);
            using var launch = Process.Start(start);
            PythonWorkerResult? found = null;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                found = await client.SendAsync(PythonWorkerAction.FindWindow,
                    new(Title: Path.GetFileName(document) + " - Notepad"), ct: stop.Token);
                if (found.Error == "TargetNotFound")
                    found = await client.SendAsync(PythonWorkerAction.FindWindow,
                        new(Title: Path.GetFileNameWithoutExtension(document) + " - Notepad"), ct: stop.Token);
                if (found.Verified || found.Error != "TargetNotFound") break;
                await Task.Delay(250, stop.Token);
            }
            if (found is not { Verified: true, Data: not null })
            { Console.WriteLine(JsonSerializer.Serialize(found)); return 1; }
            var identity = found.Data.Value;
            var target = new PythonWindowTarget(identity.GetProperty("application").GetString()!,
                identity.GetProperty("hwnd").GetInt64(), identity.GetProperty("pid").GetInt32(),
                identity.GetProperty("created").GetInt64(), identity.GetProperty("title").GetString());
            Console.WriteLine("C# pinned disposable Notepad. Leave input untouched; Ctrl+C cancels.");
            var result = await client.SendAsync(PythonWorkerAction.TypeText, target, "Hello World", ct: stop.Token);
            Console.WriteLine(JsonSerializer.Serialize(result));
            Console.WriteLine(result.Verified ? "PASS fixed C# → Python text request verified." : "FAIL fixed C# request: do not retry automatically.");
            return result.Verified ? 0 : 1;
        }
        catch (OperationCanceledException) { Console.WriteLine("Cancelled; no further requests."); return 1; }
        finally { Console.CancelKeyPress -= handler; }
    }
}

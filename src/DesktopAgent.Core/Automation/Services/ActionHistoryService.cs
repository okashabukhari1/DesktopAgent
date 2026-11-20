using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.Automation.Services;

public sealed class ActionHistoryService : IActionHistoryService
{
    private const int Capacity = 200;
    private readonly object _sync = new();
    private readonly List<ActionHistoryEntry> _entries = [];
    public event EventHandler<ActionHistoryEntry>? EntryAdded;

    public IReadOnlyList<ActionHistoryEntry> GetRecent(int count = 50)
    {
        lock (_sync) return _entries.TakeLast(Math.Clamp(count, 0, Capacity)).Reverse().ToArray();
    }

    public void Add(ActionHistoryEntry entry)
    {
        lock (_sync)
        {
            _entries.Add(entry);
            if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
        }
        EntryAdded?.Invoke(this, entry);
    }
}

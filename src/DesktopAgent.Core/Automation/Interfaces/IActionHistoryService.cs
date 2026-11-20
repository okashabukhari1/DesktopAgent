using DesktopAgent.Core.Automation.Models;

namespace DesktopAgent.Core.Automation.Interfaces;

public interface IActionHistoryService
{
    event EventHandler<ActionHistoryEntry>? EntryAdded;
    IReadOnlyList<ActionHistoryEntry> GetRecent(int count = 50);
    void Add(ActionHistoryEntry entry);
}

namespace DesktopAgent.Core.UiAwareness;

public static class UiAlgorithms
{
    public static IEnumerable<UiElement> Flatten(UiElement root)
    {
        var stack = new Stack<UiElement>(); stack.Push(root);
        while (stack.TryPop(out var item)) { yield return item; foreach (var child in item.Children.Reverse()) stack.Push(child); }
    }
    public static bool MatchesFilter(UiElement e, UiInspectionOptions o) =>
        (!o.VisibleOnly || e.IsVisible == true) && (!o.EnabledOnly || e.IsEnabled == true) && (!o.InteractiveOnly || e.IsInteractive);
    public static UiElement Protect(UiElement e) => e.IsPassword == true
        ? e with { Name = "Password field", Value = "[PROTECTED]", AutomationId = null, ClassName = null, Children = [] }
        : e with { Children = e.Children.Select(Protect).ToArray() };
    public static UiElement LimitTree(UiElement root, UiInspectionOptions options, CancellationToken ct = default)
    {
        var o = options.Bounded(); var remaining = o.MaximumElements;
        UiElement Visit(UiElement e, int depth)
        {
            ct.ThrowIfCancellationRequested(); remaining--;
            var children = new List<UiElement>();
            if (depth < o.MaximumDepth && e.IsPassword != true)
                foreach (var child in e.Children) { ct.ThrowIfCancellationRequested(); if (remaining == 0) break; children.Add(Visit(child, depth + 1)); }
            return Protect(e with { Children = children });
        }
        return Visit(root, 0);
    }
    public static UiSearchResult Search(UiSnapshot snapshot, UiElementQuery q)
    {
        if (new[] { q.Name, q.ControlType, q.AutomationId, q.ClassName, q.Value }.All(string.IsNullOrWhiteSpace))
            return new("InvalidQuery", [], snapshot.Truncated);
        static bool Has(string? actual, string? expected) => string.IsNullOrWhiteSpace(expected) || actual?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;
        var scored = snapshot.Elements.Where(e => e.IsPassword != true && Has(e.Name, q.Name) && Has(e.ControlType, q.ControlType) &&
            Has(e.AutomationId, q.AutomationId) && Has(e.ClassName, q.ClassName) && Has(e.Value, q.Value))
            .Select(e => new { Element = e, Score = (q.Name is not null && string.Equals(e.Name, q.Name, StringComparison.OrdinalIgnoreCase) ? 1000 : 0) +
                (e.IsVisible == true ? 40 : 0) + (e.IsEnabled == true ? 20 : 0) + (snapshot.Window.IsForeground ? 10 : 0) +
                (q.ControlType is not null && e.ControlType?.Equals(q.ControlType, StringComparison.OrdinalIgnoreCase) == true ? 8 : 0) +
                (q.AutomationId is not null && e.AutomationId?.Equals(q.AutomationId, StringComparison.OrdinalIgnoreCase) == true ? 8 : 0) +
                (e.IsFocused == true || e.ParentId == snapshot.FocusedElement?.ParentId && e.ParentId is not null ? 2 : 0) })
            .OrderByDescending(e => e.Score).Take(12).ToArray();
        // Rank for presentation, never automatically resolve multiple matches.
        return new(scored.Length == 0 ? "NotFound" : scored.Length == 1 && !snapshot.Truncated ? "Found" : "Ambiguous", scored.Select(e => e.Element).ToArray(), snapshot.Truncated);
    }
    public static bool IsFresh(UiSnapshot snapshot, DateTimeOffset now) => now >= snapshot.Timestamp && now - snapshot.Timestamp <= TimeSpan.FromSeconds(2);
}

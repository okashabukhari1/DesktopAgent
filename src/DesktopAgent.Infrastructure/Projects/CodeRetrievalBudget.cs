using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Projects;

namespace DesktopAgent.Infrastructure.Projects;

// Per human turn; applied before UI, conversation context, serialization, or cloud transmission.
public sealed class CodeRetrievalBudget(FileIntelligenceSettings settings)
{
    private readonly HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
    private int characters;
    private bool Reserve(Guid id, string path, int length)
    {
        var key = id + ":" + path;
        if ((!files.Contains(key) && files.Count >= settings.MaxFilesPerTurn) || characters + length > settings.MaxTurnCharacters) return false;
        files.Add(key); characters += length; return true;
    }
    public ToolResult Apply(ToolResult result)
    {
        if (!result.Success) return result;
        if (result.Data is ProjectTree tree)
        {
            var kept = new List<ProjectFileEntry>();
            foreach (var entry in tree.Entries)
            { var length = entry.RelativePath.Length + 80; if (characters + length > settings.MaxTurnCharacters) break; characters += length; kept.Add(entry); }
            return result with { Data = tree with { Entries = kept, Truncated = tree.Truncated || kept.Count != tree.Entries.Count },
                Status = kept.Count == 0 && tree.Entries.Count > 0 ? "RetrievalBudget" : result.Status,
                Message = kept.Count != tree.Entries.Count ? "Project metadata limited by the per-turn retrieval budget." : result.Message };
        }
        if (result.Data is ProjectFileEntry metadata)
        {
            var length = metadata.RelativePath.Length + 120;
            if (characters + length > settings.MaxTurnCharacters) return result with { Data = null, Sources = null, Status = "RetrievalBudget", Message = "File context budget reached. Narrow the next request." };
            characters += length;
        }
        if (result.Data is ProjectFileRead read)
        {
            var kept = new List<CodeLine>();
            foreach (var line in read.Lines) { if (!Reserve(read.ProjectId, read.RelativePath, line.Text.Length + 16)) break; kept.Add(line); }
            var limited = kept.Count != read.Lines.Count;
            return result with { Data = read with { Lines = kept, Truncated = read.Truncated || limited },
                Message = limited ? "Retrieval budget reached; only the included lines are available." : result.Message,
                Sources = kept.Count > 0 ? [new(read.ProjectId, read.RelativePath, kept[0].Number, kept[^1].Number)] : [], Status = limited && kept.Count == 0 ? "RetrievalBudget" : result.Status };
        }
        if (result.Data is ProjectSearchResult search)
        {
            var kept = search.Results.Where(r => Reserve(r.ProjectId, r.RelativePath, r.MatchedText.Length + r.ContextBefore.Sum(l => l.Text.Length) + r.ContextAfter.Sum(l => l.Text.Length) + 64)).ToArray();
            return result with { Data = search with { Results = kept, Truncated = search.Truncated || kept.Length != search.Results.Count },
                Sources = kept.Select(r => new CodeSourceReference(r.ProjectId, r.RelativePath, r.LineNumber, r.LineNumber)).Distinct().ToArray(),
                Message = kept.Length != search.Results.Count ? "Search results limited by the per-turn retrieval budget." : result.Message };
        }
        if (result.Data is ProjectDependencyReport deps)
        {
            var kept = deps.Items.Where(d => Reserve(result.Sources?.FirstOrDefault(s => s.RelativePath.Equals(d.Manifest, StringComparison.OrdinalIgnoreCase))?.ProjectId ?? Guid.Empty,
                d.Manifest, d.Name.Length + (d.Version?.Length ?? 0) + 32)).ToArray();
            return result with { Data = deps with { Items = kept, Truncated = deps.Truncated || kept.Length != deps.Items.Count },
                Sources = result.Sources?.Where(s => kept.Any(d => d.Manifest.Equals(s.RelativePath, StringComparison.OrdinalIgnoreCase))).ToArray() };
        }
        return result;
    }
}

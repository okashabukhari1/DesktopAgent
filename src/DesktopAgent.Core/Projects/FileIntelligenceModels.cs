namespace DesktopAgent.Core.Projects;

public sealed class FileIntelligenceSettings
{
    public int MaxSearchResults { get; set; } = 12;
    public int MaxFilesPerTurn { get; set; } = 4;
    public int MaxLinesPerRead { get; set; } = 120;
    public int MaxReadCharacters { get; set; } = 12000;
    public int MaxTurnCharacters { get; set; } = 24000;
    public int MaxFileBytes { get; set; } = 2097152;
    public int MaxSearchBytes { get; set; } = 16777216;
    public int MaxTreeEntries { get; set; } = 4000;
    public List<string> AdditionalExcludedDirectories { get; set; } = [];
    public FileIntelligenceSettings Copy() { var copy = (FileIntelligenceSettings)MemberwiseClone(); copy.AdditionalExcludedDirectories = [.. AdditionalExcludedDirectories]; return copy; }
    public void Sanitize()
    {
        MaxSearchResults = Math.Clamp(MaxSearchResults, 1, 30); MaxFilesPerTurn = Math.Clamp(MaxFilesPerTurn, 1, 8);
        MaxLinesPerRead = Math.Clamp(MaxLinesPerRead, 1, 200); MaxReadCharacters = Math.Clamp(MaxReadCharacters, 1000, 12000);
        MaxTurnCharacters = Math.Clamp(MaxTurnCharacters, 2000, 48000); MaxFileBytes = Math.Clamp(MaxFileBytes, 16384, 4194304);
        MaxSearchBytes = Math.Clamp(MaxSearchBytes, MaxFileBytes, 67108864); MaxTreeEntries = Math.Clamp(MaxTreeEntries, 100, 10000);
        AdditionalExcludedDirectories ??= [];
        AdditionalExcludedDirectories = AdditionalExcludedDirectories.Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 80 && x.IndexOfAny(['/', '\\', ':']) < 0 && !x.Any(char.IsControl)).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
    }
}
public sealed record ProjectFileEntry(string RelativePath, bool IsDirectory, long? SizeBytes, DateTimeOffset? ModifiedAt, bool IsBinary = false);
public sealed record ProjectTree(Guid ProjectId, string ProjectName, IReadOnlyList<ProjectFileEntry> Entries, bool Truncated, int Skipped, string? Warning = null);
public sealed record CodeLine(int Number, string Text);
public sealed record CodeSourceReference(Guid ProjectId, string RelativePath, int StartLine = 1, int EndLine = 1);
public sealed record ProjectFileRead(Guid ProjectId, string RelativePath, IReadOnlyList<CodeLine> Lines,
    long SizeBytes, bool Redacted, bool Truncated, string? Warning = null);
public sealed record CodeSearchResult(Guid ProjectId, string RelativePath, int LineNumber, int Column,
    string MatchedText, IReadOnlyList<CodeLine> ContextBefore, IReadOnlyList<CodeLine> ContextAfter, int Score,
    string? SymbolKind = null, string? SymbolName = null);
public sealed record ProjectSearchResult(IReadOnlyList<CodeSearchResult> Results, bool Truncated, int FilesExamined, int Skipped, string? Warning = null);
public sealed record ProjectDependency(string Manifest, string Name, string? Version);
public sealed record ProjectDependencyReport(IReadOnlyList<ProjectDependency> Items, int ManifestsRead, int Skipped, bool Truncated);
public sealed class ProjectFileException(string code, string message) : Exception(message)
{ public string Code { get; } = code; }
public interface ISecretProtection
{
    string Redact(string content, out bool changed);
    bool IsSensitiveName(string name);
}
public interface IProjectFileService
{
    Task<ProjectTree> GetProjectTreeAsync(Guid projectId, bool refresh = false, CancellationToken cancellationToken = default);
    Task<ProjectTree> ListDirectoryAsync(Guid projectId, string relativePath, CancellationToken cancellationToken = default);
    Task<ProjectFileEntry> GetFileMetadataAsync(Guid projectId, string relativePath, CancellationToken cancellationToken = default);
    Task<ProjectFileRead> ReadFileAsync(Guid projectId, string relativePath, int? startLine = null, int? endLine = null, CancellationToken cancellationToken = default);
    string ResolveFilePath(Guid projectId, string relativePath);
}
public interface IProjectSearchService
{
    Task<ProjectSearchResult> SearchFileNamesAsync(Guid projectId, string query, string? extension = null, string? directory = null, CancellationToken cancellationToken = default);
    Task<ProjectSearchResult> SearchTextAsync(Guid projectId, string query, string? extension = null, string? directory = null, CancellationToken cancellationToken = default);
    Task<ProjectSearchResult> SearchSymbolsAsync(Guid projectId, string query, CancellationToken cancellationToken = default);
    Task<ProjectDependencyReport> GetDependenciesAsync(Guid projectId, CancellationToken cancellationToken = default);
}
public interface IProjectFileOpenService
{
    Task<DesktopAgent.Core.Ai.ToolResult> OpenAsync(Guid projectId, string relativePath, int line = 1, CancellationToken cancellationToken = default);
}

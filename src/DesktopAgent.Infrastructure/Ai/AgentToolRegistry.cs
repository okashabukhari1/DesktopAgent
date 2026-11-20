using DesktopAgent.Core.Ai;

namespace DesktopAgent.Infrastructure.Ai;

public sealed class AgentToolRegistry(IEnumerable<IAgentTool> tools)
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools =
        tools.ToDictionary(tool => tool.Definition.Name, StringComparer.Ordinal);
    public IReadOnlyList<AiToolDefinition> Definitions => _tools.Values.Select(tool => tool.Definition).ToArray();
    public IAgentTool? Find(string name) => _tools.GetValueOrDefault(name);
}

public sealed class AiConnectionService : IAiConnectionService
{
    public event EventHandler? Changed;
    public AiConnectionStatus Status { get; private set; } = new(AiConnectionState.NotTested, "Connection not tested.");
    public void Set(AiConnectionStatus status) { Status = status; Changed?.Invoke(this, EventArgs.Empty); }
}

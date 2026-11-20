namespace DesktopAgent.Core.Permissions;

public interface IPolicyService
{
    IReadOnlyList<AgentCapability> Capabilities { get; }
    PermissionPolicy GetPolicy(string capabilityId);
    PolicyEvaluation Evaluate(string capabilityId);
    Task SetPolicyAsync(string capabilityId, PermissionPolicy policy, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public enum PermissionApproval
{
    Deny,
    AllowOnce,
    AlwaysAllow
}

public interface IAgentActionApprovalService
{
    Task<PermissionApproval> RequestAsync(AgentCapability capability, string target, CancellationToken cancellationToken = default);
}

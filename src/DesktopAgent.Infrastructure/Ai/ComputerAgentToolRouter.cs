using System.Diagnostics;
using System.Text.Json;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Permissions;
using Microsoft.Extensions.Logging;
using DesktopAgent.Core.Workspaces.Interfaces;

namespace DesktopAgent.Infrastructure.Ai;

public sealed class ComputerAgentToolRouter(AgentToolRegistry registry, IPolicyService policy,
    IAgentActionApprovalService approval, IActionHistoryService activity, ICharacterService character,
    ILogger<ComputerAgentToolRouter> logger, IWorkspaceService? workspaces = null,
    DesktopAgent.Infrastructure.ComputerUse.ComputerUseSession? computerUse = null) : IAgentToolRouter
{
    public IReadOnlyList<AiToolDefinition> Definitions => registry.Definitions;
    public async Task<ToolResult> InvokeAsync(AiToolCall call, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var tool = registry.Find(call.Name);
        if (tool is null) return Finish(new(false, "UnknownTool", "That action is unavailable.", ErrorCode: "UnknownTool"), call, watch);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (call.ArgumentsJson.Length > 8192) throw new InvalidDataException("Arguments too large.");
            using var doc = JsonDocument.Parse(call.ArgumentsJson, new JsonDocumentOptions { MaxDepth = 8 });
            try { tool.Validate(doc.RootElement); }
            catch (ArgumentException ex)
            {
                // Local schema diagnostics only: expected property names/types, never argument values or unknown names.
                var shape = tool.Definition.Parameters.TryGetProperty("properties", out var expected)
                    ? string.Join(",", expected.EnumerateObject().Select(p => p.Name + "=" +
                        (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(p.Name, out var v) ? v.ValueKind.ToString() : "Missing"))) : "Unknown schema";
                var received = doc.RootElement.ValueKind == JsonValueKind.Object ? string.Join(",", doc.RootElement.EnumerateObject().Take(16).Select(p =>
                    (System.Text.RegularExpressions.Regex.IsMatch(p.Name, @"^[a-zA-Z_][a-zA-Z_0-9]{0,63}$") ? p.Name : "[non-schema-name]") + "=" + p.Value.ValueKind)) : doc.RootElement.ValueKind.ToString();
                var expectedShape = tool.Definition.Parameters.TryGetProperty("properties", out var properties) ? string.Join(",", properties.EnumerateObject().Select(p =>
                    p.Name + "=" + (p.Value.TryGetProperty("type", out var types) ? types.GetRawText() : "unspecified"))) : "Unknown";
                var reason = DesktopAgent.Infrastructure.UiAwareness.UiTools.IsUiTool(call.Name) && ex.Message.StartsWith("UI.", StringComparison.Ordinal) ||
                    DesktopAgent.Infrastructure.ComputerUse.ComputerActionTools.IsAction(call.Name) && ex.Message.StartsWith("Action.", StringComparison.Ordinal)
                    ? ex.Message : "ArgumentValidationRejected";
                logger.LogWarning("Tool schema mismatch Tool={Tool} Received={Received} Expected={Expected} Shape={Shape} Validation=Rejected Reason={Reason}", call.Name, received, expectedShape, shape, reason);
                throw;
            }
            if (DesktopAgent.Infrastructure.UiAwareness.UiTools.IsUiTool(call.Name))
                logger.LogInformation("UI tool contract Tool={Tool} Shape={Shape} Validation=Accepted", call.Name,
                    string.Join(",", doc.RootElement.EnumerateObject().Select(p => p.Name + "=" + p.Value.ValueKind)));
            var target = tool.Target(doc.RootElement);
            string? workspaceStamp = null;
            if (call.Name == "launch_workspace" && workspaces is not null)
            {
                await workspaces.InitializeAsync(cancellationToken);
                workspaceStamp = JsonSerializer.Serialize(workspaces.Find(target ?? ""));
            }
            if (DesktopAgent.Infrastructure.UiAwareness.UiTools.IsUiTool(call.Name) &&
                policy.Evaluate(CapabilityIds.ScreenInspectUi).Outcome is PolicyOutcome.Denied or PolicyOutcome.Unavailable)
                return Finish(new(false, "PermissionDenied", "Read-only UI observation is blocked in Permissions.", ErrorCode: CapabilityIds.ScreenInspectUi), call, watch);
            var preparation = await tool.PrepareAsync(doc.RootElement, cancellationToken);
            var arguments = preparation.PreparedArguments ?? doc.RootElement;
            tool.Validate(arguments);
            foreach (var id in preparation.Capabilities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var evaluation = policy.Evaluate(id);
                Record("Policy", id, evaluation.Outcome.ToString(), target);
                if (evaluation.Outcome is PolicyOutcome.Denied or PolicyOutcome.Unavailable)
                    return Finish(new(false, "PermissionDenied", "This action is blocked in Permissions.", ErrorCode: id, Target: target), call, watch);
                if (evaluation.Outcome == PolicyOutcome.ConfirmationRequired)
                {
                    character.SetState(CharacterState.Attention);
                    if (computerUse?.IsActive == true) computerUse.Update(DesktopAgent.Core.ComputerUse.ComputerTaskState.WaitingForUser, "Waiting for the requested tool's permission");
                    var capability = AgentCapabilities.Find(id)!;
                    var decision = await approval.RequestAsync(capability, target ?? tool.Definition.Name, cancellationToken);
                    if (computerUse?.IsActive == true) computerUse.Update(DesktopAgent.Core.ComputerUse.ComputerTaskState.Observing, "Permission decision received; revalidating the target");
                    cancellationToken.ThrowIfCancellationRequested();
                    Record("Policy", id, decision.ToString(), target);
                    if (decision == PermissionApproval.Deny)
                        return Finish(new(false, "PermissionDenied", "The action was not approved.", Target: target), call, watch);
                    // Sensitive capabilities remain per-action confirmations.
                    if (decision == PermissionApproval.AlwaysAllow && id is not (CapabilityIds.ApplicationClose or CapabilityIds.WorkspaceModify or CapabilityIds.ProjectModifyCatalog or CapabilityIds.ScreenInspectUi))
                        await policy.SetPolicyAsync(id, PermissionPolicy.Allow, cancellationToken);
                }
            }
            character.SetState(CharacterState.Working);
            if (preparation.Capabilities.Any(id => policy.Evaluate(id).Outcome is PolicyOutcome.Denied or PolicyOutcome.Unavailable))
                return Finish(new(false, "PermissionDenied", "Permissions changed while preparing this action. No action was performed.", Target: target), call, watch);
            if (preparation.Snapshot is not null && preparation.Snapshot != tool.CurrentSnapshot(arguments, preparation))
                return Finish(new(false, "InvalidConfiguration", "The prepared target changed while awaiting permission. Send the request again.", Target: target), call, watch);
            if (workspaceStamp is not null && workspaceStamp != JsonSerializer.Serialize(workspaces!.Find(target ?? "")))
                return Finish(new(false, "InvalidConfiguration", "The workspace changed while awaiting permission. Send the request again.", Target: target), call, watch);
            Record("AI tool", call.Name, "Started", target);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(call.Name is "launch_workspace" or "open_project" ? 3 : 1));
            var result = await tool.ExecutePreparedAsync(arguments, preparation, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            return Finish(result, call, watch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DesktopAgent.Core.UiAwareness.UiObservationException e)
        { return Finish(DesktopAgent.Infrastructure.UiAwareness.UiTools.Failure(e.Code), call, watch); }
        catch (OperationCanceledException) { return Finish(new(false, "Timeout", "The action timed out; no further actions were scheduled."), call, watch); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException)
        {
            logger.LogWarning("Tool arguments rejected Tool={Tool} ErrorType={ErrorType}", call.Name, ex.GetType().Name);
            return Finish(new(false, "InvalidArguments", "The action arguments are invalid. No action was performed."), call, watch);
        }
        catch (Exception ex)
        {
            logger.LogError("Tool failed Tool={Tool} ErrorType={ErrorType}", call.Name, ex.GetType().Name);
            return Finish(new(false, "Failed", "The action failed. Details are in the local log."), call, watch);
        }
    }
    private ToolResult Finish(ToolResult result, AiToolCall call, Stopwatch watch)
    {
        result = result with { DurationMilliseconds = watch.ElapsedMilliseconds };
        activity.Add(new(DateTimeOffset.Now, ComputerCommandType.AiToolRequest, result.Target ?? call.Name,
            result.Success ? result.Status == "Partial" ? ActionStatus.Partial : ActionStatus.Success :
                result.Status == "PermissionDenied" ? ActionStatus.PermissionDenied : ActionStatus.Failed,
            result.Message, "AI tool"));
        return result;
    }
    private void Record(string source, string action, string outcome, string? target) =>
        activity.Add(new(DateTimeOffset.Now, ComputerCommandType.AiToolRequest, target ?? action,
            outcome is "Denied" or "Deny" ? ActionStatus.PermissionDenied : ActionStatus.Success,
            action + ": " + outcome, source));
}

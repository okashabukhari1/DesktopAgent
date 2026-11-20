using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Workspaces.Enums;
using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Permissions;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Automation;

public sealed class ComputerCommandService(
    IComputerCommandParser parser,
    IComputerController controller,
    IActionHistoryService history,
    ICharacterService character,
    IWorkspaceService workspaces,
    IPolicyService policy,
    IAgentActionApprovalService approval,
    ILogger<ComputerCommandService> logger) : IComputerCommandService
{
    public async Task<ComputerActionResult> ExecuteAsync(string input, CancellationToken cancellationToken = default, string source = "Text")
    {
        logger.LogInformation("CommandReceived Length={Length} Source={Source}", input?.Length ?? 0, source);
        var command = parser.Parse(input ?? string.Empty);
        logger.LogInformation("CommandParsed Type={Type} Target={Target}", command.Type, SanitizeTarget(command));
        if (command.Type == ComputerCommandType.Invalid)
            return Complete(command, ComputerActionResult.Fail(ActionStatus.InvalidInput,
                command.ErrorMessage ?? "Command not recognized.", command.Target), source);

        if (string.Equals(source, "AI", StringComparison.OrdinalIgnoreCase))
        {
            var required = ComputerCommandCapabilities.RequiredFor(command.Type);
            if (command.Type == ComputerCommandType.LaunchWorkspace)
            {
                await workspaces.InitializeAsync(cancellationToken);
                required = WorkspaceCapabilities.RequiredFor(workspaces, command.Target ?? "");
            }
            foreach (var capabilityId in required)
            {
                var evaluation = policy.Evaluate(capabilityId);
                if (evaluation.Outcome == PolicyOutcome.Denied)
                    return Complete(command, ComputerActionResult.Fail(ActionStatus.PermissionDenied,
                        $"I can't perform this action because '{AgentCapabilities.Find(capabilityId)?.Name}' is denied in Permissions.", command.Target), source);
                if (evaluation.Outcome == PolicyOutcome.Unavailable)
                    return Complete(command, ComputerActionResult.Fail(ActionStatus.Unsupported,
                        $"This action is unavailable: {evaluation.Message}", command.Target), source);
                if (evaluation.Outcome == PolicyOutcome.ConfirmationRequired)
                {
                    var capability = AgentCapabilities.Find(capabilityId)!;
                    character.SetState(CharacterState.Attention);
                    var approvalResult = await approval.RequestAsync(capability, command.Target ?? command.Type.ToString(), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (approvalResult == PermissionApproval.Deny)
                        return Complete(command, ComputerActionResult.Fail(ActionStatus.PermissionDenied,
                            "The action was not approved.", command.Target), source);
                    if (approvalResult == PermissionApproval.AlwaysAllow && capabilityId is not (CapabilityIds.ApplicationClose or CapabilityIds.WorkspaceModify))
                        await policy.SetPolicyAsync(capabilityId, PermissionPolicy.Allow, cancellationToken);
                }
            }
        }

        character.SetState(CharacterState.Working);
        logger.LogInformation("ActionStarted Type={Type}", command.Type);
        try
        {
            var result = command.Type is ComputerCommandType.ListWorkspaces or ComputerCommandType.WorkspaceStatus or ComputerCommandType.LaunchWorkspace or ComputerCommandType.StopWorkspace
                ? await ExecuteWorkspaceCommandAsync(command, cancellationToken)
                : await controller.ExecuteAsync(command, cancellationToken);
            return Complete(command, result, source);
        }
        catch (OperationCanceledException)
        {
            return Complete(command, ComputerActionResult.Fail(ActionStatus.Cancelled, "The action was cancelled.", command.Target), source);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ActionFailed Type={Type}", command.Type);
            return Complete(command, ComputerActionResult.Fail(ActionStatus.Failed,
                "The action failed unexpectedly. Details were written to the local log.", command.Target, exception.GetType().Name), source);
        }
    }

    private async Task<ComputerActionResult> ExecuteWorkspaceCommandAsync(ParsedComputerCommand command, CancellationToken cancellationToken)
    {
        await workspaces.InitializeAsync(cancellationToken);
        if (command.Type == ComputerCommandType.ListWorkspaces)
        {
            var items = workspaces.Workspaces.OrderBy(item => item.Name).ToList();
            return items.Count == 0
                ? ComputerActionResult.Ok("No workspaces have been created yet.")
                : ComputerActionResult.Ok("Workspaces:\n" + string.Join("\n", items.Select(item => $"• {item.Name}")));
        }

        if (command.Type == ComputerCommandType.WorkspaceStatus)
        {
            var sessions = workspaces.Sessions.Values.OrderByDescending(item => item.StartedAt).Take(10).ToList();
            return sessions.Count == 0 ? ComputerActionResult.Ok("No workspace launches have been recorded in this session.")
                : ComputerActionResult.Ok("Recent workspace launches:\n" + string.Join("\n", sessions.Select(item =>
                    $"• {item.WorkspaceName}: {item.Status} ({item.Results.Count} steps)")));
        }

        var workspace = workspaces.Find(command.Target ?? string.Empty);
        if (workspace is null) return ComputerActionResult.Fail(ActionStatus.NotFound, $"Workspace '{command.Target}' was not found.", command.Target);
        if (command.Type == ComputerCommandType.StopWorkspace)
            return workspaces.Cancel(workspace.Id)
                ? ComputerActionResult.Ok($"Stopping {workspace.Name}…", workspace.Name)
                : ComputerActionResult.Fail(ActionStatus.NotFound, $"{workspace.Name} is not currently launching.", workspace.Name);

        var launch = await workspaces.LaunchAsync(workspace.Id, cancellationToken);
        if (launch is null) return ComputerActionResult.Fail(ActionStatus.NotFound, $"Workspace '{workspace.Name}' was not found.", workspace.Name);
        var status = launch.Session.Status switch
        {
            WorkspaceLaunchStatus.Ready => ActionStatus.Success,
            WorkspaceLaunchStatus.Partial => ActionStatus.Partial,
            WorkspaceLaunchStatus.Cancelled => ActionStatus.Cancelled,
            _ => ActionStatus.Failed
        };
        return new(launch.Success, status, launch.Message, workspace.Name, DateTimeOffset.Now,
            AdditionalData: new Dictionary<string, string>
            {
                ["SessionId"] = launch.Session.SessionId.ToString(),
                ["WorkspaceStatus"] = launch.Session.Status.ToString(),
                ["Steps"] = launch.Session.Results.Count.ToString()
                ,["StepResults"] = System.Text.Json.JsonSerializer.Serialize(launch.Session.Results,
                    new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })
            });
    }

    private ComputerActionResult Complete(ParsedComputerCommand command, ComputerActionResult result, string source)
    {
        history.Add(new ActionHistoryEntry(result.Timestamp, command.Type, SanitizeTarget(command), result.Status, result.Message, source));
        character.SetState(result.Success ? CharacterState.Idle : CharacterState.Attention);
        var firstLine = result.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? result.Message;
        if (firstLine.Length > 180) firstLine = firstLine[..177] + "...";
        character.ShowMessage(firstLine);
        if (result.Success)
            logger.LogInformation("ActionCompleted Type={Type} Status={Status}", command.Type, result.Status);
        else
            logger.LogWarning("ActionFailed Type={Type} Status={Status} ErrorCode={ErrorCode}", command.Type, result.Status, result.ErrorCode);
        return result;
    }

    private static string SanitizeTarget(ParsedComputerCommand command) => command.Type switch
    {
        ComputerCommandType.OpenFile => Path.GetFileName(command.Target) ?? string.Empty,
        ComputerCommandType.OpenFolder => command.Target is null ? string.Empty : Path.GetFileName(command.Target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
        ComputerCommandType.OpenUrl when Uri.TryCreate(command.Target, UriKind.Absolute, out var uri) => uri.Host,
        _ => command.Target ?? string.Empty
    };
}

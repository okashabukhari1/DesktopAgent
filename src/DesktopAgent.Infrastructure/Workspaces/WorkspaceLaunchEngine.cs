using DesktopAgent.Core.Automation.Enums;
using DesktopAgent.Core.Automation.Interfaces;
using DesktopAgent.Core.Automation.Models;
using DesktopAgent.Core.Workspaces.Enums;
using DesktopAgent.Core.Workspaces.Interfaces;
using DesktopAgent.Core.Workspaces.Models;
using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Workspaces;

public sealed class WorkspaceLaunchEngine(
    IComputerController controller,
    IExternalWindowService windows,
    IActionHistoryService history,
    ICharacterService character,
    ILogger<WorkspaceLaunchEngine> logger) : IWorkspaceLaunchEngine
{
    public event EventHandler<WorkspaceProgress>? ProgressChanged;

    public async Task<WorkspaceLaunchResult> LaunchAsync(WorkspaceDefinition workspace, CancellationToken cancellationToken = default)
    {
        var session = new WorkspaceSession { WorkspaceId = workspace.Id, WorkspaceName = workspace.Name };
        var total = CountSteps(workspace);
        var completed = 0;
        character.SetState(CharacterState.Working);
        history.Add(new(DateTimeOffset.Now, ComputerCommandType.LaunchWorkspace, workspace.Name, ActionStatus.Success, "Workspace launch started.", "Workspace"));
        Publish(session, completed, total, $"Starting {workspace.Name}…");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(workspace.LaunchTimeoutSeconds));
        try
        {
            foreach (var action in workspace.PreLaunchActions.OrderBy(item => item.Order))
                if (!await RunAction(action, WorkspaceStepKind.PreLaunchAction)) return Finish(WorkspaceLaunchStatus.Failed, "Workspace stopped after a before-launch action failed.");

            foreach (var application in workspace.Applications.OrderBy(item => item.LaunchOrder))
            {
                timeout.Token.ThrowIfCancellationRequested();
                var target = application.CatalogApplicationId ?? application.ApplicationIdentifier;
                ComputerActionResult result;
                if (application.Launch)
                {
                    result = await controller.ExecuteAsync(new(application.CatalogApplicationId is null && Path.IsPathFullyQualified(application.ApplicationIdentifier)
                        ? ComputerCommandType.OpenExplicitApplication : ComputerCommandType.OpenApplication, target), timeout.Token);
                    if (result.Status == ActionStatus.NotFound && application.CatalogApplicationId is not null)
                    {
                        target = application.ApplicationIdentifier;
                        result = await controller.ExecuteAsync(new(ComputerCommandType.OpenApplication, target), timeout.Token);
                    }
                }
                else if (application.FocusIfRunning)
                    result = await controller.ExecuteAsync(new(ComputerCommandType.FocusApplication, target), timeout.Token);
                else
                    continue;
                Add(WorkspaceStepKind.Application, target, result);
                if (result.Success)
                {
                    if (result.Status == ActionStatus.AlreadyRunning) session.AlreadyRunningApplications.Add(target);
                    else session.LaunchedApplications.Add(target);
                }
                else if (!workspace.ContinueOnFailure) return Finish(WorkspaceLaunchStatus.Failed, $"Workspace stopped because {target} could not be prepared.");

                if (result.Success && application.TargetMonitor.HasValue)
                {
                    var requestedMonitor = application.TargetMonitor.Value;
                    var available = windows.GetMonitors().Any(monitor => monitor.Number == requestedMonitor);
                    var monitorResult = await controller.ExecuteAsync(new(ComputerCommandType.MoveToMonitor, target,
                        MonitorNumber: available ? requestedMonitor : 1), timeout.Token);
                    if (!available && monitorResult.Success)
                        monitorResult = ComputerActionResult.Fail(ActionStatus.InvalidInput,
                            $"Monitor {requestedMonitor} is unavailable; {target} was moved to the primary monitor.", target);
                    Add(WorkspaceStepKind.Layout, target, monitorResult);
                    if (!monitorResult.Success && !workspace.ContinueOnFailure) return Finish(WorkspaceLaunchStatus.Failed, $"Workspace stopped while positioning {target}.");
                }
                if (result.Success && application.Layout.HasValue)
                {
                    var layoutResult = await controller.ExecuteAsync(new(ComputerCommandType.ApplyLayout, target, application.Layout), timeout.Token);
                    Add(WorkspaceStepKind.Layout, target, layoutResult);
                    if (!layoutResult.Success && !workspace.ContinueOnFailure) return Finish(WorkspaceLaunchStatus.Failed, $"Workspace stopped while arranging {target}.");
                }
                if (application.DelayAfterLaunchMs > 0) await Task.Delay(application.DelayAfterLaunchMs, timeout.Token);
            }

            var resources = workspace.Folders.Select(item => (item, ComputerCommandType.OpenFolder, WorkspaceStepKind.Folder))
                .Concat(workspace.Files.Select(item => (item, ComputerCommandType.OpenFile, WorkspaceStepKind.File)))
                .Concat(workspace.Urls.Select(item => (item, ComputerCommandType.OpenUrl, WorkspaceStepKind.Url)))
                .OrderBy(tuple => tuple.item.Order);
            foreach (var (resource, commandType, kind) in resources)
            {
                var result = await controller.ExecuteAsync(new(commandType, resource.Value), timeout.Token);
                Add(kind, resource.DisplayName.Length == 0 ? resource.Value : resource.DisplayName, result);
                if (!result.Success && !workspace.ContinueOnFailure) return Finish(WorkspaceLaunchStatus.Failed, "Workspace stopped after a resource could not be opened.");
            }

            foreach (var action in workspace.PostLaunchActions.OrderBy(item => item.Order))
                if (!await RunAction(action, WorkspaceStepKind.PostLaunchAction)) return Finish(WorkspaceLaunchStatus.Failed, "Workspace stopped after an after-launch action failed.");

            if (!string.IsNullOrWhiteSpace(workspace.FinalFocusApplication))
            {
                var result = await controller.ExecuteAsync(new(ComputerCommandType.FocusApplication, workspace.FinalFocusApplication), timeout.Token);
                Add(WorkspaceStepKind.FinalFocus, workspace.FinalFocusApplication, result);
            }

            var failures = session.Results.Count(result => !result.Success);
            return Finish(failures == 0 ? WorkspaceLaunchStatus.Ready : WorkspaceLaunchStatus.Partial,
                failures == 0 ? $"{workspace.Name} is ready." : $"{workspace.Name} is ready with {failures} issue{(failures == 1 ? string.Empty : "s")}.");
        }
        catch (OperationCanceledException)
        {
            var status = cancellationToken.IsCancellationRequested ? WorkspaceLaunchStatus.Cancelled : WorkspaceLaunchStatus.Failed;
            return Finish(status, status == WorkspaceLaunchStatus.Cancelled ? $"{workspace.Name} launch was cancelled." : $"{workspace.Name} exceeded its launch timeout.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "WorkspaceLaunchFailed WorkspaceId={WorkspaceId}", workspace.Id);
            return Finish(WorkspaceLaunchStatus.Failed, $"{workspace.Name} could not be launched. Details were written to the local log.");
        }

        async Task<bool> RunAction(WorkspaceSafeAction action, WorkspaceStepKind kind)
        {
            var command = action.Type switch
            {
                WorkspaceActionType.Focus => ComputerCommandType.FocusApplication,
                WorkspaceActionType.Minimize => ComputerCommandType.MinimizeWindow,
                WorkspaceActionType.Maximize => ComputerCommandType.MaximizeWindow,
                WorkspaceActionType.Restore => ComputerCommandType.RestoreWindow,
                WorkspaceActionType.Close => ComputerCommandType.CloseApplication,
                _ => ComputerCommandType.Invalid
            };
            var result = await controller.ExecuteAsync(new(command, action.Target), timeout.Token);
            Add(kind, action.Target, result);
            return result.Success || workspace.ContinueOnFailure;
        }

        void Add(WorkspaceStepKind kind, string target, ComputerActionResult result)
        {
            var step = new WorkspaceStepResult(kind, target, result.Status, result.Message, result.Timestamp);
            session.Results.Add(step);
            completed++;
            Publish(session, completed, total, result.Message, step);
        }

        WorkspaceLaunchResult Finish(WorkspaceLaunchStatus status, string message)
        {
            session.Status = status;
            session.CompletedAt = DateTimeOffset.Now;
            Publish(session, completed, total, message);
            history.Add(new(DateTimeOffset.Now, ComputerCommandType.LaunchWorkspace, workspace.Name,
                status == WorkspaceLaunchStatus.Ready ? ActionStatus.Success : status == WorkspaceLaunchStatus.Partial ? ActionStatus.Partial : status == WorkspaceLaunchStatus.Cancelled ? ActionStatus.Cancelled : ActionStatus.Failed, message, "Workspace"));
            character.SetState(status is WorkspaceLaunchStatus.Ready ? CharacterState.Idle : CharacterState.Attention);
            character.ShowMessage(message);
            logger.LogInformation("WorkspaceLaunchCompleted WorkspaceId={WorkspaceId} Status={Status} Steps={Steps}", workspace.Id, status, session.Results.Count);
            return new(session, message);
        }
    }

    private void Publish(WorkspaceSession session, int complete, int total, string message, WorkspaceStepResult? step = null) =>
        ProgressChanged?.Invoke(this, new(session.WorkspaceId, session.WorkspaceName, session.Status, complete, total, message, step));

    private static int CountSteps(WorkspaceDefinition workspace) =>
        workspace.PreLaunchActions.Count + workspace.PostLaunchActions.Count + workspace.Folders.Count + workspace.Files.Count + workspace.Urls.Count +
        workspace.Applications.Count(item => item.Launch || item.FocusIfRunning) + workspace.Applications.Count(item => (item.Launch || item.FocusIfRunning) && item.TargetMonitor.HasValue) +
        workspace.Applications.Count(item => (item.Launch || item.FocusIfRunning) && item.Layout.HasValue) + (string.IsNullOrWhiteSpace(workspace.FinalFocusApplication) ? 0 : 1);
}

using System.Diagnostics;
using DesktopAgent.Core.ComputerUse;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Permissions;
using DesktopAgent.Core.UiAwareness;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.ComputerUse;

public sealed class ComputerActionExecutor(IComputerInteractionBackend backend, ComputerUseSession session,
    ISettingsService settings, IPolicyService policy, IAgentActionApprovalService approval,
    ILogger<ComputerActionExecutor>? logger = null) : IComputerActionExecutor
{
    private readonly SemaphoreSlim _lane = new(1, 1);
    public async Task<ComputerInteractionResult> ExecuteAsync(ComputerAction action, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        if (!await _lane.WaitAsync(0, ct)) return new(action.Id, false, "Busy", "An action is already in progress.");
        var attempted = false;
        ComputerInteractionResult result;
        try
        {
            session.ReserveAction();
            session.SetAction(action);
            logger?.LogInformation("ComputerAction TaskId={TaskId} ActionId={ActionId} Type={Type} Stage=Requested ElementRequired={ElementRequired}",
                session.Id, action.Id, action.Type, action.Type is not (ComputerActionType.PressKey or ComputerActionType.PressShortcut or ComputerActionType.ScrollWindow));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Token);
            var token = linked.Token;
            var evaluation = policy.Evaluate(ComputerUseCapabilities.Actions);
            if (evaluation.Outcome is PolicyOutcome.Denied or PolicyOutcome.Unavailable) throw new InvalidOperationException("PermissionDenied");
            session.Update(ComputerTaskState.Observing, "Resolving and revalidating the selected control");
            var target = await backend.PrepareAsync(action, token);
            logger?.LogInformation("ComputerAction TaskId={TaskId} ActionId={ActionId} Stage=Prepared Application={Application} Hwnd={Hwnd} Pid={Pid}",
                session.Id, action.Id, target.Window.ApplicationName, target.Window.Handle, target.Window.ProcessId);
            if (!session.Authorizes(target, action)) throw new InvalidOperationException("OutsideUserTarget");
            var risk = Classify(action, target);
            if (risk == PermissionRisk.Critical) throw new InvalidOperationException("ProtectedAction");
            var trusted = settings.Current.ComputerUse.Mode == ComputerUseMode.TrustedActions &&
                settings.Current.ComputerUse.TrustedApplications.Contains(target.Window.HostedProcessName ?? target.Window.ProcessName, StringComparer.OrdinalIgnoreCase) &&
                action.Type is ComputerActionType.Toggle or ComputerActionType.Select or ComputerActionType.SetValue;
            if (evaluation.Outcome == PolicyOutcome.ConfirmationRequired || risk >= PermissionRisk.High || risk == PermissionRisk.Medium && !trusted)
            {
                session.Update(ComputerTaskState.WaitingForUser, "Waiting for approval: " + action.Type + " in " + target.Window.ApplicationName);
                var capability = ComputerUseCapabilities.Capability with { Risk = risk,
                    Name = action.Type + " in " + target.Window.ApplicationName,
                    Description = "Approve this single UI action. Text contents are not included in activity logs." };
                if (await approval.RequestAsync(capability, "Window: " + target.Window.Title + "\nSelected control: " + target.Element.ControlType + " · " + target.Element.Name, token) == PermissionApproval.Deny)
                    throw new InvalidOperationException("PermissionDenied");
            }
            token.ThrowIfCancellationRequested();
            if (policy.Evaluate(ComputerUseCapabilities.Actions).Outcome is PolicyOutcome.Denied or PolicyOutcome.Unavailable)
                throw new InvalidOperationException("PermissionDenied");
            var epoch = session.InputEpoch;
            session.Guard(epoch);
            session.Update(ComputerTaskState.Acting, action.Type + " · " + target.Window.ApplicationName);
            attempted = true;
            // Backend must revalidate the pinned target AFTER approval; it never trusts stale coordinates.
            result = await backend.ExecuteAsync(action, target, () =>
            {
                session.Guard(epoch);
                if (policy.Evaluate(ComputerUseCapabilities.Actions).Outcome is PolicyOutcome.Denied or PolicyOutcome.Unavailable)
                    throw new OperationCanceledException("Permission revoked");
            }, token);
        }
        catch (OperationCanceledException) { result = new(action.Id, false, "Cancelled", "Stopped. A dispatched native action cannot be undone.", InputMayHaveOccurred: attempted); }
        catch (UiObservationException e)
        {
            var message = e.Code switch
            {
                "PythonEditorRequired" => "The selected control is not a text editor. Inspect Notepad and select its observed Document or Edit control. No input was sent.",
                "PythonProtectedEditor" => "The selected editor is protected. No input was sent.",
                "PythonUnsupportedApplication" => "The Python text backend supports Notepad only. No input was sent.",
                _ => "Control unavailable or changed. Observe again before continuing."
            };
            result = new(action.Id, false, e.Code, message, ErrorCode: e.Code, InputMayHaveOccurred: attempted);
        }
        catch (InvalidOperationException e) { result = new(action.Id, false, e.Message, "Computer action not allowed: " + e.Message + ".", ErrorCode: e.Message, InputMayHaveOccurred: attempted); }
        catch (Exception) { result = new(action.Id, false, "Failed", "The action failed; no automatic retry was attempted.", InputMayHaveOccurred: attempted); }
        finally { _lane.Release(); }
        result = result with { DurationMilliseconds = watch.ElapsedMilliseconds };
        logger?.LogInformation("ComputerAction TaskId={TaskId} ActionId={ActionId} Stage=Result Status={Status} Dispatched={Dispatched} Verified={Verified} Category={Category}",
            session.Id, action.Id, result.Status, result.InputMayHaveOccurred, result.Verified, result.ErrorCode);
        session.Record(result);
        return result;
    }
    public static PermissionRisk Classify(ComputerAction action, PreparedComputerTarget target)
    {
        var w = target.Window; var e = target.Element;
        var process = (w.HostedProcessName ?? w.ProcessName).ToLowerInvariant();
        if (w.Title == "[PROTECTED WINDOW]" || string.Equals(w.Title, "Run", StringComparison.OrdinalIgnoreCase) || e.IsPassword != false ||
            new[] { "cmd", "powershell", "pwsh", "windowsterminal", "regedit", "mmc", "taskmgr", "conhost", "consent", "credentialuibroker" }.Contains(process)) return PermissionRisk.Critical;
        var label = (e.Name + " " + e.AutomationId).ToLowerInvariant();
        if (new[] { "password", "credential", "sign in", "login", "security", "api key", "payment" }.Any(label.Contains)) return PermissionRisk.Critical;
        if (System.Text.RegularExpressions.Regex.IsMatch(label, @"\b(pin|otp|2fa|captcha)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return PermissionRisk.Critical;
        if (new[] { "delete", "remove", "erase", "overwrite", "send", "submit", "publish", "purchase", "buy", "install", "download", "save" }.Any(label.Contains)) return PermissionRisk.High;
        if (System.Text.RegularExpressions.Regex.IsMatch(label, @"\b(yes|confirm|continue|accept|agree|clear data|empty trash)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return PermissionRisk.High;
        if (action.Key is "Ctrl+S" or "Enter") return PermissionRisk.High;
        if (action.Type is ComputerActionType.PressKey or ComputerActionType.PressShortcut && action.Key is "Ctrl+A" or "Ctrl+C" or "Ctrl+F" or "Alt+Tab" or "Tab" or "Shift+Tab" or "Escape" or "Left" or "Right" or "Up" or "Down" or "PageUp" or "PageDown") return PermissionRisk.Low;
        if (action.Type == ComputerActionType.SetValue && e.ControlType is "Slider" or "Spinner") return PermissionRisk.Medium;
        if (action.Type is ComputerActionType.Focus or ComputerActionType.ScrollElement or ComputerActionType.ScrollWindow or ComputerActionType.Expand or ComputerActionType.Collapse) return PermissionRisk.Low;
        if (e.ControlType is "MenuItem" or "TabItem" && action.Type is ComputerActionType.Select or ComputerActionType.Invoke or ComputerActionType.ClickElement) return PermissionRisk.Low;
        if (action.Type is ComputerActionType.TypeText or ComputerActionType.SetValue)
            return PermissionRisk.Low;
        if (process is "calculatorapp" or "calculator" && action.Type is ComputerActionType.Invoke or ComputerActionType.ClickElement) return PermissionRisk.Low;
        if (process is "calculatorapp" or "calculator" && action.Type == ComputerActionType.PressKey && action.Key is { Length: 1 } arithmetic && "0123456789+-*/.=".Contains(arithmetic, StringComparison.Ordinal)) return PermissionRisk.Low;
        // Generic clicks/Enter can commit a form; never infer low risk from an LLM risk label.
        return PermissionRisk.Medium;
    }
}

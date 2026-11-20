using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using DesktopAgent.Core.ComputerUse;
using DesktopAgent.Core.UiAwareness;
using DesktopAgent.Infrastructure.ComputerUse;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.UiAwareness;

public sealed partial class WindowsUiAutomationService : IComputerInteractionBackend
{
    private sealed record Lease(UiSnapshot Snapshot, string Identity, Dictionary<string, int[]> References);
    private readonly Dictionary<Guid, Lease> _actionLeases = [];
    // Retain only opaque runtime identities across focus events. Every mutation resolves the native element anew.
    private void RememberActionReferences(UiSnapshot snapshot, string identity, Dictionary<string, int[]> references)
    {
        foreach (var id in _actionLeases.Where(x => DateTimeOffset.UtcNow - x.Value.Snapshot.Timestamp > TimeSpan.FromSeconds(60)).Select(x => x.Key).ToArray()) _actionLeases.Remove(id);
        if (_actionLeases.Count >= 8) _actionLeases.Remove(_actionLeases.Keys.First());
        _actionLeases[snapshot.SnapshotVersion] = new(snapshot, identity, references.ToDictionary(x => x.Key, x => x.Value.ToArray()));
    }
    public async Task<PreparedComputerTarget> PrepareAsync(ComputerAction action, CancellationToken ct)
    {
        var prepared = GetObservedActionTarget(action);
        await _worker.RunAsync(token => { ResolveActionElement(prepared, token); return true; }, ct);
        return prepared;
    }
    // Window actions never resolve a cached element. An optional locally-issued
    // snapshot can disambiguate the window, but its title/control state is not reused.
    public PreparedComputerTarget GetWindowActionTarget(ComputerAction action)
    {
        EnsureDesktop();
        if (!action.WindowReference.StartsWith("window:", StringComparison.Ordinal) || action.WindowReference.Length > 128 || action.Text?.Length > 2000)
            throw new ArgumentException("Invalid action");
        if (action.Text is { } input) { secrets.Redact(input, out var sensitive); if (sensitive) throw new UiObservationException("SensitiveInputBlocked"); }
        WindowContext? observed = null;
        string? identity = null;
        lock (_sync)
            if (_actionLeases.TryGetValue(action.SnapshotVersion, out var lease) &&
                DateTimeOffset.UtcNow - lease.Snapshot.Timestamp <= TimeSpan.FromSeconds(60))
            { observed = lease.Snapshot.Window; identity = lease.Identity; }
        var application = action.WindowReference[7..];
        if (application.Equals("Chrome", StringComparison.OrdinalIgnoreCase)) application = "Google Chrome";
        if (observed is not null && !observed.ApplicationName.Equals(application, StringComparison.OrdinalIgnoreCase))
            throw new UiObservationException("WrongWindow");
        var matches = Probe.List().Where(w => !Self.IsAgentWindow(w.Handle) && w.ApplicationName.Equals(application, StringComparison.OrdinalIgnoreCase)).ToArray();
        var window = observed is not null
            ? matches.SingleOrDefault(w => w.Handle == observed.Handle && w.ProcessId == observed.ProcessId) ?? throw new UiObservationException("WindowClosed")
            : matches.Length == 1 ? matches[0] : throw new UiObservationException(matches.Length == 0 ? "WindowNotFound" : "AmbiguousWindow");
        var freshIdentity = WindowIdentity(window.Handle);
        if (identity is not null && identity != freshIdentity) throw new UiObservationException("WindowIdentityChanged");
        if (!window.IsVisible || !window.IsEnabled || window.IsMinimized || window.Title == "[PROTECTED WINDOW]")
            throw new UiObservationException("ProtectedWindow");
        return new(window, new UiElement { ControlType = "Window", IsVisible = true, IsEnabled = true }, freshIdentity, []);
    }
    // Returns locally issued observation identities only. Alternate backends must independently
    // validate this exact runtime identity and sensitivity; this method never authorizes input.
    public PreparedComputerTarget GetObservedActionTarget(ComputerAction action, bool usePinnedNotepadObservation = false)
    {
        if (action.Text?.Length > 2000 || action.ElementReference.Length > 48 || !action.ElementReference.StartsWith("ui:", StringComparison.Ordinal)) throw new ArgumentException("Invalid action");
        if (action.Text is { } input) { secrets.Redact(input, out var sensitive); if (sensitive) throw new UiObservationException("SensitiveInputBlocked"); }
        Lease lease; int[] runtime; UiElement item;
        lock (_sync)
        {
            if (!_actionLeases.TryGetValue(action.SnapshotVersion, out lease!) || DateTimeOffset.UtcNow - lease.Snapshot.Timestamp > TimeSpan.FromSeconds(60) ||
                !lease.References.TryGetValue(action.ElementReference, out runtime!))
            {
                logger?.LogWarning("UiActionStale ActionId={ActionId} Reason=LeaseExpiredOrReferenceMissing", action.Id);
                throw new UiObservationException("StaleElement");
            }
            item = UiAlgorithms.Flatten(lease.Snapshot.Window.RootElement!).FirstOrDefault(e => e.Id == action.ElementReference) ?? throw new UiObservationException("StaleElement");
        }
        if (!action.WindowReference.Equals("window:" + lease.Snapshot.Window.ApplicationName, StringComparison.OrdinalIgnoreCase)) throw new UiObservationException("WrongWindow");
        // Logical references must also be unambiguous among current windows.
        var matches = Probe.List().Where(w => !Self.IsAgentWindow(w.Handle) && w.ApplicationName.Equals(lease.Snapshot.Window.ApplicationName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (usePinnedNotepadObservation && lease.Snapshot.Window.ApplicationName == "Notepad")
        {
            if (!matches.Any(w => w.Handle == lease.Snapshot.Window.Handle && w.ProcessId == lease.Snapshot.Window.ProcessId)) throw new UiObservationException("WindowClosed");
        }
        else if (matches.Length != 1 || matches[0].Handle != lease.Snapshot.Window.Handle) throw new UiObservationException("AmbiguousWindow");
        var prepared = new PreparedComputerTarget(lease.Snapshot.Window, item, lease.Identity, runtime.ToArray());
        if (WindowIdentity(prepared.Window.Handle) != prepared.Identity)
        {
            logger?.LogWarning("UiActionStale ActionId={ActionId} Reason=WindowIdentityChanged", action.Id);
            throw new UiObservationException("StaleElement");
        }
        return prepared;
    }
    private AutomationElement ResolveActionElement(PreparedComputerTarget target, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); EnsureDesktop();
        if (WindowIdentity(target.Window.Handle) != target.Identity) throw new UiObservationException("StaleElement");
        var window = Probe.Read(target.Window.Handle) ?? throw new UiObservationException("WindowClosed");
        if (window.Title != target.Window.Title)
        {
            logger?.LogWarning("UiActionStale Reason=WindowTitleChanged Hwnd={Hwnd} Pid={Pid}", window.Handle, window.ProcessId);
            throw new UiObservationException("StaleElement");
        }
        if (window.Title == "[PROTECTED WINDOW]" || !window.IsVisible || !window.IsEnabled || window.IsMinimized ||
            WindowInput.IsElevated(window.ProcessId) || window.HostedProcessId is { } hosted && WindowInput.IsElevated(hosted)) throw new UiObservationException("ProtectedWindow");
        if (WindowInput.HasOtherActivePopup(window.Handle)) throw new UiObservationException("ModalDialog");
        var root = AutomationElement.FromHandle((nint)window.Handle);
        AutomationElement? found = null; var count = 0;
        void Visit(AutomationElement element, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (++count > 200 || depth > 8 || found is not null) return;
            if (element.GetRuntimeId().SequenceEqual(target.RuntimeId)) { found = element; return; }
            var child = TreeWalker.ControlViewWalker.GetFirstChild(element);
            while (child is not null && found is null && count < 200)
            { Visit(child, depth + 1); child = TreeWalker.ControlViewWalker.GetNextSibling(child); }
        }
        Visit(root, 0);
        if (found is null)
        {
            logger?.LogWarning("UiActionStale Reason=RuntimeReferenceMissing Hwnd={Hwnd} Pid={Pid}", window.Handle, window.ProcessId);
            throw new UiObservationException("StaleElement");
        }
        var ancestor = found;
        for (var depth = 0; depth < 12 && ancestor is not null; depth++)
        {
            ct.ThrowIfCancellationRequested();
            var label = (ancestor.Current.AutomationId + " " + ancestor.Current.ClassName).ToLowerInvariant();
            if (new[] { "terminal", "console", "credential", "password", "authentication", "signin", "security", "privacy", "captcha" }.Any(label.Contains)) throw new UiObservationException("ProtectedControl");
            if (ancestor.GetRuntimeId().SequenceEqual(root.GetRuntimeId())) break;
            ancestor = TreeWalker.ControlViewWalker.GetParent(ancestor);
        }
        var current = found.Current;
        var previous = target.Element;
        var passwordKnownFalse = found.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true) is false;
        var typeChanged = current.ControlType.ProgrammaticName.Replace("ControlType.", "") != previous.ControlType;
        var idChanged = current.AutomationId != previous.AutomationId;
        var nameChanged = previous.ControlType is not ("Edit" or "Text" or "Document") && current.Name != previous.Name;
        if (current.IsPassword || !current.IsEnabled || current.IsOffscreen || !passwordKnownFalse || typeChanged || idChanged || nameChanged)
        {
            // Only fixed flags: never log element names, identifiers, field contents or request text.
            logger?.LogWarning("UiActionRejected Category=ChangedOrSensitiveControl Password={Password} PasswordKnownFalse={PasswordKnownFalse} Enabled={Enabled} Offscreen={Offscreen} TypeChanged={TypeChanged} IdChanged={IdChanged} NameChanged={NameChanged}",
                current.IsPassword, passwordKnownFalse, current.IsEnabled, current.IsOffscreen, typeChanged, idChanged, nameChanged);
            throw new UiObservationException("ChangedOrSensitiveControl");
        }
        return found;
    }
    public Task<ComputerInteractionResult> ExecuteAsync(ComputerAction action, PreparedComputerTarget target, Action guard, CancellationToken ct) =>
        _worker.RunAsync(token =>
        {
            var element = ResolveActionElement(target, token);
            void Check() { token.ThrowIfCancellationRequested(); guard(); EnsureDesktop(); if (WindowIdentity(target.Window.Handle) != target.Identity) throw new UiObservationException("WindowClosed"); }
            Check();
            // Pattern actions still require the intended window foreground: no hidden background mutations.
            if ((long)Native.GetForegroundWindow() != target.Window.Handle)
            {
                if (!Self.IsAgentWindow((long)Native.GetForegroundWindow())) throw new UiObservationException("UserFocusChanged");
                if (!WindowInput.TryFocus(target.Window.Handle)) throw new UiObservationException("FocusDenied");
                element = ResolveActionElement(target, token);
            }
            void InputGuard() { Check(); if ((long)Native.GetForegroundWindow() != target.Window.Handle) throw new UiObservationException("UserFocusChanged"); }
            bool verified = false; string? before = null, after = null;
            Func<bool>? verification = null;
            var windowSwitch = action.Key == "Alt+Tab";
            // The guard is the last operation before each native mutation. No native call can be forcibly undone.
            switch (action.Type)
            {
                case ComputerActionType.Focus:
                    InputGuard(); element.SetFocus(); verification = () => AutomationElement.FocusedElement.GetRuntimeId().SequenceEqual(target.RuntimeId); break;
                case ComputerActionType.Toggle:
                    var toggle = Pattern<TogglePattern>(element, TogglePattern.Pattern); var oldToggle = toggle.Current.ToggleState;
                    before = oldToggle.ToString(); InputGuard(); toggle.Toggle();
                    verification = () => { after = toggle.Current.ToggleState.ToString(); return toggle.Current.ToggleState != oldToggle; }; break;
                case ComputerActionType.Select:
                    var select = Pattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern); before = select.Current.IsSelected.ToString();
                    InputGuard(); select.Select(); verification = () => { after = select.Current.IsSelected.ToString(); return select.Current.IsSelected; }; break;
                case ComputerActionType.Expand:
                case ComputerActionType.Collapse:
                    var expand = Pattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern); before = expand.Current.ExpandCollapseState.ToString();
                    InputGuard(); if (action.Type == ComputerActionType.Expand) expand.Expand(); else expand.Collapse();
                    verification = () => { after = expand.Current.ExpandCollapseState.ToString(); return expand.Current.ExpandCollapseState ==
                        (action.Type == ComputerActionType.Expand ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed); }; break;
                case ComputerActionType.SetValue:
                    if (element.TryGetCurrentPattern(RangeValuePattern.Pattern, out var rangeObject))
                    {
                        var rangeValue = (RangeValuePattern)rangeObject;
                        if (rangeValue.Current.IsReadOnly || !double.TryParse(action.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
                            !double.IsFinite(number) || number < rangeValue.Current.Minimum || number > rangeValue.Current.Maximum) throw new UiObservationException("InvalidRangeValue");
                        before = rangeValue.Current.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        InputGuard(); rangeValue.SetValue(number);
                        verification = () => { after = rangeValue.Current.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); return Math.Abs(rangeValue.Current.Value - number) < 0.000001; };
                        break;
                    }
                    RequireEditable(element); var value = Pattern<ValuePattern>(element, ValuePattern.Pattern);
                    if (value.Current.IsReadOnly || action.Text is null) throw new UiObservationException("NotEditable");
                    InputGuard(); value.SetValue(action.Text);
                    // Compare only this explicitly selected, known non-password field. Never export its content.
                    verification = () => value.Current.Value == action.Text; before = "Selected non-sensitive field"; after = "Exact value match (contents omitted)"; break;
                case ComputerActionType.ScrollElement:
                    if (action.Amount is not (-1 or 1)) throw new UiObservationException("NotScrollable");
                    if (!element.TryGetCurrentPattern(ScrollPattern.Pattern, out var scrollObject))
                    {
                        var oldWheelUi = Fingerprint(target, token, includeFocus: false);
                        InputGuard(); WindowInput.Wheel(element.Current.BoundingRectangle, target.Window.Handle, action.Amount, InputGuard);
                        verification = () => Fingerprint(target, token, includeFocus: false) != oldWheelUi;
                        before = "Bounded pane state"; after = "Observable pane state changed"; break;
                    }
                    var scroll = (ScrollPattern)scrollObject; var oldPosition = scroll.Current.VerticalScrollPercent;
                    if (!scroll.Current.VerticallyScrollable) throw new UiObservationException("NotScrollable");
                    before = oldPosition.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    InputGuard(); scroll.Scroll(ScrollAmount.NoAmount, action.Amount < 0 ? ScrollAmount.SmallDecrement : ScrollAmount.SmallIncrement);
                    verification = () => { after = scroll.Current.VerticalScrollPercent.ToString(System.Globalization.CultureInfo.InvariantCulture); return scroll.Current.VerticalScrollPercent != oldPosition; }; break;
                case ComputerActionType.Invoke:
                case ComputerActionType.ClickElement:
                    var oldUi = Fingerprint(target, token, includeFocus: false);
                    if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invocation)) { InputGuard(); ((InvokePattern)invocation).Invoke(); }
                    else if (action.Type == ComputerActionType.ClickElement) { InputGuard(); WindowInput.Click(element.Current.BoundingRectangle, target.Window.Handle, InputGuard); }
                    else throw new UiObservationException("UnsupportedPattern");
                    verification = () => Fingerprint(target, token, includeFocus: false) != oldUi;
                    before = "Bounded control state"; after = "Observable control state changed (not proof of the user's whole task)";
                    break;
                case ComputerActionType.TypeText:
                    RequireEditable(element);
                    if (action.Text is null) throw new ArgumentException("Text required");
                    InputGuard(); element.SetFocus();
                    if (!AutomationElement.FocusedElement.GetRuntimeId().SequenceEqual(target.RuntimeId)) throw new UiObservationException("WrongFocus");
                    WindowInput.Type(action.Text, () => { InputGuard(); if (!AutomationElement.FocusedElement.GetRuntimeId().SequenceEqual(target.RuntimeId)) throw new UiObservationException("WrongFocus"); });
                    verification = () => VerifyInsertedText(element, action.Text);
                    before = "Selected non-sensitive editor"; after = "Inserted text matches at caret (contents omitted)";
                    break;
                case ComputerActionType.PressKey:
                case ComputerActionType.PressShortcut:
                    if (action.Key is "Ctrl+A" or "Ctrl+C") RequireEditable(element);
                    InputGuard(); element.SetFocus();
                    var oldKeyUi = Fingerprint(target, token);
                    var clipboardSequence = action.Key == "Ctrl+C" ? WindowInput.GetClipboardSequenceNumber() : 0;
                    if (action.Key == "Ctrl+C" && !HasTextSelection(element)) throw new UiObservationException("NoVerifiedSelection");
                    WindowInput.Keys(action.Key ?? "", () => { InputGuard(); if (!AutomationElement.FocusedElement.GetRuntimeId().SequenceEqual(target.RuntimeId)) throw new UiObservationException("WrongFocus"); });
                    verification = action.Key switch
                    {
                        "Ctrl+A" => () => AllTextSelected(element),
                        "Ctrl+C" => () => WindowInput.GetClipboardSequenceNumber() != clipboardSequence,
                        "Alt+Tab" => () => (long)Native.GetForegroundWindow() != target.Window.Handle && Probe.Read((long)Native.GetForegroundWindow()) is { Title: not "[PROTECTED WINDOW]" },
                        _ => () => Fingerprint(target, token) != oldKeyUi
                    };
                    before = "Bounded control state"; after = "Observable control state changed (shortcut semantics may require further observation)"; break;
                default: throw new UiObservationException("UnsupportedAction");
            }
            ActionVerifying?.Invoke(this, EventArgs.Empty);
            if (verification is not null)
            {
                var watch = Stopwatch.StartNew();
                do { if (windowSwitch) Check(); else InputGuard(); if (verification()) { verified = true; break; } token.WaitHandle.WaitOne(25); }
                while (watch.Elapsed < TimeSpan.FromMilliseconds(750));
            }
            Invalidate();
            return new ComputerInteractionResult(action.Id, verified, verified ? "Verified" : "Unverified",
                verified ? "Verified " + action.Type + " in " + target.Window.ApplicationName + "." :
                "Dispatched " + action.Type + " in " + target.Window.ApplicationName + "; its effect is unverified. Stopped to prevent blind retries.",
                target.Window.ApplicationName, before, after, verified, InputMayHaveOccurred: true);
        }, ct);
    public event EventHandler? ActionVerifying;
    private static bool VerifyInsertedText(AutomationElement element, string text)
    {
        if (text.Length == 0) return true;
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return false;
        var selections = ((TextPattern)pattern).GetSelection();
        if (selections.Length != 1) return false;
        var range = selections[0].Clone();
        range.MoveEndpointByRange(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End);
        range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -text.Length);
        // Only the short range immediately preceding the caret is compared locally, not exported or logged.
        return range.GetText(text.Length + 1) == text;
    }
    private static bool HasTextSelection(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return false;
        return ((TextPattern)pattern).GetSelection().Any(range => range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) != 0);
    }
    private static bool AllTextSelected(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return false;
        var text = (TextPattern)pattern; var ranges = text.GetSelection(); var document = text.DocumentRange;
        return ranges.Length == 1 && ranges[0].CompareEndpoints(TextPatternRangeEndpoint.Start, document, TextPatternRangeEndpoint.Start) == 0 &&
            ranges[0].CompareEndpoints(TextPatternRangeEndpoint.End, document, TextPatternRangeEndpoint.End) == 0;
    }
    private string Fingerprint(PreparedComputerTarget target, CancellationToken ct, bool includeFocus = true)
    {
        ct.ThrowIfCancellationRequested();
        var root = AutomationElement.FromHandle((nint)target.Window.Handle);
        var result = new System.Text.StringBuilder(); var count = 0;
        void Visit(AutomationElement element, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (++count > 200 || depth > 8) return;
            var c = element.Current;
            result.AppendJoin(',', element.GetRuntimeId()).Append('|').Append(c.ControlType.Id).Append('|').Append(includeFocus && c.HasKeyboardFocus).Append('|').Append(c.IsEnabled).Append('|').Append(c.IsOffscreen);
            var passwordKnownFalse = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true) is false;
            if (passwordKnownFalse && c.ControlType != ControlType.Edit && c.ControlType != ControlType.Document && c.ControlType != ControlType.Text) result.Append('|').Append(Safe(c.Name));
            // Calculator exposes arithmetic display through this well-known non-secret property. Never read arbitrary text controls.
            if ((target.Window.HostedProcessName ?? target.Window.ProcessName).ToLowerInvariant() is "calculatorapp" or "calculator" && c.AutomationId == "CalculatorResults" && passwordKnownFalse)
                result.Append('|').Append(Safe(c.Name));
            if (passwordKnownFalse && element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle)) result.Append('|').Append(((TogglePattern)toggle).Current.ToggleState);
            if (passwordKnownFalse && element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select)) result.Append('|').Append(((SelectionItemPattern)select).Current.IsSelected);
            if (!passwordKnownFalse && c.ControlType == ControlType.Edit) return;
            var child = TreeWalker.ControlViewWalker.GetFirstChild(element);
            while (child is not null && count < 200) { Visit(child, depth + 1); child = TreeWalker.ControlViewWalker.GetNextSibling(child); }
        }
        Visit(root, 0);
        // Internal comparison only. UI labels/text never enter result metadata or persistent logs.
        return result.ToString();
    }
    private static T Pattern<T>(AutomationElement element, AutomationPattern pattern) where T : class =>
        element.TryGetCurrentPattern(pattern, out var value) ? (T)value : throw new UiObservationException("UnsupportedPattern");
    private static void RequireEditable(AutomationElement element)
    {
        var c = element.Current;
        if (c.IsPassword || c.ControlType != ControlType.Edit && c.ControlType != ControlType.Document) throw new UiObservationException("NotEditable");
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && ((ValuePattern)value).Current.IsReadOnly) throw new UiObservationException("NotEditable");
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out _) && !element.TryGetCurrentPattern(TextPattern.Pattern, out _)) throw new UiObservationException("NotEditable");
    }
}

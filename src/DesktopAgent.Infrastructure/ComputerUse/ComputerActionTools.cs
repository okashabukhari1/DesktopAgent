using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.ComputerUse;
namespace DesktopAgent.Infrastructure.ComputerUse;
public static class ComputerActionTools
{
    private static readonly Dictionary<string, ComputerActionType> Actions = new()
    {
        ["invoke_ui_element"] = ComputerActionType.Invoke, ["set_ui_value"] = ComputerActionType.SetValue,
        ["toggle_ui_element"] = ComputerActionType.Toggle, ["select_ui_element"] = ComputerActionType.Select,
        ["focus_ui_element"] = ComputerActionType.Focus, ["scroll_ui"] = ComputerActionType.ScrollElement,
        ["click_ui_element"] = ComputerActionType.ClickElement, ["type_text"] = ComputerActionType.TypeText,
        ["press_key"] = ComputerActionType.PressKey, ["press_shortcut"] = ComputerActionType.PressShortcut,
        ["expand_ui_element"] = ComputerActionType.Expand, ["collapse_ui_element"] = ComputerActionType.Collapse,
        ["scroll_window"] = ComputerActionType.ScrollWindow
    };
    public static bool IsAction(string name) => Actions.ContainsKey(name);
    public static IEnumerable<IAgentTool> Create(IComputerActionExecutor executor) => Actions.Select(x => new ActionTool(x.Key, x.Value, executor));
    private static JsonElement Schema(ComputerActionType type)
    {
        var textAction = type is ComputerActionType.SetValue or ComputerActionType.TypeText;
        var keyAction = type is ComputerActionType.PressKey or ComputerActionType.PressShortcut;
        var keys = new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "+", "-", "*", "/", ".", "=", "Enter", "Escape", "Tab", "Shift+Tab", "Alt+Tab", "Ctrl+A", "Ctrl+C", "Ctrl+V", "Ctrl+F", "Ctrl+S", "Left", "Up", "Right", "Down", "PageUp", "PageDown" };
        var properties = new JsonObject {
            ["window"] = new JsonObject { ["type"] = "string", ["maxLength"] = 128, ["pattern"] = "^window:[^\\r\\n]{1,121}$" },
            ["snapshot"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$" },
            ["element"] = new JsonObject { ["type"] = "string", ["maxLength"] = 48, ["pattern"] = "^ui:[1-9][0-9]*$" },
            ["text"] = new JsonObject { ["type"] = textAction ? "string" : "null" },
            ["key"] = new JsonObject { ["type"] = keyAction ? "string" : "null" },
            ["amount"] = new JsonObject { ["type"] = "integer", ["enum"] = type is ComputerActionType.ScrollElement or ComputerActionType.ScrollWindow ? new JsonArray(-1, 1) : new JsonArray(0) }
        };
        if (textAction) properties["text"]!["maxLength"] = 2000;
        if (keyAction) properties["key"]!["enum"] = new JsonArray(keys.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray());
        if (keyAction || type is ComputerActionType.ScrollWindow or ComputerActionType.TypeText)
        {
            properties["snapshot"]!["type"] = new JsonArray("string", "null");
            properties["element"]!["type"] = new JsonArray("string", "null");
        }
        return JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["properties"] = properties,
            ["required"] = new JsonArray("window", "snapshot", "element", "text", "key", "amount"), ["additionalProperties"] = false });
    }
    private sealed class ActionTool(string name, ComputerActionType type, IComputerActionExecutor executor) : IAgentTool
    {
        public AiToolDefinition Definition { get; } = new(name,
            "Controlled Windows action using window:ApplicationName, observed snapshot GUID and ui:N reference; never raw coordinates or handles. Revalidates identity, sensitivity, policy and target locally. UIA first. Unverified means stop, never retry. Text only for known non-password editable fields. Scroll amount -1/up or 1/down. Only explicit user computer tasks." +
            (type == ComputerActionType.TypeText ? " For Notepad, REQUIRED target: the observed Text editor with ControlType Document or Edit. Get it using inspect_window with window_reference=window:Notepad and control_type=Document. Do not target a root window, toolbar, tab, button or menu. Never guess its ui:N ID. Chrome typing supports ONLY the browser-owned Find box after verified Ctrl+F; use element=null and snapshot=null, and the worker independently resolves and validates that field. Arbitrary page forms/address bars are unsupported." : "") +
            (type is ComputerActionType.PressKey or ComputerActionType.PressShortcut or ComputerActionType.ScrollWindow ? " Window actions resolve a fresh target window. Pass element=null; snapshot may be null for a unique window, or a returned snapshot to pin the intended window. Chrome scrolling uses scroll_window, amount=1/down or -1/up (three small wheel increments). Never guess an element or demand further instructions for an explicit scroll request." : ""),
            Schema(type));
        public void Validate(JsonElement a)
        {
            if (a.ValueKind != JsonValueKind.Object) throw new ArgumentException("Action.RootMustBeObject");
            var properties = Definition.Parameters.GetProperty("properties");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in a.EnumerateObject())
            {
                if (!seen.Add(p.Name)) throw new ArgumentException("Action.DuplicateProperty");
                if (!properties.TryGetProperty(p.Name, out var rule)) throw new ArgumentException("Action.UnexpectedProperty");
                var typeRule = rule.GetProperty("type");
                if (p.Value.ValueKind == JsonValueKind.Null && typeRule.ValueKind == JsonValueKind.Array &&
                    typeRule.EnumerateArray().Any(x => x.GetString() == "null")) continue;
                var expected = typeRule.ValueKind == JsonValueKind.Array ? "string" : typeRule.GetString();
                var validKind = expected switch { "string" => p.Value.ValueKind == JsonValueKind.String, "null" => p.Value.ValueKind == JsonValueKind.Null,
                    "integer" => p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out _), _ => false };
                if (!validKind) throw new ArgumentException("Action.ValueKindMismatch:" + p.Name);
                if (rule.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(v => JsonElement.DeepEquals(v, p.Value)))
                    throw new ArgumentException("Action.EnumMismatch:" + p.Name);
                if (p.Value.ValueKind != JsonValueKind.String) continue;
                var value = p.Value.GetString()!;
                if (rule.TryGetProperty("maxLength", out var max) && value.Length > max.GetInt32()) throw new ArgumentException("Action.StringTooLong:" + p.Name);
                if (rule.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(value, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    throw new ArgumentException("Action.PatternMismatch:" + p.Name);
            }
            foreach (var required in Definition.Parameters.GetProperty("required").EnumerateArray())
                if (!seen.Contains(required.GetString()!)) throw new ArgumentException("Action.MissingRequiredProperty:" + required.GetString());
        }
        public Task<IReadOnlyList<string>> RequiredCapabilitiesAsync(JsonElement a, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public string? Target(JsonElement a) => "Verified application control";
        public async Task<ToolResult> ExecuteAsync(JsonElement a, CancellationToken ct = default)
        {
            Validate(a);
            var action = new ComputerAction(Guid.NewGuid(), type, a.GetProperty("window").GetString()!,
                a.GetProperty("snapshot").ValueKind == JsonValueKind.Null ? Guid.Empty : Guid.Parse(a.GetProperty("snapshot").GetString()!),
                a.GetProperty("element").GetString() ?? "", a.GetProperty("text").GetString(), a.GetProperty("key").GetString(), a.GetProperty("amount").GetInt32(), DateTimeOffset.UtcNow);
            var r = await executor.ExecuteAsync(action, ct);
            return new(r.Success, r.Status, r.Message, r, r.ErrorCode, r.DurationMilliseconds, r.Target);
        }
    }
}

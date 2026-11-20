using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DesktopAgent.Infrastructure.UiAwareness;

// The exposed schema is also the runtime validator's source of truth.
public static class UiReadContract
{
    public static JsonElement Schema(string tool)
    {
        var properties = new JsonObject();
        if (tool is not ("get_foreground_window" or "list_open_windows"))
        {
            properties["window_reference"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["maxLength"] = 128,
                ["pattern"] = "^window:[^\\r\\n]{1,121}$", ["description"] = "Optional logical window reference, e.g. window:Calculator. Omit or null for the remembered external window." };
            foreach (var key in new[] { "query", "control_type" })
                properties[key] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["maxLength"] = 128 };
        }
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(), ["additionalProperties"] = false };
        // The live provider rejects top-level combinators in function parameters, even with strict=false.
        // A real search needs one nonblank query; control_type remains an optional filter.
        if (tool == "find_ui_element")
        {
            properties["query"] = new JsonObject { ["type"] = "string", ["maxLength"] = 128, ["pattern"] = "\\S" };
            schema["required"] = new JsonArray("query");
        }
        return JsonSerializer.SerializeToElement(schema);
    }
    public static void Validate(JsonElement schema, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object) throw new ArgumentException("UI.RootMustBeObject");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var properties = schema.GetProperty("properties");
        foreach (var p in arguments.EnumerateObject())
        {
            if (!seen.Add(p.Name)) throw new ArgumentException("UI.DuplicateProperty");
            if (!properties.TryGetProperty(p.Name, out var rule)) throw new ArgumentException("UI.UnexpectedProperty");
            ValidateValue(rule, p.Value, p.Name);
        }
        foreach (var required in schema.GetProperty("required").EnumerateArray())
            if (!arguments.TryGetProperty(required.GetString()!, out _)) throw new ArgumentException("UI.MissingRequiredProperty");
    }
    private static void ValidateValue(JsonElement rule, JsonElement value, string name)
    {
        var kind = value.ValueKind switch { JsonValueKind.String => "string", JsonValueKind.Null => "null", _ => "other" };
        var type = rule.GetProperty("type");
        if (!(type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Any(t => t.GetString() == kind) : type.GetString() == kind))
            throw new ArgumentException("UI.ValueKindMismatch:" + name);
        if (value.ValueKind != JsonValueKind.String) return;
        var text = value.GetString()!;
        if (rule.TryGetProperty("maxLength", out var max) && text.Length > max.GetInt32()) throw new ArgumentException("UI.StringTooLong:" + name);
        if (rule.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(text, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException("UI.PatternMismatch:" + name);
    }
    public static string? Optional(JsonElement arguments, string name) => arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

using System.Text;
using System.Text.Json.Nodes;

namespace Tyco.CSharp;

public enum TycoValueKind
{
    Null,
    Bool,
    Int,
    Float,
    String,
    Date,
    Time,
    DateTime,
    Array,
    Instance,
    Reference,
}

public sealed class TycoString
{
    public string Value { get; set; }
    public bool HasTemplate { get; set; }
    public bool IsLiteral { get; }

    public TycoString(string value, bool hasTemplate, bool isLiteral)
    {
        Value = value;
        HasTemplate = hasTemplate;
        IsLiteral = isLiteral;
    }

    public TycoString Clone() => new(Value, HasTemplate, IsLiteral);

    public void Render(TycoContext context, TycoInstance? current)
    {
        if (!HasTemplate || IsLiteral)
        {
            return;
        }

        var runes = Value.ToCharArray();
        var builder = new StringBuilder();
        for (var idx = 0; idx < runes.Length; idx++)
        {
            var ch = runes[idx];
            if (ch != '{')
            {
                builder.Append(ch);
                continue;
            }
            var closing = idx + 1;
            var placeholder = new StringBuilder();
            while (closing < runes.Length && runes[closing] != '}')
            {
                placeholder.Append(runes[closing]);
                closing++;
            }
            if (closing < runes.Length && runes[closing] == '}')
            {
                var resolved = TemplateResolver.Resolve(placeholder.ToString(), context, current);
                if (resolved != null)
                {
                    builder.Append(resolved);
                }
                else
                {
                    builder.Append('{').Append(placeholder).Append('}');
                }
                idx = closing;
            }
            else
            {
                builder.Append(ch);
            }
        }

        var rendered = builder.ToString();
        var decoded = Utilities.UnescapeBasicString(rendered);
        Value = decoded;
        HasTemplate = false;
    }
}

public sealed class TycoReference
{
    public string StructName { get; }
    public string PrimaryKey { get; }
    public TycoInstance? Resolved { get; set; }

    public TycoReference(string structName, string primaryKey)
    {
        StructName = structName;
        PrimaryKey = primaryKey;
    }

    public TycoReference Clone() => new(StructName, PrimaryKey)
    {
        Resolved = Resolved?.Clone(),
    };
}

public sealed class TycoInstance
{
    public string StructName { get; }
    private readonly Dictionary<string, TycoValue> _fields = new();
    private readonly List<string> _fieldOrder = new();

    public TycoInstance(string structName)
    {
        StructName = structName;
    }

    public TycoInstance Clone()
    {
        var clone = new TycoInstance(StructName);
        foreach (var key in _fieldOrder)
        {
            if (_fields.TryGetValue(key, out var value))
            {
                clone.SetAttribute(key, value.Clone());
            }
        }
        return clone;
    }

    public void SetAttribute(string name, TycoValue value)
    {
        if (!_fields.ContainsKey(name))
        {
            _fieldOrder.Add(name);
        }
        _fields[name] = value;
    }

    public TycoValue? GetAttribute(string name) => _fields.TryGetValue(name, out var value) ? value : null;

    public TycoValue? RemoveAttribute(string name)
    {
        if (!_fields.Remove(name, out var existing))
        {
            return null;
        }
        _fieldOrder.Remove(name);
        return existing;
    }

    public IReadOnlyList<string> FieldOrder => _fieldOrder;
    public IReadOnlyDictionary<string, TycoValue> Attributes => _fields;

    public void EnforceOrderFromSchema(IEnumerable<FieldSchema> fields)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>();
        foreach (var field in fields)
        {
            if (_fields.ContainsKey(field.Name))
            {
                ordered.Add(field.Name);
                seen.Add(field.Name);
            }
        }
        foreach (var key in _fieldOrder)
        {
            if (!seen.Contains(key))
            {
                ordered.Add(key);
            }
        }
        _fieldOrder.Clear();
        _fieldOrder.AddRange(ordered);
    }

    public JsonNode ToJsonNode()
    {
        var obj = new JsonObject();
        foreach (var key in _fieldOrder)
        {
            if (_fields.TryGetValue(key, out var value))
            {
                obj[key] = value.ToJsonNode();
            }
        }
        return obj;
    }
}

public sealed class TycoValue
{
    public TycoValueKind Kind { get; }
    public bool BoolValue { get; }
    public long IntValue { get; }
    public double FloatValue { get; }
    public TycoString? StringValue { get; }
    public List<TycoValue>? ArrayValue { get; }
    public TycoInstance? InstanceValue { get; }
    public TycoReference? ReferenceValue { get; }

    private TycoValue(
        TycoValueKind kind,
        bool boolValue = default,
        long intValue = default,
        double floatValue = default,
        TycoString? stringValue = null,
        List<TycoValue>? arrayValue = null,
        TycoInstance? instanceValue = null,
        TycoReference? referenceValue = null)
    {
        Kind = kind;
        BoolValue = boolValue;
        IntValue = intValue;
        FloatValue = floatValue;
        StringValue = stringValue;
        ArrayValue = arrayValue;
        InstanceValue = instanceValue;
        ReferenceValue = referenceValue;
    }

    public static TycoValue Null() => new(TycoValueKind.Null);
    public static TycoValue Bool(bool value) => new(TycoValueKind.Bool, boolValue: value);
    public static TycoValue Int(long value) => new(TycoValueKind.Int, intValue: value);
    public static TycoValue Float(double value) => new(TycoValueKind.Float, floatValue: value);
    public static TycoValue String(TycoString value) => new(TycoValueKind.String, stringValue: value);
    public static TycoValue Date(string value) => new(TycoValueKind.Date, stringValue: new TycoString(value, false, false));
    public static TycoValue Time(string value) => new(TycoValueKind.Time, stringValue: new TycoString(value, false, false));
    public static TycoValue DateTime(string value) => new(TycoValueKind.DateTime, stringValue: new TycoString(value, false, false));
    public static TycoValue Array(List<TycoValue> items) => new(TycoValueKind.Array, arrayValue: items);
    public static TycoValue Instance(TycoInstance instance) => new(TycoValueKind.Instance, instanceValue: instance);
    public static TycoValue Reference(TycoReference reference) => new(TycoValueKind.Reference, referenceValue: reference);

    public TycoValue Clone() => Kind switch
    {
        TycoValueKind.Null => Null(),
        TycoValueKind.Bool => Bool(BoolValue),
        TycoValueKind.Int => Int(IntValue),
        TycoValueKind.Float => Float(FloatValue),
        TycoValueKind.String or TycoValueKind.Date or TycoValueKind.Time or TycoValueKind.DateTime
            => StringValue != null ? String(StringValue.Clone()) : Null(),
        TycoValueKind.Array => Array(new List<TycoValue>(ArrayValue?.Select(v => v.Clone()) ?? System.Array.Empty<TycoValue>())),
        TycoValueKind.Instance => InstanceValue != null ? Instance(InstanceValue.Clone()) : Null(),
        TycoValueKind.Reference => ReferenceValue != null ? Reference(ReferenceValue.Clone()) : Null(),
        _ => Null(),
    };

    public string ToTemplateText() => Kind switch
    {
        TycoValueKind.Null => "null",
        TycoValueKind.Bool => BoolValue ? "true" : "false",
        TycoValueKind.Int => IntValue.ToString(),
        TycoValueKind.Float => FloatValue.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
        TycoValueKind.String or TycoValueKind.Date or TycoValueKind.Time or TycoValueKind.DateTime => StringValue?.Value ?? string.Empty,
        TycoValueKind.Reference => ReferenceValue?.PrimaryKey ?? string.Empty,
        _ => string.Empty,
    };

    public void RenderTemplates(TycoContext context, TycoInstance? current)
    {
        switch (Kind)
        {
            case TycoValueKind.String:
                StringValue?.Render(context, current);
                break;
            case TycoValueKind.Array:
                if (ArrayValue != null)
                {
                    foreach (var item in ArrayValue)
                    {
                        item.RenderTemplates(context, current);
                    }
                }
                break;
            case TycoValueKind.Instance:
                if (InstanceValue == null)
                {
                    break;
                }
                var snapshot = InstanceValue.Clone();
                foreach (var key in InstanceValue.FieldOrder)
                {
                    if (InstanceValue.Attributes.TryGetValue(key, out var value))
                    {
                        value.RenderTemplates(context, snapshot);
                    }
                    snapshot = InstanceValue.Clone();
                }
                break;
        }
    }

    public JsonNode? ToJsonNode() => Kind switch
    {
        TycoValueKind.Null => null,
        TycoValueKind.Bool => BoolValue,
        TycoValueKind.Int => IntValue,
        TycoValueKind.Float => FloatValue,
        TycoValueKind.String or TycoValueKind.Date or TycoValueKind.Time or TycoValueKind.DateTime => StringValue?.Value,
        TycoValueKind.Array => BuildJsonArray(ArrayValue),
        TycoValueKind.Instance => InstanceValue?.ToJsonNode(),
        TycoValueKind.Reference => ReferenceValue?.Resolved?.ToJsonNode(),
        _ => null,
    };

    private static JsonArray BuildJsonArray(IEnumerable<TycoValue>? items)
    {
        var array = new JsonArray();
        if (items == null)
        {
            return array;
        }
        foreach (var node in items)
        {
            array.Add(node.ToJsonNode());
        }
        return array;
    }
}

internal static class TemplateResolver
{
    public static string? Resolve(string placeholder, TycoContext context, TycoInstance? current)
    {
        if (string.IsNullOrWhiteSpace(placeholder))
        {
            return null;
        }
        var segments = placeholder.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }
        var fromGlobal = placeholder.StartsWith("global.", StringComparison.Ordinal);
        var startIdx = fromGlobal ? 1 : 0;

        TycoValue? value;
        if (fromGlobal)
        {
            if (segments.Length < 2)
            {
                return null;
            }
            value = context.GetGlobal(segments[1]);
        }
        else if (current != null)
        {
            value = current.GetAttribute(segments[0]) ?? context.GetGlobal(segments[0]);
        }
        else
        {
            value = context.GetGlobal(segments[0]);
        }

        if (value == null)
        {
            return null;
        }

        for (var idx = startIdx + 1; idx < segments.Length; idx++)
        {
            if (value.Kind == TycoValueKind.Instance && value.InstanceValue != null)
            {
                value = value.InstanceValue.GetAttribute(segments[idx]);
            }
            else if (value.Kind == TycoValueKind.Reference && value.ReferenceValue?.Resolved != null)
            {
                value = value.ReferenceValue.Resolved.GetAttribute(segments[idx]);
            }
            else
            {
                return null;
            }

            if (value == null)
            {
                return null;
            }
        }

        return value.ToTemplateText();
    }
}

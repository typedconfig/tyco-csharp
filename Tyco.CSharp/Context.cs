using System.Globalization;
using System.Text.Json.Nodes;

namespace Tyco.CSharp;

public sealed class FieldSchema
{
    public string Name { get; }
    public string TypeName { get; }
    public bool IsPrimaryKey { get; set; }
    public bool IsNullable { get; set; }
    public bool IsArray { get; set; }
    public TycoValue? DefaultValue { get; set; }

    public FieldSchema(string name, string typeName)
    {
        Name = name;
        TypeName = typeName;
    }

    public FieldSchema Clone() => new(Name, TypeName)
    {
        IsPrimaryKey = IsPrimaryKey,
        IsNullable = IsNullable,
        IsArray = IsArray,
        DefaultValue = DefaultValue?.Clone(),
    };
}

public sealed class TycoStruct
{
    public string Name { get; }
    private readonly List<FieldSchema> _fields = new();
    private readonly List<TycoInstance> _instances = new();
    private readonly Dictionary<string, TycoInstance> _pkIndex = new();
    private string? _primaryKey;

    public TycoStruct(string name)
    {
        Name = name;
    }

    public TycoStruct Clone()
    {
        var clone = new TycoStruct(Name)
        {
            _primaryKey = _primaryKey
        };
        clone._fields.AddRange(_fields.Select(f => f.Clone()));
        clone._instances.AddRange(_instances.Select(i => i.Clone()));
        foreach (var kvp in _pkIndex)
        {
            clone._pkIndex[kvp.Key] = kvp.Value.Clone();
        }
        return clone;
    }

    public IReadOnlyList<FieldSchema> Fields => _fields;
    public IReadOnlyList<TycoInstance> Instances => _instances;
    public bool HasPrimaryKey => _primaryKey != null;
    public string? PrimaryKeyField => _primaryKey;

    public void AddField(FieldSchema field)
    {
        if (field.IsPrimaryKey)
        {
            _primaryKey = field.Name;
        }
        _fields.Add(field);
    }

    public void AddInstance(TycoInstance instance) => _instances.Add(instance);

    public void ReplaceInstance(int index, TycoInstance instance)
    {
        if (index < _instances.Count)
        {
            _instances[index] = instance;
        }
        else
        {
            _instances.Add(instance);
        }
    }

    public void SetDefault(string fieldName, TycoValue? value)
    {
        var field = _fields.FirstOrDefault(f => f.Name == fieldName)
            ?? throw new TycoParseException($"Unknown field '{fieldName}'");
        field.DefaultValue = value?.Clone();
    }

    public void BuildPrimaryIndex()
    {
        _pkIndex.Clear();
        if (_primaryKey == null)
        {
            return;
        }
        foreach (var instance in _instances)
        {
            var value = instance.GetAttribute(_primaryKey);
            if (value != null)
            {
                _pkIndex[value.ToTemplateText()] = instance.Clone();
            }
        }
    }

    public TycoInstance? FindByPrimaryKey(string key) =>
        _pkIndex.TryGetValue(key, out var instance) ? instance : null;
}

public sealed class TycoContext
{
    private readonly Dictionary<string, TycoValue> _globals = new();
    private readonly Dictionary<string, TycoStruct> _structs = new();

    public void SetGlobal(string name, TycoValue value) => _globals[name] = value;
    public TycoValue? GetGlobal(string name) => _globals.TryGetValue(name, out var value) ? value : null;
    public IReadOnlyDictionary<string, TycoValue> Globals => _globals;

    public TycoStruct EnsureStruct(string name)
    {
        if (!_structs.TryGetValue(name, out var value))
        {
            value = new TycoStruct(name);
            _structs[name] = value;
        }
        return value;
    }

    public TycoStruct? GetStruct(string name) => _structs.TryGetValue(name, out var value) ? value : null;
    public IReadOnlyDictionary<string, TycoStruct> Structs => _structs;

    public void Render()
    {
        ResolveInlineInstances();
        foreach (var tycoStruct in _structs.Values)
        {
            tycoStruct.BuildPrimaryIndex();
        }
        ResolveReferences();
        RenderTemplates();
    }

    private void ResolveInlineInstances()
    {
        var snapshot = _structs.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone());

        TycoValue CoerceValue(TycoValue value, FieldSchema schema)
        {
            if (schema.IsArray || value.Kind != TycoValueKind.String || value.StringValue == null)
            {
                return value;
            }
            var literal = value.StringValue.Value;
            return schema.TypeName switch
            {
                "int" => TycoValue.Int(Utilities.ParseInteger(literal)),
                "float" => TycoValue.Float(double.Parse(literal, CultureInfo.InvariantCulture)),
                "bool" => TycoValue.Bool(string.Equals(literal, "true", StringComparison.OrdinalIgnoreCase)),
                _ => value,
            };
        }

        void ResolveValue(TycoValue value)
        {
            switch (value.Kind)
            {
                case TycoValueKind.Array when value.ArrayValue != null:
                    foreach (var nested in value.ArrayValue)
                    {
                        ResolveValue(nested);
                    }
                    break;
                case TycoValueKind.Instance when value.InstanceValue != null:
                    ApplySchema(value.InstanceValue, snapshot);
                    break;
            }
        }

        void ApplySchema(TycoInstance instance, Dictionary<string, TycoStruct> schemas)
        {
            if (!schemas.TryGetValue(instance.StructName, out var schema))
            {
                return;
            }

            var placeholders = instance.Attributes
                .Where(kvp => kvp.Key.StartsWith("_arg", StringComparison.Ordinal))
                .Select(kvp => (Field: kvp.Key, Index: int.Parse(kvp.Key[4..])))
                .OrderBy(tuple => tuple.Index)
                .ToList();

            foreach (var placeholder in placeholders)
            {
                if (placeholder.Index < schema.Fields.Count)
                {
                    var fieldSchema = schema.Fields[placeholder.Index];
                    var value = instance.RemoveAttribute(placeholder.Field);
                    if (value != null)
                    {
                        instance.SetAttribute(fieldSchema.Name, CoerceValue(value, fieldSchema));
                    }
                }
            }

            foreach (var field in schema.Fields)
            {
                var value = instance.RemoveAttribute(field.Name);
                if (value != null)
                {
                    instance.SetAttribute(field.Name, CoerceValue(value, field));
                }
                else if (field.DefaultValue != null)
                {
                    instance.SetAttribute(field.Name, field.DefaultValue.Clone());
                }
            }

            instance.EnforceOrderFromSchema(schema.Fields);

            foreach (var value in instance.Attributes.Values)
            {
                ResolveValue(value);
            }
        }

        foreach (var (key, value) in _globals)
        {
            ResolveValue(value);
            _globals[key] = value;
        }

        foreach (var (name, tycoStruct) in _structs)
        {
            foreach (var instance in tycoStruct.Instances)
            {
                ApplySchema(instance, snapshot);
            }
        }
    }

    private void ResolveReferences()
    {
        var snapshot = _structs.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone());

        void Visit(TycoValue value)
        {
            switch (value.Kind)
            {
                case TycoValueKind.Reference when value.ReferenceValue != null:
                    var reference = value.ReferenceValue;
                    if (!snapshot.TryGetValue(reference.StructName, out var def))
                    {
                        throw new TycoParseException($"Unknown struct '{reference.StructName}'");
                    }
                    var instance = def.FindByPrimaryKey(reference.PrimaryKey)
                        ?? throw new TycoParseException($"Unknown {reference.StructName}({reference.PrimaryKey})");
                    reference.Resolved = instance.Clone();
                    break;
                case TycoValueKind.Array when value.ArrayValue != null:
                    foreach (var item in value.ArrayValue)
                    {
                        Visit(item);
                    }
                    break;
                case TycoValueKind.Instance when value.InstanceValue != null:
                    foreach (var nested in value.InstanceValue.Attributes.Values)
                    {
                        Visit(nested);
                    }
                    break;
            }
        }

        foreach (var value in _globals.Values)
        {
            Visit(value);
        }
        foreach (var tycoStruct in _structs.Values)
        {
            foreach (var instance in tycoStruct.Instances)
            {
                foreach (var value in instance.Attributes.Values)
                {
                    Visit(value);
                }
            }
        }
    }

    private void RenderTemplates()
    {
        var snapshot = Clone();
        foreach (var (key, value) in _globals)
        {
            value.RenderTemplates(snapshot, null);
            snapshot._globals[key] = value.Clone();
        }

        foreach (var (name, tycoStruct) in _structs)
        {
            var shadow = snapshot._structs[name];
            for (var idx = 0; idx < tycoStruct.Instances.Count; idx++)
            {
                var instance = tycoStruct.Instances[idx];
                var instanceSnapshot = instance.Clone();
                foreach (var fieldName in instance.FieldOrder)
                {
                    if (instance.Attributes.TryGetValue(fieldName, out var value))
                    {
                        value.RenderTemplates(snapshot, instanceSnapshot);
                        instanceSnapshot = instance.Clone();
                    }
                }
                shadow.ReplaceInstance(idx, instance.Clone());
            }
        }
    }

    private TycoContext Clone()
    {
        var clone = new TycoContext();
        foreach (var (key, value) in _globals)
        {
            clone._globals[key] = value.Clone();
        }
        foreach (var (key, value) in _structs)
        {
            clone._structs[key] = value.Clone();
        }
        return clone;
    }

    public JsonObject ToObject()
    {
        var obj = new JsonObject();
        foreach (var (key, value) in _globals)
        {
            obj[key] = value.ToJsonNode();
        }
        foreach (var (name, tycoStruct) in _structs)
        {
            if (!tycoStruct.HasPrimaryKey)
            {
                continue;
            }
            var array = new JsonArray();
            foreach (var instance in tycoStruct.Instances)
            {
                array.Add(instance.ToJsonNode());
            }
            obj[name] = array;
        }
        return obj;
    }

    public JsonObject ToJson() => ToObject();
}

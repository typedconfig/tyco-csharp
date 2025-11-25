using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tyco.CSharp;

public sealed record SourceLine(string Text, string? Path, int LineNumber)
{
    public SourceSpan Span => new(Path, LineNumber, 1, Text);
}

public sealed class TycoParser
{
    private const string AttrNamePattern = @"[a-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)*";
    private static readonly Regex StructDefRegex = new(@"^([A-Z][A-Za-z0-9_]*)\s*:$", RegexOptions.Compiled);
    private static readonly Regex FieldRegex = new(@"^\s*([*?])?([A-Za-z][A-Za-z0-9_]*)(\[\])?\s+(" + AttrNamePattern + @")\s*:(?:\s+(.*))?$", RegexOptions.Compiled);
    private static readonly Regex DefaultUpdateRegex = new(@"^\s+(" + AttrNamePattern + @")\s*:(?:\s+(.*))?$", RegexOptions.Compiled);
    private static readonly Regex StructCallRegex = new(@"^([A-Za-z][A-Za-z0-9_]*)\((.*)\)$", RegexOptions.Compiled);
    private static readonly string[] ScalarTypeNames =
    {
        "bool",
        "int",
        "float",
        "str",
        "date",
        "time",
        "datetime",
    };
    private static readonly HashSet<string> ScalarTypes = new(ScalarTypeNames, StringComparer.Ordinal);

    private readonly HashSet<string> _included = new(StringComparer.OrdinalIgnoreCase);

    public TycoContext ParseFile(string path)
    {
        var lines = ReadFileWithIncludes(path);
        return ParseLines(lines);
    }

    public TycoContext ParseString(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n')
            .Select((text, idx) => new SourceLine(text, null, idx + 1))
            .ToList();
        return ParseLines(lines);
    }

    private List<SourceLine> ReadFileWithIncludes(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (_included.Contains(fullPath))
        {
            return new List<SourceLine>();
        }
        _included.Add(fullPath);

        var content = File.ReadAllText(fullPath);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        var result = new List<SourceLine>();
        var parent = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();

        for (var idx = 0; idx < lines.Length; idx++)
        {
            var text = lines[idx];
            var line = new SourceLine(text, fullPath, idx + 1);
            var trimmed = text.Trim();
            if (trimmed.StartsWith("#include", StringComparison.Ordinal))
            {
                var include = trimmed.Substring("#include".Length).Trim().Trim('"', '\'');
                var includePath = Path.Combine(parent, include);
                var nested = ReadFileWithIncludes(includePath);
                result.AddRange(nested);
            }
            else
            {
                result.Add(line);
            }
        }
        return result;
    }

    private TycoContext ParseLines(IReadOnlyList<SourceLine> lines)
    {
        var context = new TycoContext();
        var currentStruct = string.Empty;
        var instanceLines = new List<string>();

        for (var idx = 0; idx < lines.Count; idx++)
        {
            var line = lines[idx];
            var trimmed = Utilities.StripInlineComment(line.Text);
            var trimmedWs = trimmed.Trim();
            if (trimmedWs.Length == 0)
            {
                continue;
            }

            var structMatch = StructDefRegex.Match(trimmedWs);
            if (structMatch.Success)
            {
                if (!string.IsNullOrEmpty(currentStruct) && instanceLines.Count > 0)
                {
                    ParseStructInstances(currentStruct, instanceLines, context);
                    instanceLines.Clear();
                }
                currentStruct = structMatch.Groups[1].Value;
                context.EnsureStruct(currentStruct);
                continue;
            }

            var fieldMatch = FieldRegex.Match(line.Text);
            if (fieldMatch.Success)
            {
                var modifier = fieldMatch.Groups[1].Value;
                var typeName = fieldMatch.Groups[2].Value;
                var isArray = fieldMatch.Groups[3].Success;
                var attrName = fieldMatch.Groups[4].Value;
                var valueStr = fieldMatch.Groups[5].Value;
                var lineSpan = line.Span;

                if (Utilities.HasUnclosedDelimiter(valueStr, "\"\"\"") || Utilities.HasUnclosedDelimiter(valueStr, "'''"))
                {
                    var delimiter = valueStr.Contains("'''", StringComparison.Ordinal) ? "'''" : "\"\"\"";
                    (idx, valueStr) = AccumulateMultiline(idx, lines, valueStr, delimiter);
                }

                valueStr = Utilities.StripInlineComment(valueStr);
                var trimmedDefault = valueStr.Trim();
                if (trimmedDefault.StartsWith("(", StringComparison.Ordinal) &&
                    Utilities.HasUnclosedParentheses(trimmedDefault))
                {
                    (idx, valueStr) = AccumulateEnumList(idx, lines, valueStr);
                    valueStr = Utilities.StripInlineComment(valueStr);
                    trimmedDefault = valueStr.Trim();
                }
                var isGlobal = !char.IsWhiteSpace(line.Text.FirstOrDefault());
                if (!isGlobal && string.IsNullOrEmpty(currentStruct))
                {
                    throw new TycoParseException("Struct field defined before struct header", lineSpan);
                }

                if (!isGlobal)
                {
                    var schema = new FieldSchema(attrName, typeName)
                    {
                        IsPrimaryKey = modifier == "*",
                        IsNullable = modifier == "?",
                        IsArray = isArray,
                    };
                    if (!string.IsNullOrWhiteSpace(trimmedDefault))
                    {
                        if (trimmedDefault.StartsWith("(", StringComparison.Ordinal))
                        {
                            if (isArray)
                            {
                                throw new TycoParseException("Enum constraints are only supported on scalar fields", lineSpan);
                            }
                            if (!IsScalarType(typeName))
                            {
                                throw new TycoParseException($"Can only set enum values on {string.Join(", ", ScalarTypeNames)}", lineSpan);
                            }
                            schema.EnumChoices = ParseEnumChoices(trimmedDefault, typeName, context, lineSpan);
                        }
                        else
                        {
                            var descriptor = FieldTypeDescriptor(typeName, isArray);
                            schema.DefaultValue = ParseValue(trimmedDefault, descriptor, context, lineSpan);
                        }
                    }
                    context.GetStruct(currentStruct)!.AddField(schema);
                }
                else
                {
                    var descriptor = FieldTypeDescriptor(typeName, isArray);
                    var value = ParseValue(trimmedDefault, descriptor, context, lineSpan);
                    context.SetGlobal(attrName, value);
                }
                continue;
            }

            var defaultMatch = DefaultUpdateRegex.Match(line.Text);
            if (defaultMatch.Success && !string.IsNullOrEmpty(currentStruct))
            {
                var fieldName = defaultMatch.Groups[1].Value;
                var valueStr = defaultMatch.Groups[2].Value;
                var lineSpan = line.Span;
                if (Utilities.HasUnclosedDelimiter(valueStr, "\"\"\"") || Utilities.HasUnclosedDelimiter(valueStr, "'''"))
                {
                    var delimiter = valueStr.Contains("'''", StringComparison.Ordinal) ? "'''" : "\"\"\"";
                    (idx, valueStr) = AccumulateMultiline(idx, lines, valueStr, delimiter);
                }
                valueStr = Utilities.StripInlineComment(valueStr);
                var trimmedDefault = valueStr.Trim();
                if (trimmedDefault.StartsWith("(", StringComparison.Ordinal) &&
                    Utilities.HasUnclosedParentheses(trimmedDefault))
                {
                    (idx, valueStr) = AccumulateEnumList(idx, lines, valueStr);
                    valueStr = Utilities.StripInlineComment(valueStr);
                    trimmedDefault = valueStr.Trim();
                }

                var structDef = context.GetStruct(currentStruct)
                    ?? throw new TycoParseException($"Unknown struct '{currentStruct}'", lineSpan);
                var schema = structDef.Fields.FirstOrDefault(f => f.Name == fieldName)
                    ?? throw new TycoParseException($"Unknown field '{fieldName}'", lineSpan);

                if (!string.IsNullOrWhiteSpace(trimmedDefault))
                {
                    if (trimmedDefault.StartsWith("(", StringComparison.Ordinal))
                    {
                        if (schema.IsArray)
                        {
                            throw new TycoParseException("Enum constraints are only supported on scalar fields", lineSpan);
                        }
                        if (!IsScalarType(schema.TypeName))
                        {
                            throw new TycoParseException($"Can only set enum values on {string.Join(", ", ScalarTypeNames)}", lineSpan);
                        }
                        var choices = ParseEnumChoices(trimmedDefault, schema.TypeName, context, lineSpan);
                        structDef.SetEnumChoices(fieldName, choices, lineSpan);
                    }
                    else
                    {
                        var descriptor = FieldTypeDescriptor(schema.TypeName, schema.IsArray);
                        var parsedValue = ParseValue(trimmedDefault, descriptor, context, lineSpan);
                        structDef.SetDefault(fieldName, parsedValue, lineSpan);
                    }
                }
                else
                {
                    structDef.SetDefault(fieldName, null, lineSpan);
                }
                continue;
            }

            if (trimmedWs.StartsWith("-", StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(currentStruct))
                {
                    throw new TycoParseException("Instance data encountered outside of a struct block", line.Span);
                }
                var instLine = trimmedWs.TrimStart('-').Trim();
                while (instLine.EndsWith("\\", StringComparison.Ordinal) && idx + 1 < lines.Count)
                {
                    instLine = instLine[..^1].TrimEnd();
                    idx++;
                    instLine += " " + Utilities.StripInlineComment(lines[idx].Text).Trim();
                }
                if (Utilities.HasUnclosedDelimiter(instLine, "\"\"\"") || Utilities.HasUnclosedDelimiter(instLine, "'''"))
                {
                    var delimiter = instLine.Contains("'''", StringComparison.Ordinal) ? "'''" : "\"\"\"";
                    (idx, instLine) = AccumulateMultiline(idx, lines, instLine, delimiter);
                }
                instanceLines.Add(instLine);
                continue;
            }

            if (InstanceContinuation(line, instanceLines))
            {
                continue;
            }
        }

        if (!string.IsNullOrEmpty(currentStruct) && instanceLines.Count > 0)
        {
            ParseStructInstances(currentStruct, instanceLines, context);
        }

        context.Render();
        return context;
    }

    private static bool InstanceContinuation(SourceLine line, List<string> instanceLines)
    {
        if (instanceLines.Count == 0)
        {
            return false;
        }
        if (!char.IsWhiteSpace(line.Text.FirstOrDefault()))
        {
            return false;
        }
        var continuation = Utilities.StripInlineComment(line.Text).Trim();
        if (continuation.Length > 0)
        {
            instanceLines[^1] += " " + continuation;
        }
        return true;
    }

    private static (int, string) AccumulateMultiline(int idx, IReadOnlyList<SourceLine> lines, string value, string delimiter)
    {
        var builder = new StringBuilder(value);
        var cursor = idx;
        while (cursor + 1 < lines.Count && Utilities.HasUnclosedDelimiter(builder.ToString(), delimiter))
        {
            cursor++;
            builder.Append('\n').Append(lines[cursor].Text);
        }
        if (Utilities.HasUnclosedDelimiter(builder.ToString(), delimiter))
        {
            throw new TycoParseException($"Unterminated {delimiter} string literal");
        }
        return (cursor, builder.ToString());
    }

    private static (int, string) AccumulateEnumList(int idx, IReadOnlyList<SourceLine> lines, string value)
    {
        var builder = new StringBuilder(value);
        var cursor = idx;
        while (cursor + 1 < lines.Count && Utilities.HasUnclosedParentheses(Utilities.StripInlineComment(builder.ToString())))
        {
            cursor++;
            builder.Append('\n').Append(lines[cursor].Text);
        }
        if (Utilities.HasUnclosedParentheses(Utilities.StripInlineComment(builder.ToString())))
        {
            throw new TycoParseException("Unterminated enum declaration");
        }
        return (cursor, builder.ToString());
    }

    private void ParseStructInstances(string structName, List<string> lines, TycoContext context)
    {
        var structDef = context.GetStruct(structName)
            ?? throw new TycoParseException($"Unknown struct '{structName}'");
        var fields = structDef.Fields;
        foreach (var raw in lines)
        {
            var parts = Utilities.SplitTopLevel(raw, ',');
            var instance = new TycoInstance(structName);
            var positional = 0;
            var usingNamed = false;
            var span = new SourceSpan(null, 0, 1, raw);

            foreach (var rawPart in parts)
            {
                var part = rawPart.Trim();
                if (part.Length == 0)
                {
                    continue;
                }
                if (SplitNamedArgument(part, out var name, out var value))
                {
                    usingNamed = true;
                    var fieldSchema = fields.FirstOrDefault(f => f.Name == name)
                        ?? throw new TycoParseException($"Unknown field '{name}' in {structName}", span);
                    var descriptor = FieldTypeDescriptor(fieldSchema.TypeName, fieldSchema.IsArray);
                    var typed = ParseValue(value, descriptor, context, span);
                    instance.SetAttribute(name, typed);
                }
                else
                {
                    if (usingNamed)
                    {
                        throw new TycoParseException("Positional arguments cannot follow named arguments", span);
                    }
                    if (positional >= fields.Count)
                    {
                        throw new TycoParseException($"Too many positional arguments for {structName}", span);
                    }
                    var fieldSchema = fields[positional];
                    var descriptor = FieldTypeDescriptor(fieldSchema.TypeName, fieldSchema.IsArray);
                    var typed = ParseValue(part, descriptor, context, span);
                    instance.SetAttribute(fieldSchema.Name, typed);
                    positional++;
                }
            }
            structDef.AddInstance(instance);
        }
    }

    private TycoValue ParseValue(string token, string typeName, TycoContext context, SourceSpan span)
    {
        var trimmed = token.Trim();
        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
        {
            return TycoValue.Null();
        }

        return typeName switch
        {
            "bool" => trimmed switch
            {
                "true" => TycoValue.Bool(true),
                "false" => TycoValue.Bool(false),
                _ => throw new TycoParseException($"Invalid bool literal '{trimmed}'", span),
            },
            "int" => TycoValue.Int(Utilities.ParseInteger(trimmed)),
            "float" => TycoValue.Float(double.Parse(trimmed, CultureInfo.InvariantCulture)),
            "date" => TycoValue.Date(ParseStringValue(trimmed, span).Value),
            "time" => TycoValue.Time(Utilities.NormalizeTime(ParseStringValue(trimmed, span).Value)),
            "datetime" => TycoValue.DateTime(Utilities.NormalizeDateTime(ParseStringValue(trimmed, span).Value)),
            "str" => TycoValue.String(ParseStringValue(trimmed, span)),
            _ when typeName.EndsWith("[]", StringComparison.Ordinal) => ParseArray(trimmed, typeName[..^2], context, span),
            _ => ParseStructCall(trimmed, typeName, context, span),
        };
    }

    private TycoValue ParseArray(string token, string elementType, TycoContext context, SourceSpan span)
    {
        if (token == "[]")
        {
            return TycoValue.Array(new List<TycoValue>());
        }
        if (!token.StartsWith("[", StringComparison.Ordinal) || !token.EndsWith("]", StringComparison.Ordinal))
        {
            throw new TycoParseException($"Array literal must be wrapped in []: {token}", span);
        }
        var inner = token[1..^1];
        var parts = Utilities.SplitTopLevel(inner, ',');
        var values = new List<TycoValue>();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }
            values.Add(ParseValue(part, elementType, context, span));
        }
        return TycoValue.Array(values);
    }

    private TycoValue ParseStructCall(string token, string typeName, TycoContext context, SourceSpan span)
    {
        var match = StructCallRegex.Match(token);
        if (!match.Success)
        {
            throw new TycoParseException($"Cannot parse value '{token}' as type '{typeName}'", span);
        }
        var structName = match.Groups[1].Value;
        var args = match.Groups[2].Value.Trim();
        var def = context.GetStruct(structName);
        if (def != null && def.HasPrimaryKey)
        {
            var parsed = ParseStringValue(args, span);
            return TycoValue.Reference(new TycoReference(structName, parsed.Value));
        }
        if (def != null)
        {
            var instance = ParseInlineInstance(structName, args, span);
            return TycoValue.Instance(instance);
        }
        var pk = ParseStringValue(args, span);
        return TycoValue.Reference(new TycoReference(structName, pk.Value));
    }

    private List<TycoValue> ParseEnumChoices(string token, string typeName, TycoContext context, SourceSpan span)
    {
        var trimmed = token.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '(' || trimmed[^1] != ')')
        {
            throw new TycoParseException("Enum choices must be enclosed in parentheses", span);
        }
        var inner = trimmed[1..^1];
        var parts = Utilities.SplitTopLevel(inner, ',');
        if (parts.Count == 0)
        {
            throw new TycoParseException("Enum declaration must contain at least one choice", span);
        }
        var choices = new List<TycoValue>();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                throw new TycoParseException("Enum declaration must contain at least one choice", span);
            }
            var parsed = ParseValue(part, typeName, context, span);
            choices.Add(parsed);
        }
        return choices;
    }

    private TycoInstance ParseInlineInstance(string structName, string args, SourceSpan span)
    {
        var instance = new TycoInstance(structName);
        var parts = Utilities.SplitTopLevel(args, ',');
        var position = 0;
        foreach (var part in parts)
        {
            if (SplitNamedArgument(part, out var name, out var value))
            {
                var parsed = ParseStringValue(value, span);
                instance.SetAttribute(name, TycoValue.String(parsed));
            }
            else
            {
                var parsed = ParseStringValue(part, span);
                instance.SetAttribute($"_arg{position}", TycoValue.String(parsed));
                position++;
            }
        }
        return instance;
    }

    private static bool SplitNamedArgument(string part, out string name, out string value)
    {
        var depth = 0;
        var inQuotes = false;
        var quote = '\0';
        for (var idx = 0; idx < part.Length; idx++)
        {
            var ch = part[idx];
            if (inQuotes)
            {
                if (ch == '\\')
                {
                    idx++;
                }
                else if (ch == quote)
                {
                    inQuotes = false;
                }
                continue;
            }
            switch (ch)
            {
                case '"':
                case '\'':
                    inQuotes = true;
                    quote = ch;
                    break;
                case '(':
                case '[':
                case '{':
                    depth++;
                    break;
                case ')':
                case ']':
                case '}':
                    if (depth > 0)
                    {
                        depth--;
                    }
                    break;
                case ':':
                    if (depth == 0)
                    {
                        name = part[..idx].Trim();
                        value = part[(idx + 1)..].Trim();
                        return IsValidFieldName(name) && value.Length > 0;
                    }
                    break;
            }
        }
        name = string.Empty;
        value = string.Empty;
        return false;
    }

    private static bool IsValidFieldName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        if (!(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }
        for (var i = 1; i < name.Length; i++)
        {
            var ch = name[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
            {
                return false;
            }
        }
        return true;
    }

    private static TycoString ParseStringValue(string token, SourceSpan span)
    {
        if (token.StartsWith("\"\"\"", StringComparison.Ordinal))
        {
            var rest = token[3..];
            var endIdx = rest.IndexOf("\"\"\"", StringComparison.Ordinal);
            if (endIdx < 0)
            {
                throw new TycoParseException("Unterminated multi-line string literal", span);
            }
            var raw = rest[..endIdx];
            var content = Utilities.StripLeadingNewline(raw);
            var unescaped = Utilities.UnescapeBasicString(content);
            return new TycoString(unescaped, unescaped.Contains('{') && unescaped.Contains('}'), false);
        }
        if (token.StartsWith("'''", StringComparison.Ordinal))
        {
            var rest = token[3..];
            var endIdx = rest.IndexOf("'''", StringComparison.Ordinal);
            if (endIdx < 0)
            {
                throw new TycoParseException("Unterminated multi-line literal string", span);
            }
            var content = rest[..endIdx];
            return new TycoString(content, false, true);
        }
        if (token.StartsWith('"') && token.EndsWith('"') && token.Length >= 2)
        {
            var inner = token[1..^1];
            var unescaped = Utilities.UnescapeBasicString(inner);
            return new TycoString(unescaped, unescaped.Contains('{') && unescaped.Contains('}'), false);
        }
        if (token.StartsWith('\'') && token.EndsWith('\'') && token.Length >= 2)
        {
            var inner = token[1..^1];
            return new TycoString(inner, false, true);
        }
        return new TycoString(token, token.Contains('{') && token.Contains('}'), false);
    }

    private static string FieldTypeDescriptor(string baseType, bool isArray) =>
        isArray ? $"{baseType}[]" : baseType;

    private static bool IsScalarType(string typeName) => ScalarTypes.Contains(typeName);

    public static TycoContext Load(string path) => new TycoParser().ParseFile(path);
    public static TycoContext LoadString(string content) => new TycoParser().ParseString(content);
}

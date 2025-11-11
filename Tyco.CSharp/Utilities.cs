using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tyco.CSharp;

internal static class Utilities
{
    private static readonly Regex EscapedNewline = new(@"\\\s*\r?\n\s*", RegexOptions.Compiled);

    public static string StripInlineComment(string line)
    {
        var builder = new StringBuilder();
        var inQuotes = false;
        var escape = false;
        char quote = '\0';
        foreach (var ch in line)
        {
            if (escape)
            {
                builder.Append(ch);
                escape = false;
                continue;
            }
            if (inQuotes)
            {
                builder.Append(ch);
                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }
                if (ch == quote)
                {
                    inQuotes = false;
                }
                continue;
            }
            if (ch == '"' || ch == '\'')
            {
                inQuotes = true;
                quote = ch;
                builder.Append(ch);
                continue;
            }
            if (ch == '#')
            {
                break;
            }
            builder.Append(ch);
        }
        return builder.ToString().TrimEnd();
    }

    public static bool HasUnclosedDelimiter(string line, string delimiter)
    {
        var start = line.IndexOf(delimiter, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }
        return line.IndexOf(delimiter, start + delimiter.Length, StringComparison.Ordinal) < 0;
    }

    public static List<string> SplitTopLevel(string input, char delimiter)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var depth = 0;
        var inQuotes = false;
        var escape = false;
        char quote = '\0';

        foreach (var ch in input)
        {
            if (escape)
            {
                builder.Append(ch);
                escape = false;
                continue;
            }
            if (inQuotes)
            {
                builder.Append(ch);
                if (ch == '\\')
                {
                    escape = true;
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
                    builder.Append(ch);
                    break;
                case '\\':
                    builder.Append(ch);
                    escape = true;
                    break;
                case '[':
                case '{':
                case '(':
                    depth++;
                    builder.Append(ch);
                    break;
                case ']':
                case '}':
                case ')':
                    if (depth > 0)
                    {
                        depth--;
                    }
                    builder.Append(ch);
                    break;
                default:
                    if (ch == delimiter && depth == 0)
                    {
                        var part = builder.ToString().Trim();
                        if (part.Length > 0)
                        {
                            result.Add(part);
                        }
                        builder.Clear();
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }

        if (builder.Length > 0)
        {
            var tail = builder.ToString().Trim();
            if (tail.Length > 0)
            {
                result.Add(tail);
            }
        }

        return result;
    }

    public static long ParseInteger(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Length == 0)
        {
            throw new TycoParseException($"Empty integer literal: {token}");
        }
        var negative = trimmed.StartsWith("-", StringComparison.Ordinal);
        var body = negative ? trimmed[1..] : trimmed;
        int @base = 10;
        if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            body = body[2..];
            @base = 16;
        }
        else if (body.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
        {
            body = body[2..];
            @base = 8;
        }
        else if (body.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            body = body[2..];
            @base = 2;
        }

        try
        {
            long value;
            if (@base == 10)
            {
                value = long.Parse(body, CultureInfo.InvariantCulture);
            }
            else if (@base == 16)
            {
                value = Convert.ToInt64(body, 16);
            }
            else if (@base == 8)
            {
                value = Convert.ToInt64(body, 8);
            }
            else
            {
                value = Convert.ToInt64(body, 2);
            }
            return negative ? -value : value;
        }
        catch (Exception ex)
        {
            throw new TycoParseException($"Failed to parse integer '{token}': {ex.Message}");
        }
    }

    public static string NormalizeTime(string value)
    {
        var idx = value.IndexOf('.');
        if (idx < 0)
        {
            return value;
        }
        var head = value[..(idx + 1)];
        var rest = value[(idx + 1)..];
        var digitCount = rest.TakeWhile(char.IsDigit).Count();
        var rawDigits = rest[..digitCount];
        var digits = rawDigits;
        if (digits.Length < 6)
        {
            digits = digits.PadRight(6, '0');
        }
        else if (digits.Length > 6)
        {
            digits = digits[..6];
        }
        var remainder = rest[digitCount..];
        return head + digits + remainder;
    }

    public static string NormalizeDateTime(string value)
    {
        var result = value.Replace(" ", "T");
        if (result.EndsWith("Z", StringComparison.Ordinal))
        {
            result = result[..^1] + "+00:00";
        }
        var idx = result.IndexOf('.');
        if (idx < 0)
        {
            return result;
        }
        var tzStart = result.Length;
        for (var i = idx; i < result.Length; i++)
        {
            if (result[i] == '+' || result[i] == '-')
            {
                tzStart = i;
                break;
            }
        }
        var fraction = NormalizeTime(result[idx..tzStart]);
        return result[..idx] + fraction + result[tzStart..];
    }

    public static string UnescapeBasicString(string value)
    {
        value = EscapedNewline.Replace(value, string.Empty);
        var builder = new StringBuilder();
        for (var idx = 0; idx < value.Length; idx++)
        {
            var ch = value[idx];
            if (ch != '\\')
            {
                builder.Append(ch);
                continue;
            }
            if (idx + 1 >= value.Length)
            {
                builder.Append('\\');
                break;
            }
            var next = value[++idx];
            switch (next)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case '"':
                    builder.Append('"');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case 'u':
                case 'U':
                    var length = next == 'u' ? 4 : 8;
                    if (idx + length >= value.Length)
                    {
                        throw new TycoParseException("Incomplete unicode escape sequence");
                    }
                    var hex = value.Substring(idx + 1, length);
                    idx += length;
                    var codePoint = Convert.ToInt32(hex, 16);
                    builder.Append(char.ConvertFromUtf32(codePoint));
                    break;
                default:
                    builder.Append('\\').Append(next);
                    break;
            }
        }
        return builder.ToString();
    }

    public static string StripLeadingNewline(string value) =>
        value.StartsWith('\n') ? value[1..] : value;
}

using System.Text;

namespace Tyco.CSharp;

public class TycoException : Exception
{
    protected TycoException(string message) : base(message) { }
    protected TycoException(string message, Exception inner) : base(message, inner) { }
}

public sealed class TycoParseException : TycoException
{
    public SourceSpan? Span { get; }

    public TycoParseException(string message, SourceSpan? span = null, Exception? inner = null)
        : base(message, inner ?? new Exception(message))
    {
        Span = span;
    }

    public TycoParseException WithSpan(SourceSpan span) => new(Span == null ? Message : $"{Message}", span);

    public override string ToString()
    {
        if (Span == null)
        {
            return $"Tyco parse error: {Message}";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Tyco parse error: {Message}");
        builder.AppendLine(Span.Render());
        return builder.ToString();
    }
}

public sealed record SourceSpan(string? Path, int Line, int Column, string LineText)
{
    public string Render()
    {
        var location = string.IsNullOrEmpty(Path)
            ? $"Line {Line}, column {Column}:"
            : $"File \"{Path}\", line {Line}, column {Column}:";
        var pointer = new StringBuilder();
        var visualCol = 0;
        for (var idx = 0; idx < Math.Max(0, Column - 1) && idx < LineText.Length; idx++)
        {
            if (LineText[idx] == '\t')
            {
                var nextTab = ((visualCol / 8) + 1) * 8;
                while (visualCol < nextTab)
                {
                    pointer.Append(' ');
                    visualCol++;
                }
            }
            else
            {
                pointer.Append(' ');
                visualCol++;
            }
        }
        pointer.Append('^');
        return $"{location}\n{LineText}\n{pointer}";
    }
}

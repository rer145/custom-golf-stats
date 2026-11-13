using System.Text;

namespace CustomGolfStats.Migration.Report;

/// <summary>Minimal Markdown builder: headings, paragraphs and tables with escaped cells.</summary>
internal sealed class MarkdownWriter
{
    private readonly StringBuilder _text = new();

    public static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    public void Line(string text = "") => _text.Append(text).Append('\n');

    public void Paragraph(string text)
    {
        Line(text);
        Line();
    }

    public void Heading(string title, int level = 2)
    {
        Line($"{new string('#', level)} {title}");
        Line();
    }

    public void Table(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0)
        {
            Line("None.");
            Line();
            return;
        }

        Line("| " + string.Join(" | ", headers.Select(Escape)) + " |");
        Line("|" + string.Concat(headers.Select(_ => "---|")));
        foreach (var row in rows)
        {
            Line("| " + string.Join(" | ", row.Select(Escape)) + " |");
        }

        Line();
    }

    public override string ToString() => _text.ToString();
}

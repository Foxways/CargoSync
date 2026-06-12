using System;
using System.Drawing;
using System.Windows.Forms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Help
{
    /// <summary>One help page. Body uses a tiny markup: "# " heading, "## " subheading,
    /// "- " bullet, "---" divider, blank line = paragraph break.</summary>
    public sealed record HelpTopic(string Id, string Title, string Body);

    /// <summary>Renders a topic's mini-markup into a RichTextBox with the app's dark theme.</summary>
    public static class HelpRenderer
    {
        public static void Render(RichTextBox box, HelpTopic topic)
        {
            box.SuspendLayout();
            box.Clear();
            box.SelectionIndent = 4;

            Write(box, topic.Title + Environment.NewLine, AppleTheme.Font(16f, FontStyle.Bold), AppleTheme.TextPrimary);
            Write(box, Environment.NewLine, AppleTheme.Body, AppleTheme.TextPrimary);

            foreach (var raw in topic.Body.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line == "---")
                {
                    Write(box, new string('─', 60) + Environment.NewLine, AppleTheme.Caption, AppleTheme.Hairline);
                }
                else if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    Write(box, Environment.NewLine + line.Substring(3) + Environment.NewLine,
                        AppleTheme.Font(11.5f, FontStyle.Bold), AppleTheme.Accent);
                }
                else if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    Write(box, Environment.NewLine + line.Substring(2) + Environment.NewLine,
                        AppleTheme.Font(13f, FontStyle.Bold), AppleTheme.TextPrimary);
                }
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    Write(box, "   •  ", AppleTheme.Body, AppleTheme.Accent);
                    Write(box, line.Substring(2) + Environment.NewLine, AppleTheme.Body, AppleTheme.TextPrimary);
                }
                else
                {
                    Write(box, line + Environment.NewLine, AppleTheme.Body,
                        line.Length == 0 ? AppleTheme.TextPrimary : AppleTheme.TextSecondary);
                }
            }

            box.SelectionStart = 0;
            box.ScrollToCaret();
            box.ResumeLayout();
        }

        private static void Write(RichTextBox box, string text, Font font, Color color)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionFont = font;
            box.SelectionColor = color;
            box.AppendText(text);
        }
    }
}

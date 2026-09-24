using System.Text;

namespace ShotAI.Core.Tests.SourceGuards;

/// <summary>C# source with its comments blanked, since prose may quote a colour on purpose (spec 06 8.2).</summary>
internal static class CSharpText
{
    /// <summary>
    /// Every comment replaced by spaces, newlines kept, so line numbers still match. String and
    /// character literals are copied as they are, so a <c>//</c> inside one is not a comment.
    /// </summary>
    public static string StripComments(string s)
    {
        var sb = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            var next = i + 1 < s.Length ? s[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < s.Length && s[i] != '\n') { sb.Append(' '); i++; }
            }
            else if (c == '/' && next == '*')
            {
                sb.Append("  ");
                i += 2;
                while (i < s.Length && !(s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/')) { sb.Append(s[i] == '\n' ? '\n' : ' '); i++; }
                if (i < s.Length) { sb.Append("  "); i += 2; }
            }
            else if (c == '"' && next == '"' && i + 2 < s.Length && s[i + 2] == '"')
            {
                i = CopyRaw(s, i, sb);
            }
            else if (c == '"')
            {
                i = CopyQuoted(s, i, sb, '"', Verbatim(s, i));
            }
            else if (c == '\'')
            {
                i = CopyQuoted(s, i, sb, '\'', verbatim: false);
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    // @"..." or $@"..." or @$"...": a backslash is not an escape and "" is a quote.
    private static bool Verbatim(string s, int quote)
    {
        for (var j = quote - 1; j >= 0 && j >= quote - 2; j--)
        {
            if (s[j] == '@') return true;
            if (s[j] != '$') return false;
        }
        return false;
    }

    private static int CopyQuoted(string s, int i, StringBuilder sb, char quote, bool verbatim)
    {
        sb.Append(s[i++]);
        while (i < s.Length)
        {
            var c = s[i];
            if (!verbatim && c == '\\' && i + 1 < s.Length)
            {
                sb.Append(c).Append(s[i + 1]);
                i += 2;
                continue;
            }
            sb.Append(c);
            i++;
            if (c == quote)
            {
                if (verbatim && i < s.Length && s[i] == quote) { sb.Append(s[i++]); continue; }
                break;
            }
            if (c == '\n' && !verbatim) break;
        }
        return i;
    }

    // """...""": ends at a run of at least as many quotes as opened it.
    private static int CopyRaw(string s, int i, StringBuilder sb)
    {
        var open = 0;
        while (i < s.Length && s[i] == '"') { sb.Append('"'); open++; i++; }
        while (i < s.Length)
        {
            if (s[i] != '"') { sb.Append(s[i++]); continue; }
            var run = 0;
            while (i < s.Length && s[i] == '"') { sb.Append('"'); run++; i++; }
            if (run >= open) break;
        }
        return i;
    }
}

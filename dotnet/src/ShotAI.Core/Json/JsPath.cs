namespace ShotAI.Core.Json;

/// <summary>Node's <c>path</c> functions that the store and the render gate reproduce (spec 04 7.6).</summary>
internal static class JsPath
{
    /// <summary>
    /// <c>path.win32.extname</c>, the <c>path.extname</c> of Electron on Windows: the last
    /// segment's text from its last <c>.</c>, or <c>""</c> when that dot starts the segment.
    /// </summary>
    /// <remarks>
    /// Both <c>/</c> and <c>\</c> separate segments, trailing separators are skipped, a drive
    /// prefix such as <c>C:</c> is not part of the segment, and <c>..</c> has no extension:
    /// <c>.png</c> gives <c>""</c>, <c>a.</c> gives <c>"."</c> and <c>..png</c> gives
    /// <c>".png"</c>, where <see cref="Path.GetExtension(string)"/> gives <c>".png"</c> for the
    /// first. A line-for-line port of Node 22's <c>lib/path.js</c>.
    /// </remarks>
    public static string ExtName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var start = 0;
        var startDot = -1;
        var startPart = 0;
        var end = -1;
        var matchedSlash = true;
        // Node's preDotState: 0 until a character is seen before the extension's dot, then 1 for a dot and -1 for anything else.
        var preDotState = 0;

        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0])) start = startPart = 2;

        for (var i = path.Length - 1; i >= start; i--)
        {
            var c = path[i];
            if (c is '/' or '\\')
            {
                // A separator before the last segment ends it; trailing ones are skipped.
                if (!matchedSlash)
                {
                    startPart = i + 1;
                    break;
                }
                continue;
            }
            if (end == -1)
            {
                matchedSlash = false;
                end = i + 1;
            }
            if (c == '.')
            {
                if (startDot == -1) startDot = i;
                else if (preDotState != 1) preDotState = 1;
            }
            else if (startDot != -1)
            {
                preDotState = -1;
            }
        }

        if (startDot == -1
            || end == -1
            || preDotState == 0
            || (preDotState == 1 && startDot == end - 1 && startDot == startPart + 1))
        {
            return "";
        }
        return path[startDot..end];
    }
}

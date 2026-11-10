using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace VRCX.Common.Services;

/// <summary>The original's string cleanups for text VRChat sends (base/string.js).</summary>
public static partial class VrchatText
{
    private static readonly (string Plain, string Lookalike)[] Symbols =
    [
        ("@", "＠"), ("#", "＃"), ("$", "＄"), ("%", "％"), ("&", "＆"), ("=", "＝"), ("+", "＋"),
        ("/", "⁄"), ("\\", "＼"), (";", ";"), (":", "˸"), (",", "‚"), ("?", "？"), ("!", "ǃ"),
        ("\"", "＂"), ("<", "≺"), (">", "≻"), (".", "․"), ("^", "＾"), ("{", "｛"), ("}", "｝"),
        ("[", "［"), ("]", "］"), ("(", "（"), (")", "）"), ("|", "｜"), ("*", "∗"),
    ];

    /// <summary>The original's replaceBioSymbols: VRChat's look-alike characters back to ASCII.</summary>
    public static string ReplaceBioSymbols(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var result = text;
        foreach (var (plain, lookalike) in Symbols) result = result.Replace(lookalike, plain, StringComparison.Ordinal);
        return Spaces().Replace(result, " ").TrimEnd();
    }

    /// <summary>ReplaceBioSymbols that keeps a null as null, for optional fields.</summary>
    [return: NotNullIfNotNull(nameof(text))]
    public static string? ReplaceBioSymbolsOrNull(string? text) => text is null ? null : ReplaceBioSymbols(text);

    /// <summary>The original's removeEmojis: emoji and symbol ranges dropped, whitespace collapsed and trimmed.</summary>
    public static string RemoveEmojis(string? text) =>
        string.IsNullOrEmpty(text) ? "" : Whitespace().Replace(Emojis().Replace(text, ""), " ").Trim();

    /// <summary>A status description as the original's sanitizeUserJson keeps it.</summary>
    public static string StatusDescription(string? text) => string.IsNullOrEmpty(text) ? "" : RemoveEmojis(ReplaceBioSymbols(text));

    [GeneratedRegex(" +")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"([✀-➿]|[-]|\uD83C[\uDC00-\uDFFF]|\uD83D[\uDC00-\uDFFF]|[‑-⛿]|\uD83E[\uDD10-\uDDFF])")]
    private static partial Regex Emojis();
}

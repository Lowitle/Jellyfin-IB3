using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Ib3.Client;

/// <summary>
/// Text helpers for the values IB3 publishes: descriptions (plain text with stray HTML), titles
/// (many of them written in capitals) and the numbering fields (<c>season</c>, <c>episode</c>).
/// </summary>
internal static partial class Ib3Text
{
    /// <summary>
    /// Words that are acronyms even when they are short and written in capitals. Used when a title is
    /// written in full capitals and has to be turned into a sentence.
    /// </summary>
    private static readonly HashSet<string> Acronyms = new(StringComparer.Ordinal)
    {
        "IB3", "TV3", "UGT", "CCOO", "PSOE", "PP", "ONG", "UE", "ADN", "IVA", "PIB", "RTVE", "ONU", "OTAN"
    };

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Converts an HTML fragment (or a plain text with stray tags) into plain text.
    /// </summary>
    /// <param name="html">HTML fragment.</param>
    /// <returns>Plain text, or <c>null</c> when there is no text.</returns>
    internal static string? FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = TagRegex().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Turns a title written in full capitals into a sentence, keeping the acronyms.
    /// </summary>
    /// <param name="value">Title published by IB3.</param>
    /// <returns>The title with the casing a library expects.</returns>
    /// <remarks>
    /// IB3 publishes the title of most emissions in capitals (<c>100 ANYS D'HISTÒRIA UGT BALEARS</c>). A
    /// title that already mixes cases —<c>Família Planells-Sala</c>— is returned untouched: the plugin
    /// never rewrites what the broadcaster wrote.
    /// </remarks>
    internal static string Sentence(string? value)
    {
        var text = FromHtml(value);
        if (text is null)
        {
            return string.Empty;
        }

        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count < 8 || letters.Count(char.IsUpper) * 10 < letters.Count * 9)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var startOfWord = true;

        foreach (var (token, isWord) in Tokenize(text))
        {
            if (!isWord)
            {
                builder.Append(token);
                startOfWord = token is "." or "!" or "?" or ":" or "¿" or "¡";
                continue;
            }

            var kept = Acronyms.Contains(token.ToUpperInvariant())
                || (token.Length <= 3 && token.Any(char.IsUpper))
                || token.Any(char.IsDigit);

            var lower = token.ToLowerInvariant();
            if (startOfWord && lower.Length > 0)
            {
                builder.Append(char.ToUpperInvariant(lower[0])).Append(lower.AsSpan(1));
            }
            else
            {
                builder.Append(kept ? token : lower);
            }

            startOfWord = false;
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    private static IEnumerable<(string Text, bool IsWord)> Tokenize(string value)
    {
        var builder = new StringBuilder();

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '\'' or '’' or '-')
            {
                builder.Append(character);
                continue;
            }

            if (builder.Length > 0)
            {
                yield return (builder.ToString(), true);
                builder.Clear();
            }

            if (char.IsWhiteSpace(character))
            {
                yield return (" ", false);
            }
            else
            {
                yield return (character.ToString(), false);
            }
        }

        if (builder.Length > 0)
        {
            yield return (builder.ToString(), true);
        }
    }
}

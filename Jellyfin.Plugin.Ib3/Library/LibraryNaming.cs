using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.Ib3.Library;

/// <summary>
/// Builds file and folder names that are safe on every platform Jellyfin can run on.
/// </summary>
public static class LibraryNaming
{
    private static readonly char[] Forbidden =
    [
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    ];

    /// <summary>
    /// Sanitizes a value so it can be used as a single file or folder name.
    /// </summary>
    /// <param name="value">Original value.</param>
    /// <param name="maxLength">Maximum length.</param>
    /// <param name="fallback">Value returned when nothing usable remains.</param>
    /// <returns>The sanitized value.</returns>
    public static string Sanitize(string? value, int maxLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormC))
        {
            if (char.IsControl(character) || Array.IndexOf(Forbidden, character) >= 0)
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(character);
            }
        }

        var collapsed = CollapseWhitespace(builder.ToString()).Trim(' ', '.', '-', '_');

        if (collapsed.Length == 0)
        {
            return fallback;
        }

        if (collapsed.Length > maxLength)
        {
            collapsed = collapsed[..maxLength].Trim(' ', '.', '-', '_');
        }

        return collapsed.Length == 0 ? fallback : collapsed;
    }

    /// <summary>
    /// Gets the folder name of a series.
    /// </summary>
    /// <param name="seriesTitle">Series title.</param>
    /// <returns>The folder name.</returns>
    public static string SeriesFolderName(string seriesTitle)
        => Sanitize(seriesTitle, 100, "Programa");

    /// <summary>
    /// Gets the base file name of an episode: <c>Programa - S01E02 - Título</c>.
    /// </summary>
    /// <param name="seriesTitle">Series title.</param>
    /// <param name="season">Season number.</param>
    /// <param name="episode">Episode number.</param>
    /// <param name="episodeTitle">Episode title.</param>
    /// <returns>The base file name, without extension.</returns>
    /// <remarks>
    /// The <c>S01E02</c> marker is what Jellyfin reads the season and the episode from: the folder of the
    /// season is a hint, the file name is the source of truth.
    /// </remarks>
    public static string EpisodeFileName(string seriesTitle, int season, int episode, string episodeTitle)
    {
        var marker = string.Format(CultureInfo.InvariantCulture, "S{0:00}E{1:00}", season, episode);
        var name = string.Concat(
            Sanitize(seriesTitle, 90, "Programa"),
            " - ",
            marker,
            " - ",
            Sanitize(episodeTitle, 90, "Capítol " + episode.ToString(CultureInfo.InvariantCulture)));

        return Sanitize(name, 180, marker);
    }

    /// <summary>
    /// Keeps the first subtitle of every language so two tracks of the same language never write the same
    /// file twice, and normalizes the language to the ISO code Jellyfin reads from the file name.
    /// </summary>
    /// <param name="subtitles">Subtitles of an episode.</param>
    /// <returns>The subtitles to write, one per language.</returns>
    internal static IEnumerable<LibrarySubtitle> CleanSubtitles(IReadOnlyList<LibrarySubtitle>? subtitles)
    {
        if (subtitles is null)
        {
            yield break;
        }

        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subtitle in subtitles)
        {
            if (string.IsNullOrWhiteSpace(subtitle.Srt))
            {
                continue;
            }

            // The language ends up in a file name: keep only ASCII letters and digits.
            var language = new string(
                (string.IsNullOrWhiteSpace(subtitle.Language) ? "ca" : subtitle.Language.Trim().ToLowerInvariant())
                .Where(char.IsAsciiLetterOrDigit)
                .ToArray());

            if (language.Length == 0)
            {
                // IB3 publishes practically everything in Catalan, so it is the least wrong guess.
                language = "ca";
            }

            if (languages.Add(language))
            {
                yield return subtitle with { Language = language };
            }
        }
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;

        foreach (var character in value)
        {
            var isSpace = char.IsWhiteSpace(character);
            if (isSpace)
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }
            }
            else
            {
                builder.Append(character);
            }

            lastWasSpace = isSpace;
        }

        return builder.ToString();
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Ib3.Client;

/// <summary>
/// Numbering and dates of the values IB3 publishes.
/// </summary>
internal static partial class Ib3Text
{
    [GeneratedRegex(@"^[Tt]?\s*(\d{1,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex SeasonRegex();

    [GeneratedRegex(@"^(\d{1,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeRegex();

    [GeneratedRegex(@"^(.+?)\s*[-–—|:]\s*.+$", RegexOptions.CultureInvariant)]
    private static partial Regex SeriesPrefixRegex();

    /// <summary>
    /// Reads the season of a video (<c>"1"</c>, <c>"T2"</c>, <c>1</c>).
    /// </summary>
    /// <param name="value">Value published by IB3.</param>
    /// <returns>The season, or <c>null</c> when IB3 publishes none.</returns>
    internal static int? ParseSeason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = SeasonRegex().Match(value.Trim());

        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var season)
            && season > 0
                ? season
                : null;
    }

    /// <summary>
    /// Reads the episode of a video, which is a number for the series and a production code for the
    /// documentaries (<c>"100UGT-25"</c>).
    /// </summary>
    /// <param name="value">Value published by IB3.</param>
    /// <returns>The episode number, or <c>null</c> when the field is not a number.</returns>
    internal static int? ParseEpisode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = EpisodeRegex().Match(value.Trim());

        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var episode)
            && episode > 0
                ? episode
                : null;
    }

    /// <summary>
    /// Reads a date of the API (<c>2025-10-18 23:50:00</c>).
    /// </summary>
    /// <param name="value">Value published by IB3.</param>
    /// <returns>The date, or <c>null</c> when it cannot be read.</returns>
    internal static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var date)
                ? date
                : null;

    /// <summary>
    /// Normalizes a text to letters, digits and single spaces, without accents and in lowercase, so
    /// «Sèries» and «series» compare equal and «L'últim estiu» matches «l ultim estiu».
    /// </summary>
    /// <param name="value">Text to normalize.</param>
    /// <returns>The normalized text, empty when there is nothing to compare.</returns>
    internal static string Key(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var character in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Builds the title of an episode: the title of the video, without the name of the program in front
    /// of it, and with a fallback when IB3 publishes no title of its own.
    /// </summary>
    /// <param name="mediaTitle">Title of the video.</param>
    /// <param name="seriesTitle">Title of the program.</param>
    /// <param name="episode">Episode number, used for the fallback.</param>
    /// <returns>The episode title.</returns>
    internal static string EpisodeTitle(string? mediaTitle, string seriesTitle, int episode)
    {
        var title = Sentence(mediaTitle);

        if (title.Length == 0)
        {
            return string.Format(CultureInfo.InvariantCulture, "Capítol {0}", episode);
        }

        var stripped = StripSeriesPrefix(title, seriesTitle);
        return stripped.Length == 0 ? title : stripped;
    }

    /// <summary>
    /// Removes the name of the program from the front of the title of a video
    /// («Emergències - Nit de tempesta» → «Nit de tempesta»).
    /// </summary>
    /// <param name="title">Title of the video.</param>
    /// <param name="seriesTitle">Title of the program.</param>
    /// <returns>The title without the name of the program, or the title itself when it has no prefix.</returns>
    internal static string StripSeriesPrefix(string title, string seriesTitle)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(seriesTitle))
        {
            return title;
        }

        var match = SeriesPrefixRegex().Match(title.Trim());
        if (!match.Success)
        {
            return title;
        }

        var prefix = match.Groups[1].Value.Trim();
        if (!string.Equals(prefix, seriesTitle.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return title;
        }

        var rest = title.Trim()[prefix.Length..].TrimStart(' ', '-', '–', '—', '|', ':', '.', ',');
        return rest.Trim();
    }
}

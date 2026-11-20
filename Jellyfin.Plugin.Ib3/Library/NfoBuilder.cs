using System.Globalization;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Ib3.Library;

/// <summary>
/// Builds the NFO documents read natively by Jellyfin (through <c>MediaBrowser.XbmcMetadata</c>).
/// </summary>
internal static class NfoBuilder
{
    /// <summary>
    /// Builds the <c>tvshow.nfo</c> document.
    /// </summary>
    /// <param name="series">Series metadata.</param>
    /// <returns>The NFO content.</returns>
    /// <remarks>
    /// The genres are what Jellyfin shows as filters in the library («Sèrie», «Documental»,
    /// «Actualitat»...), and the tags keep the exact IB3 category the program is filed under. The
    /// <c>ib3</c> unique id is always present and default: it is the key the plugin uses to recognise the
    /// series and to tell whether a folder is its own.
    /// <para>
    /// The year and the premiere date come from the first episode IB3 publishes —the catalog publishes no
    /// production year—, the <c>mpaa</c> is the classification by age of the program, and the
    /// <c>namedseason</c> elements name the seasons so Jellyfin shows «Temporada 1» instead of a bare
    /// number. With <see cref="LibrarySeries.LockData"/> the document also carries <c>lockdata</c>, which
    /// stops the online providers from replacing all of it.
    /// </para>
    /// </remarks>
    internal static string BuildSeries(LibrarySeries series)
    {
        ArgumentNullException.ThrowIfNull(series);

        var root = new XElement(
            "tvshow",
            new XElement("title", series.Title),
            new XElement("sorttitle", series.Title),
            new XElement(
                "plot",
                string.IsNullOrWhiteSpace(series.Plot) ? $"Programes de {series.Title} a IB3." : series.Plot));

        if (series.Year > 0)
        {
            root.Add(new XElement("year", series.Year.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (series.PremiereDate is { } premiere)
        {
            root.Add(new XElement("premiered", premiere.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(series.Country))
        {
            root.Add(new XElement("country", series.Country.Trim()));
        }

        root.Add(new XElement("studio", "IB3"));

        foreach (var genre in Distinct(series.Genres))
        {
            root.Add(new XElement("genre", genre));
        }

        foreach (var tag in Distinct(series.Tags))
        {
            root.Add(new XElement("tag", tag));
        }

        root.Add(new XElement("tag", "IB3"));

        if (!string.IsNullOrWhiteSpace(series.OfficialRating))
        {
            root.Add(new XElement("mpaa", series.OfficialRating.Trim()));
        }

        foreach (var season in series.Seasons.Where(number => number > 0).Distinct().Order())
        {
            root.Add(new XElement(
                "namedseason",
                new XAttribute("number", season.ToString(CultureInfo.InvariantCulture)),
                string.Format(CultureInfo.InvariantCulture, "Temporada {0}", season)));
        }

        root.Add(new XElement(
            "uniqueid",
            new XAttribute("type", "ib3"),
            new XAttribute("default", "true"),
            series.ProgramId));

        if (!string.IsNullOrWhiteSpace(series.Website))
        {
            root.Add(new XElement("website", series.Website.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(series.PosterUrl))
        {
            root.Add(new XElement("thumb", series.PosterUrl));
        }

        if (series.LockData)
        {
            root.Add(new XElement("lockdata", "true"));
        }

        return Serialize(root);
    }

    /// <summary>
    /// Builds the <c>season.nfo</c> document of a season.
    /// </summary>
    /// <param name="number">Season number.</param>
    /// <returns>The NFO content.</returns>
    /// <remarks>
    /// Jellyfin reads <c>season.nfo</c> with <c>MediaBrowser.XbmcMetadata</c>: <c>seasonnumber</c> is the
    /// index and <c>title</c> the name, so the seasons of the library stop being «Temporada 1» only
    /// because the folder happens to say so.
    /// </remarks>
    internal static string BuildSeason(int number)
    {
        var root = new XElement(
            "season",
            new XElement("seasonnumber", number.ToString(CultureInfo.InvariantCulture)),
            new XElement("title", string.Format(CultureInfo.InvariantCulture, "Temporada {0}", number)));

        return Serialize(root);
    }

    /// <summary>
    /// Builds the NFO document of an episode.
    /// </summary>
    /// <param name="seriesTitle">Series title.</param>
    /// <param name="episode">Episode.</param>
    /// <returns>The NFO content.</returns>
    internal static string BuildEpisode(string seriesTitle, LibraryEpisode episode)
    {
        var root = new XElement(
            "episodedetails",
            new XElement("title", episode.Title),
            new XElement("showtitle", seriesTitle),
            new XElement("season", episode.Season),
            new XElement("episode", episode.Episode));

        if (!string.IsNullOrWhiteSpace(episode.Plot))
        {
            root.Add(new XElement("plot", episode.Plot));
        }

        if (episode.Runtime is { } runtime && runtime.TotalMinutes >= 1)
        {
            root.Add(new XElement(
                "runtime",
                ((int)Math.Round(runtime.TotalMinutes, MidpointRounding.AwayFromZero))
                    .ToString(CultureInfo.InvariantCulture)));
        }

        if (episode.Aired is { } aired)
        {
            root.Add(new XElement("aired", aired.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(episode.VideoId))
        {
            root.Add(new XElement("uniqueid", new XAttribute("type", "ib3"), episode.VideoId));
        }

        if (!string.IsNullOrWhiteSpace(episode.ThumbnailUrl))
        {
            root.Add(new XElement("thumb", episode.ThumbnailUrl));
        }

        foreach (var genre in episode.Genres.Where(g => !string.IsNullOrWhiteSpace(g)))
        {
            root.Add(new XElement("genre", genre));
        }

        if (!string.IsNullOrWhiteSpace(episode.Director))
        {
            root.Add(new XElement("director", episode.Director));
        }

        return Serialize(root);
    }

    private static string Serialize(XElement root)
        => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + root + "\n";

    /// <summary>
    /// Trims the values and drops the empty ones and the duplicates, so a genre never lands twice in the
    /// NFO because IB3 published it twice.
    /// </summary>
    /// <param name="values">Values to clean.</param>
    /// <returns>The distinct values, in the original order.</returns>
    private static IEnumerable<string> Distinct(IEnumerable<string>? values)
    {
        if (values is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            var text = value?.Trim();
            if (!string.IsNullOrEmpty(text) && seen.Add(text))
            {
                yield return text;
            }
        }
    }
}

namespace Jellyfin.Plugin.Ib3.Library;

/// <summary>
/// A program of IB3 ready to be written to disk as a series, with the metadata Jellyfin uses to classify
/// it.
/// </summary>
public sealed class LibrarySeries
{
    /// <summary>
    /// Gets or sets the series title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the IB3 program id (written as the default unique id of the <c>tvshow.nfo</c>).
    /// </summary>
    public string ProgramId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the slug of the public page of the program (<c>emergencies</c>).
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the plot.
    /// </summary>
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets the public page of the program on totib3.org.
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// Gets or sets the year of the first episode IB3 publishes, which is what Jellyfin shows next to the
    /// title of the series.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the date of the first episode IB3 publishes.
    /// </summary>
    public DateTimeOffset? PremiereDate { get; set; }

    /// <summary>
    /// Gets or sets the classification by age IB3 publishes for the program (<c>7</c>, <c>12</c>,
    /// <c>16</c>, <c>18</c> or <c>TP</c>), or <c>null</c> when it publishes none.
    /// </summary>
    public string? OfficialRating { get; set; }

    /// <summary>
    /// Gets or sets the seasons the series publishes, so Jellyfin names them instead of showing bare
    /// numbers.
    /// </summary>
    public IReadOnlyCollection<int> Seasons { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the <c>tvshow.nfo</c> carries <c>lockdata</c>, which tells
    /// Jellyfin to keep the metadata written here instead of letting the online providers replace it.
    /// </summary>
    /// <remarks>
    /// Without the lock a library with TheMovieDb or TheTVDB enabled can pair «Dona» with an unrelated
    /// series of the same name, because the numbering of the episodes of IB3 matches neither provider.
    /// </remarks>
    public bool LockData { get; set; }

    /// <summary>
    /// Gets or sets the genres written to the <c>tvshow.nfo</c>. The first one is the section of the
    /// platform ("Sèrie", "Documental", "Actualitat"...); Jellyfin shows them as filters.
    /// </summary>
    public IReadOnlyCollection<string> Genres { get; set; } = [];

    /// <summary>
    /// Gets or sets the tags written to the <c>tvshow.nfo</c> (the raw IB3 category names).
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the production country ("ES").
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Gets or sets the URL of the artwork of the series, downloaded next to the <c>tvshow.nfo</c>.
    /// </summary>
    public string? PosterUrl { get; set; }
}

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

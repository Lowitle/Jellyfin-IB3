using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Ib3.Configuration;

/// <summary>
/// Configuration of the plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the folder of the server the library is written to.
    /// </summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the programs of IB3 that are mirrored.
    /// </summary>
    public List<Ib3ProgramReference> Programs { get; set; } = [];

    /// <summary>
    /// Gets or sets the interval of the refresh task, in hours.
    /// </summary>
    public int RefreshIntervalHours { get; set; } = 12;

    /// <summary>
    /// Gets or sets a value indicating whether the <c>tvshow.nfo</c> and <c>.nfo</c> files are written.
    /// </summary>
    /// <remarks>
    /// The numbering of IB3 does not match TVDB/TMDB, so the local metadata is the source of truth of the
    /// library: with the NFO files turned off Jellyfin only shows the file names.
    /// </remarks>
    public bool WriteNfo { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the subtitles of the videos are downloaded.
    /// </summary>
    public bool WriteSubtitles { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the poster and the backdrop of each series are saved.
    /// </summary>
    public bool WriteArtwork { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the files of the episodes that IB3 has unpublished are removed.
    /// </summary>
    public bool DeleteObsoleteFiles { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether each program is written in the subfolder of its section
    /// («Sèries», «Documentals», «Actualitat»...).
    /// </summary>
    public bool OrganizeByKind { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether each program is written in a subfolder of its subcategory
    /// («Documentals/Història», «Programes/IB3 Júnior»...) inside the folder of its section.
    /// </summary>
    /// <remarks>
    /// It only applies with <see cref="OrganizeByKind"/> on, which is what creates the folder of the section.
    /// A program IB3 files under no subcategory is written straight in the folder of its section.
    /// </remarks>
    public bool OrganizeBySubcategory { get; set; } = true;

    /// <summary>
    /// Gets or sets the name of the library the plugin offers to create.
    /// </summary>
    public string LibraryName { get; set; } = "IB3";
}

/// <summary>
/// A program of IB3 the user has added to the library.
/// </summary>
public class Ib3ProgramReference
{
    /// <summary>
    /// Gets or sets the IB3 program id (the GUID of the API), which is the key of the program.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the slug of the program (<c>emergencies</c>), used to rebuild its web address.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title of the program the last run wrote, which names the series folder.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what the user typed to add the program: the URL of totib3.org, the slug or the id.
    /// </summary>
    /// <remarks>
    /// The reference is kept so the configuration page can show where the program came from, and so the
    /// program can be resolved again when the <see cref="Id"/> was never filled in.
    /// </remarks>
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the folder of the layout the user pinned for the program, or an empty string to keep
    /// the classification of the catalog.
    /// </summary>
    public string CategoryOverride { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the series folder the last run wrote, so a program that changes its title or its kind
    /// does not leave the old folder behind.
    /// </summary>
    public string WrittenPath { get; set; } = string.Empty;
}

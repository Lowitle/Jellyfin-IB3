using Jellyfin.Plugin.Ib3.Client;

namespace Jellyfin.Plugin.Ib3.Library;

/// <summary>
/// Folders the programs of IB3 are written to, one per section of <c>totib3.org</c>.
/// </summary>
/// <remarks>
/// <para>
/// The platform publishes eight sections of video —«Actualitat», «Retransmissions», «Sèries», «Cinema»,
/// «Documentals», «Programes», «Destacat» and «Arxiu»— and most of them are divided in subcategories. A
/// destination folder that holds every program straight under it forces the user to keep a single library:
/// with one folder per section the default «IB3» library indexes all of them and a curated library can
/// point at just one —Jellyfin indexes a folder in every library that points at it, so no file is
/// duplicated.
/// </para>
/// <para>
/// The subcategories of the site are not folders: a library of «Programas de TV» reads every folder that
/// groups other series («Programes/Cuina», «Programes/Entreteniment») as a series of its own, so the series
/// folder is always written straight in the folder of its section, and the subcategory stays where
/// Jellyfin wants it, in the metadata of the program (a genre and a tag of the <c>tvshow.nfo</c>).
/// </para>
/// <para>
/// The folders are only written when the user turns the layout on
/// (<see cref="Configuration.PluginConfiguration.OrganizeByKind"/>): a library written with the layout off
/// keeps its programs straight under the destination folder until the user asks for the change, which
/// rewrites the series once (Jellyfin reads them again).
/// </para>
/// </remarks>
internal static class LibraryCategory
{
    /// <summary>News, current affairs and the daily bulletins («Actualitat»).</summary>
    internal const string News = Ib3Categories.Actualitat;

    /// <summary>Live broadcasts and sports («Retransmissions»).</summary>
    internal const string Broadcasts = Ib3Categories.Retransmissions;

    /// <summary>Fiction series («Sèries»).</summary>
    internal const string Series = Ib3Categories.Series;

    /// <summary>Films and made-for-TV movies («Cinema»).</summary>
    internal const string Cinema = Ib3Categories.Cinema;

    /// <summary>Documentaries and docu-series («Documentals»).</summary>
    internal const string Documentaries = Ib3Categories.Documentaries;

    /// <summary>Magazines, contests, cooking and children's programmes («Programes»).</summary>
    internal const string Shows = Ib3Categories.Shows;

    /// <summary>The programs the platform highlights («Destacat»).</summary>
    internal const string Featured = Ib3Categories.Featured;

    /// <summary>Everything the platform keeps in its archive («Arxiu»).</summary>
    internal const string Archive = Ib3Categories.Archive;

    /// <summary>
    /// Gets the folders the «IB3» library indexes, one per section.
    /// </summary>
    /// <remarks>
    /// The order is the one of the menu of the site, and it is stable: the provisioning adds the folders
    /// that are missing and never renames them.
    /// </remarks>
    internal static IReadOnlyList<string> All { get; } = Ib3Categories.Names;

    /// <summary>
    /// Resolves the folder a program is written to.
    /// </summary>
    /// <param name="category">
    /// Name of the IB3 category the program is filed under («Sèries», «Documentals», «Actualitat»...), or
    /// <c>null</c> when IB3 files it under none.
    /// </param>
    /// <param name="pinned">
    /// Folder the user pinned for the program, or <c>null</c> to keep the classification of the catalog.
    /// </param>
    /// <returns>The folder of the program, relative to the destination folder.</returns>
    /// <remarks>
    /// A program IB3 files under no known category lands in <see cref="Shows"/>, never straight in the
    /// destination folder: the library indexes the folders of the layout, not the destination itself.
    /// </remarks>
    internal static string Resolve(string? category, string? pinned)
    {
        var normalized = Normalize(pinned);

        return normalized ?? Normalize(category) ?? Shows;
    }

    /// <summary>
    /// Normalizes the name of a section to the folder of the layout.
    /// </summary>
    /// <param name="category">Name typed or stored in the configuration, in any casing and with or without accents.</param>
    /// <returns>The folder of the section, or <c>null</c> when it is not one of the folders of the layout.</returns>
    /// <remarks>
    /// The names of the previous versions of the plugin («Informatius», «Infantil») are mapped to the section
    /// that holds them now, so a configuration written before the move keeps working without a migration.
    /// </remarks>
    internal static string? Normalize(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        var value = category.Trim();

        foreach (var folder in All)
        {
            if (Ib3Text.Key(value) == Ib3Text.Key(folder))
            {
                return folder;
            }
        }

        // The catalog publishes some names in the singular («Sèrie», «Documental») and the site uses other
        // words for the same section («Informàtius», «Infantil», «Esports»).
        return Ib3Text.Key(value) switch
        {
            "serie" or "series" or "ficcio" or "ficcion" => Series,
            "documental" or "documentals" or "documentales" or "documentary" => Documentaries,
            "informatiu" or "informatius" or "informativos" or "noticies" or "noticias" or "news" => News,
            "retransmissio" or "retransmissions" or "retransmisiones" or "directes" or "esports"
                or "deportes" or "sports" => Broadcasts,
            "pelicula" or "pelicules" or "peliculas" or "films" or "movies" => Cinema,
            "programa" or "programes" or "programas" or "infantil" or "infantils" or "menuts"
                or "junior" or "shows" => Shows,
            "destacat" or "destacats" or "destacados" or "featured" => Featured,
            "arxiu" or "arxius" or "archivo" or "archive" => Archive,
            _ => null
        };
    }

    /// <summary>
    /// Deletes the empty folders the subcategory layout of the previous versions left inside the sections.
    /// </summary>
    /// <param name="rootPath">Destination folder of the plugin.</param>
    /// <returns>The folders that were deleted, in the order of the layout.</returns>
    /// <remarks>
    /// The subcategory folders («Programes/Cuina», «Documentals/Història») were dropped because a library of
    /// «Programas de TV» reads a folder that only groups other series as a series of its own, so the program
    /// that moves up a level leaves an empty folder behind. Only a folder with nothing inside is removed —a
    /// series the plugin does not write any more keeps its folder, and so does anything of the user—, and a
    /// folder that cannot be read or deleted is left for the next run.
    /// </remarks>
    internal static IReadOnlyList<string> RemoveEmptySubcategoryFolders(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var removed = new List<string>();

        foreach (var section in Sections(rootPath))
        {
            if (!Directory.Exists(section))
            {
                continue;
            }

            try
            {
                foreach (var folder in Directory.EnumerateDirectories(section))
                {
                    if (Directory.EnumerateFileSystemEntries(folder).Any())
                    {
                        continue;
                    }

                    Directory.Delete(folder);
                    removed.Add(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing else to do: the next run tries again.
            }
        }

        return removed;
    }

    /// <summary>
    /// Builds the folder a section is written to.
    /// </summary>
    /// <param name="rootPath">Destination folder of the plugin.</param>
    /// <param name="category">Section of the program.</param>
    /// <returns>The folder of the section.</returns>
    internal static string Folder(string rootPath, string category)
        => Path.Combine(rootPath, category);

    /// <summary>
    /// Builds the folders the «IB3» library indexes: one per section of the platform under the destination
    /// folder.
    /// </summary>
    /// <param name="rootPath">Destination folder of the plugin.</param>
    /// <returns>The folders of the sections, in the order of the menu of the site.</returns>
    /// <remarks>
    /// Each folder is a media path of the library: Jellyfin reads a folder that is not a media path of any
    /// library as a plain folder of the library it hangs from, so a library that points at the destination
    /// folder shows the eight section names as if they were series and mixes the subcategory folders with
    /// them. Pointing at the folders of the sections keeps the layout of the platform in the library.
    /// </remarks>
    internal static IReadOnlyList<string> Sections(string rootPath)
        => [.. All.Select(section => Folder(rootPath, section))];
}

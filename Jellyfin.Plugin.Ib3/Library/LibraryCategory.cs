using Jellyfin.Plugin.Ib3.Client;

namespace Jellyfin.Plugin.Ib3.Library;

/// <summary>
/// Folders the programs of IB3 are written to, one per section of <c>totib3.org</c>, plus the subfolder of
/// the subcategory each program is filed under.
/// </summary>
/// <remarks>
/// The platform publishes eight sections of video —«Actualitat», «Retransmissions», «Sèries», «Cinema»,
/// «Documentals», «Programes», «Destacat» and «Arxiu»— and most of them are divided in subcategories. A
/// destination folder that holds every program straight under it forces the user to keep a single library:
/// with one folder per section the default «IB3» library indexes all of them and a curated library can
/// point at just one —Jellyfin indexes a folder in every library that points at it, so no file is
/// duplicated—, while the subfolder adds the second level the site shows in its menu.
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
    /// Normalizes the name of a subcategory to a single folder name.
    /// </summary>
    /// <param name="subcategory">Subcategory the program is filed under, or <c>null</c>.</param>
    /// <returns>The folder of the subcategory, or <c>null</c> when there is none to write.</returns>
    /// <remarks>
    /// The subcategory is a name of the platform («IB3 Júnior», «Documentals històrics»), so it is only
    /// sanitized: nothing of the layout depends on its value, it only subdivides the folder of the section.
    /// </remarks>
    internal static string? Subcategory(string? subcategory)
    {
        var name = string.IsNullOrWhiteSpace(subcategory) ? null : LibraryNaming.Sanitize(subcategory, 80, string.Empty);

        return string.IsNullOrEmpty(name) ? null : name;
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
    /// Builds the folder a program is written to, with the subfolder of its subcategory when it has one.
    /// </summary>
    /// <param name="rootPath">Destination folder of the plugin.</param>
    /// <param name="category">Section of the program.</param>
    /// <param name="subcategory">Subcategory of the program, or <c>null</c>.</param>
    /// <returns>The folder of the section, or the subfolder of the subcategory inside it.</returns>
    internal static string Folder(string rootPath, string category, string? subcategory)
        => Subcategory(subcategory) is { } name
            ? Path.Combine(Folder(rootPath, category), name)
            : Folder(rootPath, category);
}

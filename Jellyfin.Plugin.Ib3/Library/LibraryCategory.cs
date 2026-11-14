namespace Jellyfin.Plugin.Ib3.Library;

/// <summary>
/// Folders the programs of IB3 are written to, one per kind of program.
/// </summary>
/// <remarks>
/// A destination folder that holds every program straight under it forces the user to keep a single
/// library: Jellyfin filters by genre, but «Documentals», «Informatius» or «Infantil» cannot be libraries
/// of their own. With one folder per kind the plugin writes each program where its kind belongs, the
/// default «IB3» library indexes the folders of the series and a curated library can point at just one of
/// them —Jellyfin indexes a folder in every library that points at it, so no file is duplicated.
/// <para>
/// The folders are only written when the user turns the layout on
/// (<see cref="Configuration.PluginConfiguration.OrganizeByKind"/>): a library written with the layout off
/// keeps its programs straight under the destination folder until the user asks for the change, which
/// rewrites the series once (Jellyfin reads them again).
/// </para>
/// </remarks>
internal static class LibraryCategory
{
    /// <summary>The miscellany: contests, music, culture, cooking and the unknown.</summary>
    internal const string Shows = "Programes";

    /// <summary>Fiction series.</summary>
    internal const string Series = "Sèries";

    /// <summary>Documentaries and docu-series.</summary>
    internal const string Documentaries = "Documentals";

    /// <summary>News and current affairs.</summary>
    internal const string News = "Informatius";

    /// <summary>Children's programmes.</summary>
    internal const string Children = "Infantil";

    /// <summary>
    /// Gets the folders the «IB3» library indexes, one per kind.
    /// </summary>
    /// <remarks>
    /// The order is the one the configuration page shows, most specific first, and it is stable: the
    /// provisioning adds the folders that are missing and never renames them.
    /// </remarks>
    internal static IReadOnlyList<string> All { get; } =
        [Shows, Series, Documentaries, News, Children];

    /// <summary>
    /// Resolves the folder a program is written to.
    /// </summary>
    /// <param name="category">
    /// Name of the IB3 category the program is filed under («Sèries», «Documentals»,
    /// «Informatius»...), or <c>null</c> when IB3 files it under none.
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
    /// Normalizes the name of a kind of program to the folder of the layout.
    /// </summary>
    /// <param name="category">Name typed or stored in the configuration, in any casing.</param>
    /// <returns>The folder of the kind, or <c>null</c> when it is not one of the folders of the layout.</returns>
    internal static string? Normalize(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        var value = category.Trim();

        foreach (var folder in All)
        {
            if (Same(value, folder))
            {
                return folder;
            }
        }

        // IB3 publishes some names in the singular («Sèrie», «Documental»), and the news category is also
        // called «Actualitat» in the web player.
        if (Same(value, "Sèrie") || Same(value, "Series") || Same(value, "Serie"))
        {
            return Series;
        }

        if (Same(value, "Documental") || Same(value, "Documentales"))
        {
            return Documentaries;
        }

        if (Same(value, "Informatiu") || Same(value, "Actualitat") || Same(value, "Notícies"))
        {
            return News;
        }

        if (Same(value, "Infantils") || Same(value, "Menuts"))
        {
            return Children;
        }

        return null;
    }

    /// <summary>
    /// Builds the folder a category is written to.
    /// </summary>
    /// <param name="rootPath">Destination folder of the plugin.</param>
    /// <param name="category">Category of the program.</param>
    /// <returns>The folder of the category.</returns>
    internal static string Folder(string rootPath, string category)
        => Path.Combine(rootPath, category);

    private static bool Same(string? left, string right)
        => string.Equals(left?.Trim(), right, StringComparison.OrdinalIgnoreCase);
}

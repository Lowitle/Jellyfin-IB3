namespace Jellyfin.Plugin.Ib3.Client;

/// <summary>
/// The sections of the digital platform of IB3 (<c>totib3.org</c>): the eight video categories the site
/// files its programs under.
/// </summary>
/// <remarks>
/// <para>
/// The API answers <c>categories</c> with sixteen entries: eight of video and eight of audio, three of
/// them sharing the name with a video section («Actualitat», «Destacat», «Arxiu»). The plugin only
/// mirrors video, so it filters by <c>type</c>, and this table is the fallback when the endpoint cannot be
/// read — it is the same list the menu of totib3.org shows, with the ids published today.
/// </para>
/// <para>
/// The table is what the library layout names its folders after: one folder per section, so a curated
/// library can point at «Documentals» or «Arxiu» without copying a single file.
/// </para>
/// </remarks>
public static class Ib3Categories
{
    /// <summary>News and current affairs.</summary>
    public const string Actualitat = "Actualitat";

    /// <summary>Live events and sports broadcasts.</summary>
    public const string Retransmissions = "Retransmissions";

    /// <summary>Fiction series.</summary>
    public const string Series = "Sèries";

    /// <summary>Films.</summary>
    public const string Cinema = "Cinema";

    /// <summary>Documentaries and docu-series.</summary>
    public const string Documentaries = "Documentals";

    /// <summary>Magazines, contests, culture, cooking and children's programmes.</summary>
    public const string Shows = "Programes";

    /// <summary>The selection of the platform.</summary>
    public const string Featured = "Destacat";

    /// <summary>The archive: what IB3 keeps available out of its schedule.</summary>
    public const string Archive = "Arxiu";

    /// <summary>
    /// Kind of catalog entry the plugin mirrors. The audio sections publish podcasts, which have no
    /// video file to write to a <c>.strm</c>.
    /// </summary>
    public const string VideoType = "video";

    /// <summary>
    /// Gets the eight video sections, in the order the menu of totib3.org shows them.
    /// </summary>
    public static IReadOnlyList<Ib3CategoryInfo> Video { get; } =
    [
        new(
            "0250d430-dd9e-11ec-90c9-c437725f29d4",
            Actualitat,
            [
                new("5ced8dc1-dd9e-11ec-90c9-c437725f29d4", "Magazín"),
                new("a6c0c083-9857-11f0-8d28-5254b4aaea21", "El temps"),
                new("cade43a1-dda9-11ec-90c9-c437725f29d4", "Notícies"),
                new("d1251ebb-fd1b-11ec-90c9-c437725f29d4", "Reportatges"),
                new("e0203d58-93a0-11f0-8d28-5254b4aaea21", "Actualitat esportiva")
            ]),
        new(
            "357337df-f7ae-11ec-90c9-c437725f29d4",
            Retransmissions,
            [
                new("3fd8db1a-f7af-11ec-90c9-c437725f29d4", "Festes de les Balears"),
                new("58555649-f7af-11ec-90c9-c437725f29d4", "Esdeveniments"),
                new("5b8bd367-f7af-11ec-90c9-c437725f29d4", "Altres"),
                new("a919b852-8fcf-11f0-8d28-5254b4aaea21", "Retransmissions esportives")
            ]),
        new(
            "47c0ddd5-dcec-11ec-90c9-c437725f29d4",
            Series,
            [
                new("39ac5ea6-45cf-11f0-8d28-5254b4aaea21", "Sèries"),
                new("5aa207f2-e7d1-11ec-90c9-c437725f29d4", "Cinema")
            ]),
        new("6fd22f5f-9869-11f0-8d28-5254b4aaea21", Cinema, []),
        new(
            "a86f5001-d752-11ec-8f22-c437725f29d4",
            Documentaries,
            [
                new("0564fb5d-8fc7-11f0-8d28-5254b4aaea21", "Biografies"),
                new("110b0bdb-8fc7-11f0-8d28-5254b4aaea21", "Documentals divulgatius"),
                new("418b2a77-8fc7-11f0-8d28-5254b4aaea21", "Documentals esportius"),
                new("544bddea-9565-11f1-b2f7-fa163ed37948", "Documentals culturals"),
                new("b4a3f0a7-955f-11f1-b2f7-fa163ed37948", "Natura i ciència"),
                new("e7a0323d-8fc6-11f0-8d28-5254b4aaea21", "Documentals recents"),
                new("fe153c04-8fc6-11f0-8d28-5254b4aaea21", "Documentals històrics")
            ]),
        new(
            "afaf0d47-d752-11ec-8f22-c437725f29d4",
            Shows,
            [
                new("7ecd0735-dda7-11ec-90c9-c437725f29d4", "Divulgació"),
                new("877895d3-dda7-11ec-90c9-c437725f29d4", "Natura i pagesia"),
                new("901e1516-dda7-11ec-90c9-c437725f29d4", "Cultura"),
                new("a402247a-dda7-11ec-90c9-c437725f29d4", "Concurs"),
                new("a9ea8605-dda7-11ec-90c9-c437725f29d4", "IB3 Júnior"),
                new("ba9eebd5-dda7-11ec-90c9-c437725f29d4", "Entreteniment"),
                new("bf0306b2-93a0-11f0-8d28-5254b4aaea21", "Programes esportius"),
                new("c8e52caa-dda7-11ec-90c9-c437725f29d4", "Cuina"),
                new("db89c0e8-caa8-11f0-942e-fa163ed37948", "Videopòdcast")
            ]),
        new("e1b51b6d-f7ad-11ec-90c9-c437725f29d4", Featured, []),
        new("ea191152-f7ad-11ec-90c9-c437725f29d4", Archive, [])
    ];

    /// <summary>
    /// Gets the names of the eight video sections, in the order of the menu.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = Video.Select(category => category.Name).ToList();

    /// <summary>
    /// Tells whether a category of the catalog is a video section.
    /// </summary>
    /// <param name="category">Category published by IB3.</param>
    /// <returns><c>true</c> for the eight sections the plugin mirrors.</returns>
    /// <remarks>
    /// <para>
    /// The name is not enough to tell a section of video from one of audio —«Actualitat», «Destacat» and
    /// «Arxiu» exist twice—, so a known id decides first and the <c>type</c> the API publishes next; the
    /// names are only the last resort, for a category that arrives without either of them.
    /// </para>
    /// <para>
    /// An id the table does not know is not an answer: IB3 publishes new categories without notice, and a
    /// video one has to be mirrored instead of dropped, so the type is what decides.
    /// </para>
    /// </remarks>
    public static bool IsVideo(Ib3Category? category)
    {
        if (category is null)
        {
            return false;
        }

        var id = category.Id?.Trim();

        if (id is { Length: > 0 }
            && Video.Any(section => string.Equals(section.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var type = category.Type?.Trim();

        if (type is { Length: > 0 })
        {
            return string.Equals(type, VideoType, StringComparison.OrdinalIgnoreCase);
        }

        return Names.Contains(category.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the video section a category id or name names.
    /// </summary>
    /// <param name="idOrName">Id (a GUID) or name («Documentals») of the section.</param>
    /// <returns>The section, or <c>null</c> when it is not one of the eight.</returns>
    /// <remarks>
    /// The name is compared in any casing and with or without accents, so «series», «Sèries» and «SÉRIES»
    /// all name the same section.
    /// </remarks>
    public static Ib3CategoryInfo? Find(string? idOrName)
    {
        var value = idOrName?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var key = Ib3Text.Key(value);

        return Video.FirstOrDefault(section =>
            string.Equals(section.Id, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(section.Name, value, StringComparison.OrdinalIgnoreCase)
            || Ib3Text.Key(section.Name) == key);
    }

    /// <summary>
    /// Tells where a section sits in the menu of the platform.
    /// </summary>
    /// <param name="idOrName">Id (a GUID) or name («Documentals») of the section.</param>
    /// <returns>The position in the menu, or <see cref="int.MaxValue"/> when it is not one of the eight.</returns>
    /// <remarks>
    /// The menu of totib3.org has a stable order and the API answers in its own, so the order of this
    /// table is what sorts the sections of the configuration page and what picks the section of a program
    /// that IB3 files under two of them («Actualitat» and «Programes»).
    /// </remarks>
    public static int OrderOf(string? idOrName)
    {
        var section = Find(idOrName);

        if (section is null)
        {
            return int.MaxValue;
        }

        for (var index = 0; index < Video.Count; index++)
        {
            if (string.Equals(Video[index].Id, section.Id, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Finds the subcategory of a section by its id or its name.
    /// </summary>
    /// <param name="section">Section of the platform.</param>
    /// <param name="idOrName">Id or name of the subcategory.</param>
    /// <returns>The subcategory, or <c>null</c> when the section publishes none that fits.</returns>
    /// <remarks>
    /// A subcategory the section does not publish returns <c>null</c> and the whole section is listed
    /// instead, so a stale name of the page never empties the table.
    /// </remarks>
    public static Ib3SubcategoryInfo? FindSubcategory(Ib3CategoryInfo? section, string? idOrName)
    {
        var value = idOrName?.Trim();

        if (section is null || string.IsNullOrEmpty(value))
        {
            return null;
        }

        var key = Ib3Text.Key(value);

        return section.Subcategories.FirstOrDefault(subcategory =>
            string.Equals(subcategory.Id, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(subcategory.Name, value, StringComparison.OrdinalIgnoreCase)
            || Ib3Text.Key(subcategory.Name) == key);
    }
}

/// <summary>
/// A video section of the platform (the entries of the menu of <c>totib3.org</c>).
/// </summary>
/// <param name="Id">Id of the category in the API.</param>
/// <param name="Name">Name of the section («Sèries», «Documentals»...).</param>
/// <param name="Subcategories">Subcategories the section is divided into.</param>
public sealed record Ib3CategoryInfo(
    string Id,
    string Name,
    IReadOnlyList<Ib3SubcategoryInfo> Subcategories);

/// <summary>
/// A subcategory of a video section («Documentals històrics», «IB3 Júnior»...).
/// </summary>
/// <param name="Id">Id of the subcategory in the API.</param>
/// <param name="Name">Name of the subcategory.</param>
public sealed record Ib3SubcategoryInfo(string Id, string Name);


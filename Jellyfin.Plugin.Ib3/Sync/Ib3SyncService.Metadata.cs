using Jellyfin.Plugin.Ib3.Client;
using Jellyfin.Plugin.Ib3.Configuration;
using Jellyfin.Plugin.Ib3.Library;

namespace Jellyfin.Plugin.Ib3.Sync;

/// <summary>
/// Metadata of a program: the title of the series, the genre of its kind and the raw IB3 categories.
/// </summary>
public sealed partial class Ib3SyncService
{
    /// <summary>
    /// Gets the title of the series, preferring the one IB3 publishes now so a renamed program is renamed
    /// in the library too.
    /// </summary>
    /// <param name="program">Program of the catalog.</param>
    /// <param name="reference">Program reference, used as fallback.</param>
    /// <returns>The title of the series, never empty.</returns>
    internal static string SeriesTitle(Ib3Program program, Ib3ProgramReference reference)
    {
        foreach (var candidate in new[] { program.Title, reference.Title })
        {
            var title = Ib3Text.Sentence(candidate);
            if (title.Length > 0)
            {
                return title;
            }
        }

        var slug = program.Slug ?? reference.Slug;
        var fromSlug = Ib3Text.Sentence(slug?.Replace('-', ' ').Replace('_', ' '));

        return fromSlug.Length > 0 ? fromSlug : "Programa";
    }

    /// <summary>
    /// Builds the genres written to the <c>tvshow.nfo</c>: the kind of program and the themes IB3 files it
    /// under. Jellyfin shows them as filters of the library.
    /// </summary>
    /// <param name="program">Program of the catalog.</param>
    /// <returns>The distinct genres, the kind first.</returns>
    internal static IReadOnlyCollection<string> Genres(Ib3Program program)
    {
        var genres = new List<string> { KindLabel(program) };
        genres.AddRange(Names(Subcategories(program)));

        return Distinct(genres);
    }

    /// <summary>
    /// Builds the tags written to the <c>tvshow.nfo</c>: the raw names of the categories and the
    /// subcategories of IB3, so the exact filing of the broadcaster is kept.
    /// </summary>
    /// <param name="program">Program of the catalog.</param>
    /// <returns>The distinct tags.</returns>
    internal static IReadOnlyCollection<string> Tags(Ib3Program program)
        => Distinct(Names(program.Categories).Concat(Names(Subcategories(program))));

    /// <summary>
    /// Names the section of a program as a genre («Sèrie», «Documental», «Actualitat», «Pel·lícula»...).
    /// </summary>
    /// <param name="program">Program of the catalog.</param>
    /// <returns>The genre of the section of the program.</returns>
    /// <remarks>
    /// The genre is the section and not the subcategory: the subcategories are written as tags too, so a
    /// client that groups by genre keeps the eight sections of the platform.
    /// </remarks>
    internal static string KindLabel(Ib3Program program)
        => LibraryCategory.Resolve(Ib3Client.Ib3CategoryName(program), null) switch
        {
            LibraryCategory.Series => "Sèrie",
            LibraryCategory.Cinema => "Pel·lícula",
            LibraryCategory.Documentaries => "Documental",
            LibraryCategory.News => "Actualitat",
            LibraryCategory.Broadcasts => "Retransmissió",
            LibraryCategory.Featured => "Destacat",
            LibraryCategory.Archive => "Arxiu",
            _ => "Programa"
        };

    /// <summary>
    /// Gets the absolute URL of the poster of the program, preferring the biggest variant.
    /// </summary>
    /// <param name="program">Program of the catalog.</param>
    /// <returns>The URL, or <c>null</c> when IB3 publishes no artwork.</returns>
    internal static string? PosterUrl(Ib3Program? program)
        => Ib3Client.AbsoluteUrl(
            program?.Image?.Large ?? program?.Image?.Medium ?? program?.Image?.Small);

    private static IEnumerable<Ib3Category> Subcategories(Ib3Program program)
        => program.Categories?.SelectMany(category => category.Subcategories ?? []) ?? [];

    private static IEnumerable<string> Names(IEnumerable<Ib3Category>? categories)
        => (categories ?? [])
            .Select(category => category.Name?.Trim())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!);

    private static IReadOnlyCollection<string> Distinct(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = new List<string>();

        foreach (var value in values)
        {
            var text = value?.Trim();
            if (!string.IsNullOrEmpty(text) && seen.Add(text))
            {
                distinct.Add(text);
            }
        }

        return distinct;
    }
}

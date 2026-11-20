using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Client;

/// <summary>
/// Catalog reads of <see cref="Ib3Client"/>: the list of programs IB3 publishes and the resolution of a
/// reference typed by the user against it.
/// </summary>
/// <remarks>
/// The catalog changes slowly (a program a day at most), so it is read once and kept in memory for
/// <c>CatalogLifetime</c>. Every caller goes through it: the configuration page searches it, the
/// synchronization resolves the configured programs with it, and a program typed as a URL, a slug or a
/// title never costs another request.
/// </remarks>
public sealed partial class Ib3Client
{
    private const int DefaultScore = 1;

    private static readonly TimeSpan CatalogLifetime = TimeSpan.FromMinutes(10);
    private static readonly IReadOnlyList<Ib3Program> EmptyCatalog = [];

    private readonly SemaphoreSlim _catalogLock = new(1, 1);
    private IReadOnlyList<Ib3Program>? _catalog;
    private DateTimeOffset _catalogReadAt;

    /// <summary>
    /// Gets the catalog of programs, from memory when it is fresh enough.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The programs of the catalog.</returns>
    public Task<IReadOnlyList<Ib3Program>> GetCatalogAsync(CancellationToken cancellationToken)
        => GetCatalogAsync(refresh: false, cancellationToken);

    /// <summary>
    /// Gets the catalog of programs.
    /// </summary>
    /// <param name="refresh">Whether the catalog has to be read again even when the cached one is fresh.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The programs of the catalog.</returns>
    public async Task<IReadOnlyList<Ib3Program>> GetCatalogAsync(
        bool refresh,
        CancellationToken cancellationToken)
    {
        var cached = _catalog;
        if (!refresh && cached is not null && DateTimeOffset.UtcNow - _catalogReadAt < CatalogLifetime)
        {
            return cached;
        }

        await _catalogLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _catalog;
            if (!refresh && cached is not null && DateTimeOffset.UtcNow - _catalogReadAt < CatalogLifetime)
            {
                return cached;
            }

            var programs = await ReadCatalogAsync(cancellationToken).ConfigureAwait(false);

            // A failed read keeps the catalog the server already has, so a throttled refresh does not
            // empty the library.
            if (programs.Count > 0 || _catalog is null)
            {
                _catalog = programs;
                _catalogReadAt = DateTimeOffset.UtcNow;
            }

            return _catalog ?? EmptyCatalog;
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    /// <summary>
    /// Searches the catalog by title or slug.
    /// </summary>
    /// <param name="query">Text typed by the user; an empty query returns the newest programs.</param>
    /// <param name="limit">Maximum number of matches.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matches, best first.</returns>
    public async Task<IReadOnlyList<ProgramMatch>> SearchProgramsAsync(
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        var key = Ib3Text.Key(query);
        var matches = new List<ProgramMatch>(catalog.Count);

        foreach (var program in catalog)
        {
            if (string.IsNullOrWhiteSpace(program.Id))
            {
                continue;
            }

            var score = key.Length == 0 ? DefaultScore : Score(program, key);
            if (score <= 0)
            {
                continue;
            }

            matches.Add(new ProgramMatch(
                program.Id!,
                program.Slug ?? string.Empty,
                Ib3Text.Sentence(program.Title),
                score,
                Ib3Text.Sentence(program.Description),
                Ib3CategoryName(program)));
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Clamp(limit, 1, 200))
            .ToList();
    }

    /// <summary>
    /// Resolves a reference typed by the user into a program of the catalog.
    /// </summary>
    /// <param name="reference">
    /// The id of the program, the URL of its page (<c>https://totib3.org/play/tv/emergencies/e12</c>), its
    /// slug or its title.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The program, or <c>null</c> when IB3 publishes no program that fits the reference.</returns>
    public async Task<Ib3Program?> FindProgramAsync(string? reference, CancellationToken cancellationToken)
    {
        var value = reference?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParse(value, out var id))
        {
            var program = await GetProgramAsync(id.ToString(), cancellationToken).ConfigureAwait(false);
            if (program is not null)
            {
                return program;
            }
        }

        var catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (catalog.Count == 0)
        {
            return null;
        }

        foreach (var slug in ProgramSlugs(value))
        {
            var match = catalog.FirstOrDefault(program => Same(program.Slug, slug));
            if (match is not null)
            {
                return match;
            }
        }

        var byId = catalog.FirstOrDefault(program => Same(program.Id, value));
        if (byId is not null)
        {
            return byId;
        }

        var key = Ib3Text.Key(value);
        if (key.Length == 0)
        {
            return null;
        }

        var exact = catalog.FirstOrDefault(program => Ib3Text.Key(program.Title) == key);
        if (exact is not null)
        {
            return exact;
        }

        var partial = catalog
            .Where(program => Ib3Text.Key(program.Title).Contains(key, StringComparison.Ordinal))
            .OrderBy(program => Ib3Text.Key(program.Title).Length)
            .FirstOrDefault();

        if (partial is null)
        {
            _logger.LogWarning("IB3: no se encontró ningún programa para la referencia «{Reference}».", value);
        }

        return partial;
    }

    /// <summary>
    /// Gets the name of the first video category of a program («Sèries», «Documentals»…).
    /// </summary>
    /// <param name="program">Program of the catalog.</param>
    /// <returns>The name of the category, or <c>null</c> when IB3 files the program under none.</returns>
    /// <remarks>
    /// The catalog also files programs under audio categories with the same name as a video section, so the
    /// video ones are preferred; a program filed under neither keeps the first name it publishes.
    /// </remarks>
    internal static string? Ib3CategoryName(Ib3Program? program)
    {
        var name = Section(program)?.Name?.Trim();

        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Gets the name of the first subcategory of the section of a program («Documentals històrics», «IB3
    /// Júnior»...).
    /// </summary>
    /// <param name="program">Program of the catalog.</param>
    /// <returns>The name of the subcategory, or <c>null</c> when IB3 files the program under none.</returns>
    /// <remarks>
    /// The subcategory is read from the same category that names the section, so the subfolder always
    /// belongs to the section of the program.
    /// </remarks>
    internal static string? Ib3SubcategoryName(Ib3Program? program)
    {
        var subcategories = Section(program)?.Subcategories;

        if (subcategories is null)
        {
            return null;
        }

        return subcategories
            .Select(subcategory => subcategory.Name?.Trim())
            .FirstOrDefault(name => !string.IsNullOrEmpty(name));
    }

    /// <summary>
    /// Picks the category of the catalog that files a program under its section: the first one of a video
    /// section, in the order of the menu of totib3.org, and, for a program filed under none, the first one
    /// that has a name.
    /// </summary>
    /// <param name="program">Program of the catalog.</param>
    /// <returns>The category of the section, or <c>null</c> when the program publishes none.</returns>
    /// <remarks>
    /// <para>
    /// The catalog also files programs under audio categories that share their name with a video section
    /// («Actualitat», «Destacat», «Arxiu»), so the video ones win.
    /// </para>
    /// <para>
    /// A program can be published by two sections («Actualitat» and «Programes»), and the API does not
    /// keep a stable order, so the menu of the site decides which folder it goes to: the news section
    /// comes before the magazines, which is where the program is really filed.
    /// </para>
    /// </remarks>
    private static Ib3Category? Section(Ib3Program? program)
    {
        var categories = program?.Categories;
        if (categories is null || categories.Count == 0)
        {
            return null;
        }

        return categories
                .Where(category => Ib3Categories.IsVideo(category) && !string.IsNullOrWhiteSpace(category.Name))
                .OrderBy(category => Ib3Categories.OrderOf(category.Id))
                .FirstOrDefault()
            ?? categories.FirstOrDefault(category => !string.IsNullOrWhiteSpace(category.Name));
    }

    private static bool Same(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static int Score(Ib3Program program, string key)
    {
        var title = Ib3Text.Key(program.Title);
        if (title.Length == 0)
        {
            return 0;
        }

        if (title == key)
        {
            return 100;
        }

        var slug = Ib3Text.Key(program.Slug);
        if (slug == key)
        {
            return 90;
        }

        if (title.StartsWith(key, StringComparison.Ordinal) || slug.StartsWith(key, StringComparison.Ordinal))
        {
            return 80;
        }

        if (title.Contains(key, StringComparison.Ordinal))
        {
            return 60;
        }

        return key.Length >= 3 && slug.Contains(key, StringComparison.Ordinal) ? 40 : 0;
    }

    private async Task<IReadOnlyList<Ib3Program>> ReadCatalogAsync(CancellationToken cancellationToken)
    {
        var programs = new List<Ib3Program>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = string.Format(CultureInfo.InvariantCulture, SearchUrl, offset);
            var answer = await GetJsonAsync<Ib3Page<Ib3Program>>(url, cancellationToken).ConfigureAwait(false);

            if (answer?.Items is null || answer.Items.Count == 0)
            {
                break;
            }

            foreach (var program in answer.Items)
            {
                if (!string.IsNullOrWhiteSpace(program.Id) && seen.Add(program.Id))
                {
                    programs.Add(program);
                }
            }

            offset += answer.Items.Count;

            if (answer.Total > 0 && offset >= answer.Total)
            {
                break;
            }
        }

        if (programs.Count > 0)
        {
            _logger.LogInformation("IB3: {Count} programas en el catálogo.", programs.Count);
        }

        return programs;
    }
}

/// <summary>
/// A program of the catalog that fits a reference typed by the user.
/// </summary>
/// <param name="Id">Id of the program (the GUID of the API).</param>
/// <param name="Slug">Slug of the public page (<c>emergencies</c>).</param>
/// <param name="Title">Title of the program as the catalog publishes it.</param>
/// <param name="Score">Evidence that paired it: 100 is the exact title.</param>
/// <param name="Description">Description of the program, for the configuration page.</param>
/// <param name="Category">Category of IB3 the program is filed under.</param>
public sealed record ProgramMatch(
    string Id,
    string Slug,
    string Title,
    int Score,
    string? Description,
    string? Category);

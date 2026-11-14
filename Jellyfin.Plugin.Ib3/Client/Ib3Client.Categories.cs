using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Client;

/// <summary>
/// Category reads of <see cref="Ib3Client"/>: the sections of the platform and the programs each one
/// publishes, which is what lets the plugin replicate a whole section instead of a program at a time.
/// </summary>
/// <remarks>
/// The sections change less than a program a month, so they are read once and kept in memory; the programs
/// of a section are read on every run, because that list is what the synchronization mirrors.
/// </remarks>
public sealed partial class Ib3Client
{
    /// <summary>
    /// List of categories of the catalog. The API answers the video and the audio sections together.
    /// </summary>
    internal const string CategoriesUrl = "categories?offset={0}";

    /// <summary>
    /// Programs of a category, newest first.
    /// </summary>
    internal const string CategoryProgramsUrl = "searchPrograms?order=date&categoryId={0}&offset={1}";

    /// <summary>
    /// Programs of a subcategory, newest first.
    /// </summary>
    internal const string SubcategoryProgramsUrl =
        "searchPrograms?order=date&categoryId={0}&subcategoryId={1}&offset={2}";

    private static readonly TimeSpan CategoriesLifetime = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _categoriesLock = new(1, 1);
    private IReadOnlyList<Ib3CategoryInfo>? _categories;
    private DateTimeOffset _categoriesReadAt;

    /// <summary>
    /// Gets the video sections of the platform, from memory when the cached list is fresh enough.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The eight video sections, in the order of the menu.</returns>
    public Task<IReadOnlyList<Ib3CategoryInfo>> GetCategoriesAsync(CancellationToken cancellationToken)
        => GetCategoriesAsync(refresh: false, cancellationToken);

    /// <summary>
    /// Gets the video sections of the platform.
    /// </summary>
    /// <param name="refresh">Whether the list has to be read again even when the cached one is fresh.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The eight video sections, in the order of the menu.</returns>
    /// <remarks>
    /// When the endpoint cannot be read the static list of <see cref="Ib3Categories"/> is used: the
    /// configuration page still shows the sections of the site and the synchronization still knows where
    /// each program of a category belongs.
    /// </remarks>
    public async Task<IReadOnlyList<Ib3CategoryInfo>> GetCategoriesAsync(
        bool refresh,
        CancellationToken cancellationToken)
    {
        var cached = _categories;
        if (!refresh && cached is not null && DateTimeOffset.UtcNow - _categoriesReadAt < CategoriesLifetime)
        {
            return cached;
        }

        await _categoriesLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _categories;
            if (!refresh && cached is not null && DateTimeOffset.UtcNow - _categoriesReadAt < CategoriesLifetime)
            {
                return cached;
            }

            var sections = await ReadCategoriesAsync(cancellationToken).ConfigureAwait(false);

            if (sections.Count > 0)
            {
                _categories = sections;
                _categoriesReadAt = DateTimeOffset.UtcNow;
            }

            return _categories ?? Ib3Categories.Video;
        }
        finally
        {
            _categoriesLock.Release();
        }
    }

    /// <summary>
    /// Reads the programs of a category, newest first.
    /// </summary>
    /// <param name="categoryId">Id of the section.</param>
    /// <param name="subcategoryId">Id of the subcategory, or <c>null</c> for the whole section.</param>
    /// <param name="limit">Maximum number of programs (<c>0</c> for every one of them).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The programs of the category; empty when IB3 publishes none or cannot be read.</returns>
    /// <remarks>
    /// The endpoint answers 20 programs per page whatever the requested size, so the read follows the
    /// <c>offset</c> it publishes and stops on an empty page. Every program comes with its categories, so
    /// the synchronization needs no extra request to know where it belongs.
    /// </remarks>
    public async Task<IReadOnlyList<Ib3Program>> GetCategoryProgramsAsync(
        string? categoryId,
        string? subcategoryId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return [];
        }

        var max = limit > 0 ? limit : MaxPages * PageSize;
        var programs = new List<Ib3Program>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;

        for (var page = 0; page < MaxPages && programs.Count < max; page++)
        {
            var answer = await GetJsonAsync<Ib3Page<Ib3Program>>(
                ProgramsOfCategoryUrl(categoryId, subcategoryId, offset),
                cancellationToken).ConfigureAwait(false);

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

        return programs.Count > max ? programs[..max] : programs;
    }

    private static string ProgramsOfCategoryUrl(string categoryId, string? subcategoryId, int offset)
        => string.IsNullOrWhiteSpace(subcategoryId)
            ? string.Format(
                CultureInfo.InvariantCulture,
                CategoryProgramsUrl,
                Uri.EscapeDataString(categoryId.Trim()),
                offset)
            : string.Format(
                CultureInfo.InvariantCulture,
                SubcategoryProgramsUrl,
                Uri.EscapeDataString(categoryId.Trim()),
                Uri.EscapeDataString(subcategoryId.Trim()),
                offset);

    private async Task<IReadOnlyList<Ib3CategoryInfo>> ReadCategoriesAsync(CancellationToken cancellationToken)
    {
        var sections = new List<Ib3CategoryInfo>();
        var offset = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = string.Format(CultureInfo.InvariantCulture, CategoriesUrl, offset);
            var answer = await GetJsonAsync<Ib3Page<Ib3Category>>(url, cancellationToken).ConfigureAwait(false);

            if (answer?.Items is null || answer.Items.Count == 0)
            {
                break;
            }

            foreach (var category in answer.Items)
            {
                // The audio sections («Pòdcast», «Música»...) are skipped: they publish no video to write.
                if (!Ib3Categories.IsVideo(category) || string.IsNullOrWhiteSpace(category.Name))
                {
                    continue;
                }

                sections.Add(new Ib3CategoryInfo(
                    category.Id?.Trim() ?? string.Empty,
                    category.Name!.Trim(),
                    Subcategories(category)));
            }

            offset += answer.Items.Count;

            if (answer.Total > 0 && offset >= answer.Total)
            {
                break;
            }
        }

        // The API answers in its own order; the menu of the site has a stable one, so the sections are
        // sorted by it and an unknown one is appended at the end.
        return sections
            .OrderBy(section => Ib3Categories.OrderOf(section.Id))
            .ToList();
    }

    private static IReadOnlyList<Ib3SubcategoryInfo> Subcategories(Ib3Category category)
    {
        var subcategories = new List<Ib3SubcategoryInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subcategory in category.Subcategories ?? [])
        {
            var name = subcategory.Name?.Trim();

            if (string.IsNullOrEmpty(name) || !seen.Add(name))
            {
                continue;
            }

            subcategories.Add(new Ib3SubcategoryInfo(subcategory.Id?.Trim() ?? string.Empty, name));
        }

        return subcategories.Count > 0
            ? subcategories
            : Ib3Categories.Find(category.Id)?.Subcategories ?? [];
    }
}

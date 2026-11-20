using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Client;

/// <summary>
/// Reads the public JSON API of IB3 (<c>ib3media.org/api</c>): the catalog of programs
/// (<c>searchPrograms</c>), the detail of a program (<c>programs/{id}</c>) and its videos
/// (<c>medias?programId=…</c>).
/// </summary>
/// <remarks>
/// The API is the one the IB3 web player uses. It publishes the progressive MP4 URL and the WebVTT
/// subtitle URL in the detail of every video, so the plugin needs no player negotiation: a video is a
/// single JSON read away from a <c>.strm</c> file.
/// </remarks>
public sealed partial class Ib3Client : IDisposable
{
    /// <summary>
    /// Address the <see cref="HttpClient"/> of <see cref="PluginServiceRegistrator"/> is based on.
    /// </summary>
    public const string BaseAddress = "https://ib3media.org/api/";

    /// <summary>
    /// Page size the API answers with. The endpoint ignores the requested size, so the plugin reads the
    /// value the API publishes in every page.
    /// </summary>
    public const int PageSize = 20;

    /// <summary>
    /// Hard cap of pages, so a wrong <c>total</c> never turns into an endless loop.
    /// </summary>
    internal const int MaxPages = 200;

    /// <summary>
    /// Segments of the player of totib3.org that never name a program.
    /// </summary>
    private static readonly HashSet<string> PlayerSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "play", "tv", "radio", "cat"
    };

    /// <summary>
    /// Catalog of programs, newest first.
    /// </summary>
    internal const string SearchUrl = "searchPrograms?order=date&offset={0}";

    /// <summary>
    /// Detail of a program, with the seasons it publishes.
    /// </summary>
    internal const string ProgramUrl = "programs/{0}";

    /// <summary>
    /// Videos of a program, newest first.
    /// </summary>
    internal const string MediasUrl = "medias?programId={0}&order=date&offset={1}";

    private readonly HttpClient _httpClient;
    private readonly ILogger<Ib3Client> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ib3Client"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client configured by <see cref="PluginServiceRegistrator"/>.</param>
    /// <param name="logger">Logger.</param>
    public Ib3Client(HttpClient httpClient, ILogger<Ib3Client> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Releases the locks that guard the catalog of programs and the sections of the platform.
    /// </summary>
    /// <remarks>
    /// The <see cref="HttpClient"/> is owned by <see cref="IHttpClientFactory"/> and it is never disposed
    /// here; only the <see cref="SemaphoreSlim"/> of the cached catalog is, so the analyzer that flags a
    /// disposable field on a non-disposable type stays happy.
    /// </remarks>
    public void Dispose()
    {
        _catalogLock.Dispose();
        _categoriesLock.Dispose();
    }

    /// <summary>
    /// Turns a URL of the API into an absolute one. IB3 publishes the media and artwork URLs with no
    /// scheme (<c>media.ib3alacarta.com/…</c>).
    /// </summary>
    /// <param name="url">URL published by IB3.</param>
    /// <returns>The absolute URL, or <c>null</c> when IB3 publishes none.</returns>
    public static string? AbsoluteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var value = url.Trim();
        return value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? value
                : "https://" + value.TrimStart('/');
    }

    /// <summary>
    /// Builds the public page of a program, the address the configuration page shows.
    /// </summary>
    /// <param name="slug">Slug of the program (<c>emergencies</c>).</param>
    /// <returns>The public URL, or <c>null</c> when the program has no slug.</returns>
    /// <remarks>
    /// The platform files the programs under <c>/play/tv/</c>; the slug straight under the host answers
    /// 404 since the player of totib3.org replaced the old pages.
    /// </remarks>
    public static string? WebUrl(string? slug)
        => string.IsNullOrWhiteSpace(slug)
            ? null
            : "https://totib3.org/play/tv/" + slug.Trim().Trim('/');

    /// <summary>
    /// Reads the slugs a reference typed by the user may name.
    /// </summary>
    /// <param name="reference">
    /// A public URL (<c>https://totib3.org/play/tv/emergencies/e12</c>), a slug (<c>emergencies</c>) or the
    /// id of the program.
    /// </param>
    /// <returns>
    /// Every path segment that could be the slug of a program, in the order they appear, without
    /// repetitions.
    /// </returns>
    /// <remarks>
    /// The catalog publishes the slug of every program, so the plugin resolves the reference against the
    /// catalog it already keeps in memory instead of guessing which segment is the program: in
    /// <c>https://totib3.org/play/tv/emergencies/e12</c> the program is the third segment, and in
    /// <c>https://ib3.org/programa/emergencies</c> it is the second one. The segments the player adds
    /// (<c>play</c>, <c>tv</c>, <c>radio</c>...) and the ones that name an episode (<c>e12</c>,
    /// <c>t1e13</c>) are skipped, so a pasted address never costs a lookup that cannot match.
    /// </remarks>
    public static IReadOnlyList<string> ProgramSlugs(string? reference)
    {
        var value = reference?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var slugs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in value.Split(['/', '?', '#'], StringSplitOptions.RemoveEmptyEntries))
        {
            // The host of the URL and the files of the URL carry a dot; the scheme carries a colon.
            if (segment.Contains('.', StringComparison.Ordinal)
                || segment.Contains(':', StringComparison.Ordinal)
                || !SlugRegex().IsMatch(segment)
                || PlayerSegments.Contains(segment)
                || EpisodeRegex().IsMatch(segment))
            {
                continue;
            }

            var slug = segment.ToLowerInvariant();
            if (seen.Add(slug))
            {
                slugs.Add(slug);
            }
        }

        return slugs;
    }

    [GeneratedRegex(@"^[a-z0-9]+(?:[-_][a-z0-9]+)*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@"^(?:e\d+|t\d+e\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeRegex();
}

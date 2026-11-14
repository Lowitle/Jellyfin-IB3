using Jellyfin.Plugin.Ib3.Client;
using Jellyfin.Plugin.Ib3.Configuration;
using Jellyfin.Plugin.Ib3.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Sync;

/// <summary>
/// Episode level synchronization: numbering, stream URL, metadata and subtitles of every video.
/// </summary>
public sealed partial class Ib3SyncService
{
    /// <summary>
    /// Builds the episodes of a program from the videos IB3 publishes.
    /// </summary>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="program">Program of the catalog, with the seasons it publishes.</param>
    /// <param name="seriesTitle">Title of the series.</param>
    /// <param name="medias">Videos of the program, newest first.</param>
    /// <param name="progress">Progress (0-1), or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The episodes that can be played.</returns>
    /// <remarks>
    /// The numbering of IB3 is used when it is a number. A video numbered with a production code —the
    /// documentaries publish <c>100UGT-25</c>— takes the next free number of its season, in broadcast
    /// order. A video with no MP4 is a video whose rights expired: it is logged and left out.
    /// </remarks>
    private async Task<IReadOnlyList<LibraryEpisode>> BuildEpisodesAsync(
        PluginConfiguration configuration,
        Ib3Program program,
        string seriesTitle,
        IReadOnlyList<Ib3Media> medias,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var episodes = new List<LibraryEpisode>(medias.Count);
        var used = new Dictionary<int, HashSet<int>>();
        var defaultSeason = program.Seasons?.Where(season => season > 0).DefaultIfEmpty(1).Min() ?? 1;
        var ordered = medias
            .OrderBy(Aired)
            .ThenBy(media => media.Id, StringComparer.Ordinal)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var media = ordered[i];

            if (IsExpired(media))
            {
                _logger.LogDebug(
                    "IB3: el vídeo {VideoId} de «{Series}» dejó de publicarse el {Date}; se omite",
                    media.Id,
                    seriesTitle,
                    media.PublishOut);

                continue;
            }

            var url = Ib3Client.PickFile(media);
            if (url is null)
            {
                _logger.LogWarning(
                    "IB3: el vídeo {VideoId} de «{Series}» ({Title}) no publica MP4, "
                    + "normalmente porque ha caducado; se omite y se reintenta en la próxima actualización",
                    media.Id,
                    seriesTitle,
                    media.Title ?? media.Slug);

                continue;
            }

            var season = Ib3Text.ParseSeason(media.Season) ?? defaultSeason;
            var number = Reserve(used, season, Ib3Text.ParseEpisode(media.Episode));

            episodes.Add(new LibraryEpisode
            {
                Season = season,
                Episode = number,
                Title = Ib3Text.EpisodeTitle(media.Title ?? media.Slug, seriesTitle, number),
                Plot = Ib3Text.FromHtml(media.Description),
                Url = url,
                Subtitles = await GetSubtitlesAsync(configuration, media, cancellationToken)
                    .ConfigureAwait(false),
                Aired = Aired(media),
                Runtime = Runtime(media),
                ThumbnailUrl = Ib3Client.AbsoluteUrl(
                    media.Image?.Large ?? media.Image?.Medium ?? media.Image?.Small),
                VideoId = media.Id,
                Genres = [KindLabel(program)]
            });

            progress?.Report((i + 1d) / ordered.Count);
        }

        return episodes;
    }

    /// <summary>
    /// Reserves the number of an episode in its season: the one IB3 publishes, or the next free one.
    /// </summary>
    /// <param name="used">Numbers already taken, per season.</param>
    /// <param name="season">Season of the episode.</param>
    /// <param name="published">Number IB3 publishes, or <c>null</c> when the field is not a number.</param>
    /// <returns>A number no other episode of the season uses.</returns>
    internal static int Reserve(Dictionary<int, HashSet<int>> used, int season, int? published)
    {
        if (!used.TryGetValue(season, out var numbers))
        {
            numbers = [];
            used[season] = numbers;
        }

        var number = published is > 0 ? published.Value : 1;

        while (!numbers.Add(number))
        {
            number++;
        }

        return number;
    }

    /// <summary>
    /// Tells whether IB3 has stopped publishing a video (<c>publishOut</c> in the past).
    /// </summary>
    /// <param name="media">Video of a program.</param>
    /// <returns><c>true</c> when the rights of the video expired.</returns>
    internal static bool IsExpired(Ib3Media media)
        => Ib3Text.ParseDate(media.PublishOut) is { } until && until < DateTimeOffset.Now;

    /// <summary>
    /// Reads the broadcast date of a video.
    /// </summary>
    /// <param name="media">Video of a program.</param>
    /// <returns>The date, or <c>null</c> when IB3 publishes none.</returns>
    private static DateTimeOffset? Aired(Ib3Media media) => Ib3Text.ParseDate(media.Broadcast);

    /// <summary>
    /// Reads the duration of a video.
    /// </summary>
    /// <param name="media">Video of a program.</param>
    /// <returns>The duration, or <c>null</c> when IB3 publishes none.</returns>
    private static TimeSpan? Runtime(Ib3Media media)
        => media.Media?.Duration is > 0 and var seconds
            ? TimeSpan.FromSeconds(seconds)
            : null;

    /// <summary>
    /// Downloads and converts the subtitles of a video.
    /// </summary>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="media">Video of a program.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The subtitles to write; empty when the video publishes none.</returns>
    private async Task<IReadOnlyList<LibrarySubtitle>> GetSubtitlesAsync(
        PluginConfiguration configuration,
        Ib3Media media,
        CancellationToken cancellationToken)
    {
        if (!configuration.WriteSubtitles)
        {
            return [];
        }

        var url = Ib3Client.PickSubtitle(media);
        if (url is null)
        {
            return [];
        }

        var vtt = await _client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(vtt))
        {
            _logger.LogWarning(
                "IB3: no se pudo descargar el subtítulo del vídeo {VideoId} ({Url}); "
                + "se reintentará en la próxima sincronización",
                media.Id,
                url);

            return [];
        }

        try
        {
            var srt = VttToSrt.Convert(vtt);
            return srt.Length == 0 ? [] : [new LibrarySubtitle(SubtitleLanguage(url), srt)];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "IB3: no se pudo convertir el subtítulo del vídeo {VideoId}",
                media.Id);

            return [];
        }
    }

    /// <summary>
    /// Guesses the language of a subtitle from its URL.
    /// </summary>
    /// <param name="url">WebVTT URL.</param>
    /// <returns>The ISO language code, Catalan when the URL says nothing.</returns>
    /// <remarks>
    /// IB3 publishes practically the whole catalog in Catalan and the URL carries no language field in
    /// the API: the tokens of the file name («ca», «es», «en») are the only hint, and Catalan is the
    /// least wrong guess for a URL that has none.
    /// </remarks>
    internal static string SubtitleLanguage(string? url)
    {
        var path = url ?? string.Empty;
        var query = path.IndexOfAny(['?', '#']);
        if (query >= 0)
        {
            path = path[..query];
        }

        var name = path.Split('/').LastOrDefault() ?? string.Empty;

        foreach (var token in name.ToLowerInvariant().Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token)
            {
                case "ca" or "cat" or "catala" or "català":
                    return "ca";
                case "es" or "esp" or "spa":
                    return "es";
                case "en" or "eng" or "angles":
                    return "en";
            }
        }

        return "ca";
    }
}

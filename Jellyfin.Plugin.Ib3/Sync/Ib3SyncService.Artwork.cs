using Jellyfin.Plugin.Ib3.Client;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Sync;

/// <summary>
/// Series artwork: the poster is saved next to the <c>tvshow.nfo</c>, so the library shows a picture
/// without any online metadata provider.
/// </summary>
public sealed partial class Ib3SyncService
{
    /// <summary>
    /// Saves the poster of a program in its series folder.
    /// </summary>
    /// <param name="program">Program of the catalog.</param>
    /// <param name="seriesPath">Series folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task SavePosterAsync(
        Ib3Program program,
        string seriesPath,
        CancellationToken cancellationToken)
    {
        var url = PosterUrl(program);
        if (url is null)
        {
            return;
        }

        var path = Path.Combine(seriesPath, "poster" + ImageExtension(url));

        try
        {
            await _client.DownloadIfMissingAsync(url, path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The episodes are already written: a picture that could not be downloaded must not abort the
            // synchronization, and the next run downloads it again.
            _logger.LogWarning(
                ex,
                "IB3: no se pudo guardar el póster de {Path}",
                seriesPath);
        }
    }

    /// <summary>
    /// Gets the extension Jellyfin expects for a local image. The extension is kept from the URL so the
    /// file name and the content always agree.
    /// </summary>
    /// <param name="url">Image URL.</param>
    /// <returns>The extension, including the dot.</returns>
    internal static string ImageExtension(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (extension is ".jpg" or ".jpeg" or ".png" or ".webp")
            {
                return extension;
            }
        }

        return ".jpg";
    }
}

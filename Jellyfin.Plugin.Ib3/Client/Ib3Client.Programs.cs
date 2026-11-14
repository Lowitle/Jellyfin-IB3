using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Client;

/// <summary>
/// Program and video reads of <see cref="Ib3Client"/>.
/// </summary>
public sealed partial class Ib3Client
{
    /// <summary>
    /// Hard cap of videos read for a program, so a wrong <c>total</c> never turns into an endless loop.
    /// </summary>
    internal const int MaxMedias = 5000;

    /// <summary>
    /// Reads the detail of a program, with the seasons it publishes.
    /// </summary>
    /// <param name="programId">Id of the program.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The program, or <c>null</c> when IB3 does not serve it.</returns>
    public async Task<Ib3Program?> GetProgramAsync(string? programId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(programId))
        {
            return null;
        }

        var url = string.Format(
            CultureInfo.InvariantCulture,
            ProgramUrl,
            Uri.EscapeDataString(programId.Trim()));

        return await GetJsonAsync<Ib3Program>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads every video of a program, newest first.
    /// </summary>
    /// <param name="programId">Id of the program.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The videos of the program; empty when IB3 publishes none.</returns>
    /// <remarks>
    /// The endpoint answers 20 videos per page whatever the requested size, and it answers
    /// <c>total: 0</c> for an unknown program, so the read stops on an empty page and never trusts the
    /// total.
    /// </remarks>
    public async Task<IReadOnlyList<Ib3Media>> GetMediasAsync(
        string? programId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(programId))
        {
            return [];
        }

        var medias = new List<Ib3Media>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;

        for (var page = 0; page < MaxPages && medias.Count < MaxMedias; page++)
        {
            var url = string.Format(
                CultureInfo.InvariantCulture,
                MediasUrl,
                Uri.EscapeDataString(programId.Trim()),
                offset);

            var answer = await GetJsonAsync<Ib3Page<Ib3Media>>(url, cancellationToken).ConfigureAwait(false);

            if (answer?.Items is null || answer.Items.Count == 0)
            {
                break;
            }

            foreach (var media in answer.Items)
            {
                if (!string.IsNullOrWhiteSpace(media.Id) && seen.Add(media.Id))
                {
                    medias.Add(media);
                }
            }

            offset += answer.Items.Count;

            if (answer.Total > 0 && offset >= answer.Total)
            {
                break;
            }
        }

        return medias;
    }

    /// <summary>
    /// Gets the progressive MP4 URL of a video.
    /// </summary>
    /// <param name="media">Video of a program.</param>
    /// <returns>The absolute URL, or <c>null</c> when IB3 publishes no playable file.</returns>
    /// <remarks>
    /// IB3 publishes the file URL with no scheme (<c>media.ib3alacarta.com/…mp4</c>). A video with no
    /// file is a video whose rights expired: the caller counts it and leaves it out of the library.
    /// </remarks>
    public static string? PickFile(Ib3Media? media)
        => AbsoluteUrl(media?.Media?.Filename);

    /// <summary>
    /// Gets the WebVTT subtitle URL of a video.
    /// </summary>
    /// <param name="media">Video of a program.</param>
    /// <returns>The absolute URL, or <c>null</c> when the video publishes no subtitle.</returns>
    public static string? PickSubtitle(Ib3Media? media)
    {
        var track = media?.Media?.Track;
        return string.IsNullOrWhiteSpace(track) ? null : AbsoluteUrl(track);
    }
}

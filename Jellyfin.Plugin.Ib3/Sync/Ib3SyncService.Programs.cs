using Jellyfin.Plugin.Ib3.Client;
using Jellyfin.Plugin.Ib3.Configuration;
using Jellyfin.Plugin.Ib3.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Sync;

/// <summary>
/// Program level synchronization: resolves each program and writes its series folder.
/// </summary>
public sealed partial class Ib3SyncService
{
    /// <summary>
    /// Synchronizes one program and returns the number of files written.
    /// </summary>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="reference">Program to synchronize.</param>
    /// <param name="progress">Progress of the program (0-1), or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of files written.</returns>
    private async Task<int> SyncProgramAsync(
        PluginConfiguration configuration,
        Ib3ProgramReference reference,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var program = await ResolveProgramAsync(reference, cancellationToken).ConfigureAwait(false);
        if (program is null)
        {
            _logger.LogWarning(
                "IB3: no se encontró ningún programa para «{Reference}»; se omite hasta la próxima actualización",
                reference.Reference.Length > 0 ? reference.Reference : reference.Id);

            progress?.Report(1);
            return 0;
        }

        var programId = string.IsNullOrWhiteSpace(program.Id) ? reference.Id : program.Id!.Trim();
        if (string.IsNullOrWhiteSpace(programId))
        {
            _logger.LogWarning(
                "IB3: el programa «{Series}» no publica id; se omite",
                program.Title ?? reference.Title);

            progress?.Report(1);
            return 0;
        }

        var seriesTitle = SeriesTitle(program, reference);
        var medias = await _client.GetMediasAsync(programId, cancellationToken).ConfigureAwait(false);

        // A program with no video is usually a failure of the API, not a program that has emptied its
        // catalog: its folder is left as it is.
        if (medias.Count == 0)
        {
            _logger.LogWarning(
                "IB3: «{Series}» no publica ningún vídeo; no se toca su carpeta",
                seriesTitle);

            progress?.Report(1);
            return 0;
        }

        var series = new LibrarySeries
        {
            Title = seriesTitle,
            ProgramId = programId,
            Slug = program.Slug ?? reference.Slug,
            Plot = Ib3Text.FromHtml(program.Description),
            Genres = Genres(program),
            Tags = Tags(program),
            Country = "ES",
            PosterUrl = PosterUrl(program)
        };

        var episodes = await BuildEpisodesAsync(
            configuration,
            program,
            seriesTitle,
            medias,
            new Progress<double>(fraction =>
                progress?.Report(0.1 + (Math.Clamp(fraction, 0d, 1d) * 0.8))),
            cancellationToken).ConfigureAwait(false);

        if (episodes.Count == 0)
        {
            _logger.LogWarning(
                "IB3: «{Series}» no publica ningún vídeo reproducible ({Videos} vídeos); no se toca su carpeta",
                seriesTitle,
                medias.Count);

            progress?.Report(1);
            return 0;
        }

        var options = new LibraryWriteOptions
        {
            WriteNfo = configuration.WriteNfo,
            WriteSubtitles = configuration.WriteSubtitles,
            DeleteObsolete = configuration.DeleteObsoleteFiles
        };

        var result = StrmLibrary.Write(
            RootPath(configuration, program, reference),
            series,
            episodes,
            options);

        if (configuration.WriteArtwork)
        {
            await SavePosterAsync(program, result.SeriesPath, cancellationToken).ConfigureAwait(false);
        }

        // A program that changed its title, its kind or its id is written to another folder: the folder of
        // the previous run is removed so the library does not keep the same program twice.
        var removed = LibraryPath.SamePath(reference.WrittenPath, result.SeriesPath)
            ? 0
            : LibraryCleanup.TryDeleteProgram(reference.WrittenPath, programId);

        if (removed > 0)
        {
            _logger.LogInformation(
                "IB3: carpeta anterior de «{Series}» eliminada ({Path})",
                seriesTitle,
                reference.WrittenPath);
        }

        reference.Id = programId;
        reference.Slug = program.Slug ?? reference.Slug;
        reference.Title = seriesTitle;
        reference.WrittenPath = result.SeriesPath;

        _libraryMonitor.ReportFileSystemChanged(result.SeriesPath);

        _logger.LogInformation(
            "IB3: «{Series}» → {Episodes} episodios ({Videos} vídeos, {Skipped} sin URL), "
            + "{Files} archivos escritos, {Removed} obsoletos eliminados",
            seriesTitle,
            result.EpisodesWritten,
            medias.Count,
            medias.Count - episodes.Count,
            result.FilesWritten,
            result.FilesRemoved);

        progress?.Report(1);
        return result.FilesWritten;
    }

    /// <summary>
    /// Resolves the program of a reference: the id first, then whatever the user typed (URL, slug, title).
    /// </summary>
    /// <param name="reference">Program reference of the configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The program, or <c>null</c> when IB3 publishes none that fits.</returns>
    private async Task<Ib3Program?> ResolveProgramAsync(
        Ib3ProgramReference reference,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(reference.Id, out var id))
        {
            var program = await _client.GetProgramAsync(id.ToString(), cancellationToken)
                .ConfigureAwait(false);

            if (program is not null)
            {
                return program;
            }
        }

        foreach (var candidate in new[] { reference.Reference, reference.Slug, reference.Id, reference.Title })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var program = await _client.FindProgramAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (program is not null)
            {
                return program;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the folder the series is written to: the destination folder itself, or the folder of the
    /// kind of program when the layout is on.
    /// </summary>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="program">Program of the catalog.</param>
    /// <param name="reference">Program reference.</param>
    /// <returns>The folder of the series, relative to the library root.</returns>
    private static string RootPath(
        PluginConfiguration configuration,
        Ib3Program program,
        Ib3ProgramReference reference)
    {
        var destination = configuration.DestinationPath.Trim();

        return configuration.OrganizeByKind
            ? LibraryCategory.Folder(
                destination,
                LibraryCategory.Resolve(Ib3Client.Ib3CategoryName(program), reference.CategoryOverride))
            : destination;
    }
}

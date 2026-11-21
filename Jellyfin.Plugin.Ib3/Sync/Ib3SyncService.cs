using Jellyfin.Plugin.Ib3.Client;
using Jellyfin.Plugin.Ib3.Configuration;
using Jellyfin.Plugin.Ib3.Library;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Sync;

/// <summary>
/// Mirrors the configured IB3 programs into <c>.strm</c> files, <c>.nfo</c> metadata and subtitles.
/// </summary>
/// <remarks>
/// A run is a best effort per program: a program IB3 does not serve, or whose folder cannot be written,
/// is logged and skipped so the rest of the library is still refreshed.
/// </remarks>
public sealed partial class Ib3SyncService
{
    private readonly Ib3Client _client;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<Ib3SyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ib3SyncService"/> class.
    /// </summary>
    /// <param name="client">IB3 API client.</param>
    /// <param name="libraryMonitor">Library monitor, notified after each series is updated.</param>
    /// <param name="logger">Logger.</param>
    public Ib3SyncService(
        Ib3Client client,
        ILibraryMonitor libraryMonitor,
        ILogger<Ib3SyncService> logger)
    {
        _client = client;
        _libraryMonitor = libraryMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Synchronizes every configured program.
    /// </summary>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="progress">Progress (0-100), or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task RunAsync(
        PluginConfiguration configuration,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var references = (configuration.Programs ?? [])
            .Where(program => !string.IsNullOrWhiteSpace(program.Id)
                || !string.IsNullOrWhiteSpace(program.Reference))
            .ToList();

        if (references.Count == 0)
        {
            _logger.LogWarning("IB3: no hay programas configurados, no se genera nada");
            progress?.Report(100);
            return;
        }

        if (string.IsNullOrWhiteSpace(configuration.DestinationPath))
        {
            throw new InvalidOperationException(
                "IB3: configura la carpeta de destino antes de actualizar.");
        }

        // Fails fast with an actionable message when the folder is not mounted inside the container or
        // Jellyfin cannot write into it.
        LibraryPath.EnsureWritableRoot(configuration.DestinationPath);
        _logger.LogInformation("IB3: carpeta de destino {Path}", configuration.DestinationPath.Trim());

        var totalWritten = 0;
        for (var i = 0; i < references.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = i;

            try
            {
                totalWritten += await SyncProgramAsync(
                    configuration,
                    references[index],
                    new Progress<double>(fraction => progress?.Report(
                        ((index + Math.Clamp(fraction, 0d, 1d)) / references.Count) * 100d)),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "IB3: error al sincronizar el programa {Program}",
                    references[index].Id);
            }
        }

        // A program that moves out of the folder that grouped it —the subcategory folders of the versions
        // before the layout was dropped— leaves it empty: the empty folder goes with it, so Jellyfin does
        // not read it as one more series.
        foreach (var folder in LibraryCategory.RemoveEmptySubcategoryFolders(configuration.DestinationPath))
        {
            _logger.LogInformation(
                "IB3: subcarpeta vacía de una versión anterior eliminada ({Path})",
                folder);
        }

        progress?.Report(100);
        SaveRunState(references);
        _logger.LogInformation(
            "IB3: sincronización completada, {Files} archivos escritos",
            totalWritten);
    }

    /// <summary>
    /// Stores the series folder and the title each program was written with, so the next run can clean
    /// up after a program that changed its title, its kind or its id.
    /// </summary>
    /// <param name="references">Programs of the run, with what was written for each one.</param>
    /// <remarks>
    /// The configuration is read again from the plugin instead of saving the object of the run: the user
    /// may have saved the page while the task was writing, and that save must not be undone.
    /// </remarks>
    private void SaveRunState(IReadOnlyList<Ib3ProgramReference> references)
    {
        var plugin = Plugin.Instance;
        if (plugin is null || plugin.Configuration.Programs is not { Count: > 0 } programs)
        {
            return;
        }

        var configuration = plugin.Configuration;
        var changed = false;

        foreach (var program in programs)
        {
            var reference = references.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, program.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Reference, program.Reference, StringComparison.OrdinalIgnoreCase));

            if (reference is null)
            {
                continue;
            }

            changed |= Copy(reference.Id, program.Id, value => program.Id = value);
            changed |= Copy(reference.Slug, program.Slug, value => program.Slug = value);
            changed |= Copy(reference.Title, program.Title, value => program.Title = value);
            changed |= Copy(reference.WrittenPath, program.WrittenPath, value => program.WrittenPath = value);
        }

        if (!changed)
        {
            return;
        }

        try
        {
            plugin.UpdateConfiguration(configuration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The state is only a hint for the next run: the series themselves are already written.
            _logger.LogWarning(ex, "IB3: no se pudo guardar la carpeta escrita de los programas");
        }
    }

    /// <summary>
    /// Copies <paramref name="value"/> into the stored field when it changed.
    /// </summary>
    /// <param name="value">Value written by the run.</param>
    /// <param name="stored">Value the configuration holds now.</param>
    /// <param name="assign">Setter of the stored field.</param>
    /// <returns><c>true</c> when the field changed.</returns>
    private static bool Copy(string? value, string? stored, Action<string> assign)
    {
        if (value is null || string.Equals(value, stored, StringComparison.Ordinal))
        {
            return false;
        }

        assign(value);
        return true;
    }
}

using Jellyfin.Plugin.Ib3.Configuration;
using Jellyfin.Plugin.Ib3.Sync;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Tasks;

/// <summary>
/// Scheduled task that mirrors the configured IB3 programs into the library folder.
/// </summary>
public sealed class Ib3RefreshTask : IScheduledTask
{
    /// <summary>
    /// Key of the scheduled task (stable across versions).
    /// </summary>
    public const string TaskKey = "Ib3Refresh";

    private const int DefaultIntervalHours = 12;

    private readonly Ib3SyncService _syncService;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ib3RefreshTask"/> class.
    /// </summary>
    /// <param name="syncService">Synchronization service.</param>
    /// <param name="logger">Logger.</param>
    public Ib3RefreshTask(Ib3SyncService syncService, ILogger<Ib3RefreshTask> logger)
    {
        _syncService = syncService;
        ArgumentNullException.ThrowIfNull(logger);
    }

    /// <inheritdoc />
    public string Name => "Actualizar IB3";

    /// <inheritdoc />
    public string Key => TaskKey;

    /// <inheritdoc />
    public string Description =>
        "Descarga el catálogo de los programas de IB3 configurados y regenera los archivos .strm, .nfo y los subtítulos de la biblioteca.";

    /// <inheritdoc />
    public string Category => "IB3";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return _syncService.RunAsync(configuration, progress, cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => BuildTriggers(Plugin.Instance?.Configuration.RefreshIntervalHours ?? DefaultIntervalHours);

    /// <summary>
    /// Builds the triggers of the task from the configured refresh interval.
    /// </summary>
    /// <param name="refreshIntervalHours">Interval, in hours (values lower than 1 fall back to the default).</param>
    /// <returns>The triggers.</returns>
    internal static IReadOnlyList<TaskTriggerInfo> BuildTriggers(int refreshIntervalHours)
    {
        var hours = refreshIntervalHours < 1 ? DefaultIntervalHours : refreshIntervalHours;

        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(hours).Ticks
            },
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.StartupTrigger
            }
        ];
    }
}

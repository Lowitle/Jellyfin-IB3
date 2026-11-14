using Jellyfin.Plugin.Ib3.Configuration;
using Jellyfin.Plugin.Ib3.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3;

/// <summary>
/// Applies the configured refresh interval to the scheduled task.
/// </summary>
public static class Ib3TriggerScheduler
{
    /// <summary>
    /// Updates the triggers of <see cref="Ib3RefreshTask"/> with the configured interval.
    /// </summary>
    /// <param name="taskManager">Task manager.</param>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns><c>true</c> when the task was found and updated.</returns>
    public static bool Apply(ITaskManager taskManager, PluginConfiguration configuration, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(taskManager);
        ArgumentNullException.ThrowIfNull(configuration);

        var worker = taskManager.ScheduledTasks
            .FirstOrDefault(task => task.ScheduledTask is Ib3RefreshTask);

        if (worker is null)
        {
            logger?.LogWarning("IB3: la tarea programada no está registrada todavía; reinicia el servidor");
            return false;
        }

        worker.Triggers = Ib3RefreshTask.BuildTriggers(configuration.RefreshIntervalHours);
        worker.ReloadTriggerEvents();

        logger?.LogInformation(
            "IB3: intervalo de actualización aplicado ({Hours} h)",
            configuration.RefreshIntervalHours);

        return true;
    }
}

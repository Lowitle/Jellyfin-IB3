using Jellyfin.Plugin.Ib3.Client;
using Jellyfin.Plugin.Ib3.Sync;
using Jellyfin.Plugin.Ib3.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Ib3;

/// <summary>
/// Registers the plugin services in the Jellyfin container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <summary>
    /// User agent sent to IB3.
    /// </summary>
    public const string UserAgent = "JellyfinIb3/1.0 (+https://github.com/jellyfin)";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient<Ib3Client>(client =>
        {
            client.BaseAddress = new Uri(Ib3Client.BaseAddress, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(100);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ca-ES,ca;q=0.9,es;q=0.8");
        });

        serviceCollection.AddSingleton<Ib3SyncService>();
        serviceCollection.AddSingleton<IScheduledTask, Ib3RefreshTask>();
    }
}

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// The plugin id is used by Jellyfin to locate the plugin data folder and the configuration file.
[assembly: Guid("6a1d7f43-9c2b-4e58-b0a4-3f7c1d9e5b28")]

// The project generates its own assembly attributes (GenerateAssemblyInfo is off), so the version of
// Directory.Build.props has to be applied by hand: the plugin catalog and the update check read it.
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// The unit tests and the probe tool exercise the internal helpers (NfoBuilder, Ib3Text, VttToSrt...).
[assembly: InternalsVisibleTo("Jellyfin.Plugin.Ib3.Tests")]
[assembly: InternalsVisibleTo("Jellyfin.Plugin.Ib3.Probe")]

using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Library;

/// <summary>
/// Describes the section folders of the destination folder and the libraries that index them, and adds a
/// section to a library the user already has.
/// </summary>
/// <remarks>
/// <para>
/// Folders are not exclusive: Jellyfin indexes a folder in every library that points at it, so a section
/// can be part of «IB3» and of a curated library of the user («Documentals», «Sèries», «IB3 Júnior»...) at
/// the same time without copying a single file.
/// </para>
/// <para>
/// <see cref="LibraryProvisioner"/> creates the library that gathers every section; the table built from
/// <see cref="Describe"/> is the manual half: one row per section, the libraries of the server and the
/// folder of the section, so «Arxiu» can be sent to an existing library without touching the rest.
/// </para>
/// <para>
/// Nothing is persisted: the association is applied to Jellyfin when the page asks for it, and the next
/// read of the table shows the library as one of the owners of the folder.
/// </para>
/// </remarks>
public static class LibrarySections
{
    /// <summary>
    /// Describes the section folders of the destination and the libraries of the server.
    /// </summary>
    /// <remarks>
    /// The sections are always listed, even with «Separar los programas en carpetas por sección» off: the
    /// table then says which folders the library would index with the layout on.
    /// </remarks>
    /// <param name="path">Destination folder of the plugin.</param>
    /// <param name="name">Library name of the plugin; «IB3» when empty.</param>
    /// <param name="bySection">Whether the synchronization writes a folder per section.</param>
    /// <param name="libraries">Libraries already configured in the server.</param>
    /// <returns>The section folders and the libraries of the server.</returns>
    public static LibrarySectionMatrix Describe(
        string path,
        string? name,
        bool bySection,
        IEnumerable<LibraryFolder>? libraries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var root = LibraryIO.Normalize(path)
            ?? throw new ArgumentException("La carpeta de destino no es una ruta válida.", nameof(path));
        var libraryName = string.IsNullOrWhiteSpace(name) ? LibraryProvisioner.DefaultLibraryName : name.Trim();
        var all = libraries?.Where(library => library is not null).ToList() ?? [];

        var own = all.FirstOrDefault(library =>
            string.Equals(library.Name, libraryName, StringComparison.OrdinalIgnoreCase));

        // A library that still points at the destination folder indexes the sections too, because the
        // folder contains them: that is the setup the library button migrates to one folder per section.
        IReadOnlyList<string> ownLocations = own?.Locations ?? [];
        var coversEverything = ownLocations.Any(location => LibraryPath.SamePath(location, root));

        var folders = LibraryCategory.Sections(root)
            .Select(folder => new LibrarySectionFolder(
                Path.GetFileName(folder),
                folder,
                Directory.Exists(folder),
                coversEverything || ownLocations.Any(location => LibraryPath.SamePath(location, folder)),
                all.Where(library => library.Locations.Any(location => LibraryPath.SamePath(location, folder)))
                    .Select(library => library.Name)
                    .ToArray()))
            .ToArray();

        return new LibrarySectionMatrix(
            root,
            libraryName,
            bySection,
            own is not null,
            folders,
            all.Select(library => new LibrarySummary(
                library.Name,
                TypeLabel(library.CollectionType),
                library.IsTvShows,
                library.Locations))
                .ToArray(),
            folders.Where(folder => !folder.Covered).Select(folder => folder.Section).ToArray());
    }

    /// <summary>
    /// Decides whether a section folder has to be added to a library, without touching the server.
    /// </summary>
    /// <param name="folder">Folder of the section.</param>
    /// <param name="libraryName">Library that has to index the folder.</param>
    /// <param name="libraries">Libraries already configured in the server.</param>
    /// <returns>The plan to apply.</returns>
    public static LibraryLinkPlan PlanAssociation(
        string? folder,
        string? libraryName,
        IEnumerable<LibraryFolder>? libraries)
    {
        var normalized = LibraryIO.Normalize(folder);

        if (normalized is null)
        {
            return new LibraryLinkPlan(
                LibraryLinkAction.Invalid,
                folder?.Trim() ?? string.Empty,
                null,
                "Indica la carpeta de la sección que quieres añadir a una biblioteca.");
        }

        var name = libraryName?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            return new LibraryLinkPlan(
                LibraryLinkAction.Invalid,
                normalized,
                null,
                "Elige la biblioteca a la que quieres añadir la sección.");
        }

        var all = libraries?.Where(library => library is not null).ToList() ?? [];
        var library = all.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

        if (library is null)
        {
            return new LibraryLinkPlan(
                LibraryLinkAction.Invalid,
                normalized,
                name,
                $"La biblioteca «{name}» no existe en este Jellyfin. Actualiza la página si la acabas de crear.");
        }

        if (library.Locations.Any(location => LibraryPath.SamePath(location, normalized)))
        {
            return new LibraryLinkPlan(
                LibraryLinkAction.Existing,
                normalized,
                library.Name,
                $"La carpeta «{Path.GetFileName(normalized)}» ya está en la biblioteca «{library.Name}»"
                + $" ({TypeLabel(library.CollectionType)}).");
        }

        return new LibraryLinkPlan(
            LibraryLinkAction.Added,
            normalized,
            library.Name,
            $"La carpeta «{Path.GetFileName(normalized)}» se añade a la biblioteca «{library.Name}»"
            + $" ({TypeLabel(library.CollectionType)})."
            + (library.IsTvShows
                ? string.Empty
                : " La biblioteca no es de tipo «Programas de TV», así que Jellyfin puede no reconocer"
                    + " las series que hay dentro."));
    }

    /// <summary>
    /// Adds the folder of a section to a library of the server.
    /// </summary>
    /// <remarks>
    /// The folder is not removed from the library of the plugin: Jellyfin indexes it in both libraries. The
    /// status is always returned with a <c>success</c> flag, so the page can show the reason of a rejection
    /// instead of a generic error.
    /// </remarks>
    /// <param name="libraryManager">Library manager of the server.</param>
    /// <param name="folder">Folder of the section.</param>
    /// <param name="libraryName">Library that has to index the folder.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="libraryMonitor">Library monitor used to tell Jellyfin about the new folder.</param>
    /// <returns>The result of the operation.</returns>
    public static LibraryLinkStatus Associate(
        ILibraryManager libraryManager,
        string? folder,
        string? libraryName,
        ILogger logger,
        ILibraryMonitor? libraryMonitor = null)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(logger);

        var libraries = libraryManager.GetVirtualFolders() ?? [];
        var plan = PlanAssociation(folder, libraryName, ToLibraries(libraries));

        switch (plan.Action)
        {
            case LibraryLinkAction.Invalid:
                return new LibraryLinkStatus(false, "invalid", plan.Folder, plan.LibraryName, plan.Message);

            case LibraryLinkAction.Existing:
                return new LibraryLinkStatus(true, "existing", plan.Folder, plan.LibraryName, plan.Message);

            default:
                // The folder is created before touching the library, so Jellyfin never stores a path that
                // does not exist yet (the synchronization only creates it when it writes its first program).
                EnsureFolder(plan.Folder, logger);

                try
                {
                    libraryManager.AddMediaPath(plan.LibraryName!, new MediaPathInfo(plan.Folder));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                    or IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(
                        "IB3: Jellyfin rechazó la carpeta {Folder} para {Library}: {Message}",
                        plan.Folder,
                        plan.LibraryName,
                        ex.Message);

                    return new LibraryLinkStatus(
                        false,
                        "invalid",
                        plan.Folder,
                        plan.LibraryName,
                        $"Jellyfin no aceptó la carpeta «{Path.GetFileName(plan.Folder)}» en"
                        + $" «{plan.LibraryName}»: {ex.Message}");
                }

                libraryMonitor?.ReportFileSystemChanged(plan.Folder);

                logger.LogInformation(
                    "IB3: sección {Folder} añadida a la biblioteca {Library}",
                    plan.Folder,
                    plan.LibraryName);

                return new LibraryLinkStatus(true, "added", plan.Folder, plan.LibraryName, plan.Message);
        }
    }

    /// <summary>
    /// Reduces the libraries of the server to the data the planner needs, so it can be checked without the
    /// Jellyfin assemblies.
    /// </summary>
    /// <param name="libraries">Libraries of the server.</param>
    /// <returns>The same libraries as plain records.</returns>
    public static LibraryFolder[] ToLibraries(IEnumerable<VirtualFolderInfo> libraries)
        => libraries
            .Select(library => new LibraryFolder(
                library.Name ?? string.Empty,
                library.CollectionType == CollectionTypeOptions.tvshows,
                library.Locations?.Where(location => !string.IsNullOrWhiteSpace(location)).ToArray() ?? [],
                library.CollectionType?.ToString()))
            .ToArray();

    /// <summary>
    /// Gets the name of a collection type in the language of the page.
    /// </summary>
    /// <param name="collectionType">Raw value of the collection type of a library.</param>
    /// <returns>The label shown to the user.</returns>
    private static string TypeLabel(string? collectionType) => collectionType switch
    {
        nameof(CollectionTypeOptions.movies) => "Películas",
        nameof(CollectionTypeOptions.tvshows) => "Programas de TV",
        nameof(CollectionTypeOptions.music) => "Música",
        nameof(CollectionTypeOptions.musicvideos) => "Vídeos musicales",
        nameof(CollectionTypeOptions.homevideos) => "Vídeos",
        nameof(CollectionTypeOptions.boxsets) => "Colecciones",
        nameof(CollectionTypeOptions.books) => "Libros",
        nameof(CollectionTypeOptions.mixed) => "Contenido mixto",
        "photos" => "Fotos",
        _ => "Sin tipo"
    };

    /// <summary>
    /// Creates the folder that is about to be added to the library, so Jellyfin never stores a path that
    /// does not exist yet.
    /// </summary>
    /// <param name="folder">Folder of the section.</param>
    /// <param name="logger">Logger.</param>
    private static void EnsureFolder(string folder, ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The path is added anyway: the synchronization reports what it cannot write, and a missing
            // folder shows up in Jellyfin as an empty library instead of breaking the request.
            logger.LogWarning(ex, "IB3: no se pudo crear la carpeta {Folder}", folder);
        }
    }
}

/// <summary>
/// The data of a server library the planner needs, without depending on the Jellyfin assemblies.
/// </summary>
/// <param name="Name">Library name.</param>
/// <param name="IsTvShows">Whether the library is of type «Programas de TV».</param>
/// <param name="Locations">Folders of the library.</param>
/// <param name="CollectionType">Raw collection type of the library, <c>null</c> when it has none.</param>
public sealed record LibraryFolder(
    string Name,
    bool IsTvShows,
    IReadOnlyList<string> Locations,
    string? CollectionType = null);

/// <summary>
/// Library of the server, as the configuration page needs it for the combo of the table.
/// </summary>
/// <param name="Name">Library name.</param>
/// <param name="Type">Collection type in the language of the page («Programas de TV», «Películas»...).</param>
/// <param name="IsTvShows">Whether the library is of type «Programas de TV».</param>
/// <param name="Locations">Folders of the library.</param>
public sealed record LibrarySummary(
    string Name,
    string Type,
    bool IsTvShows,
    IReadOnlyList<string> Locations);

/// <summary>
/// Section folder of the destination and the libraries that index it.
/// </summary>
/// <param name="Section">Section name, which is also the name of the folder («Sèries», «Arxiu»...).</param>
/// <param name="Path">Absolute path of the folder.</param>
/// <param name="Exists">Whether the folder exists on disk.</param>
/// <param name="Covered">Whether the library of the plugin already indexes the folder.</param>
/// <param name="Libraries">Names of every library that indexes the folder.</param>
public sealed record LibrarySectionFolder(
    string Section,
    string Path,
    bool Exists,
    bool Covered,
    IReadOnlyList<string> Libraries);

/// <summary>
/// Section folders of the destination and the libraries of the server, for the table of the page.
/// </summary>
/// <param name="Path">Destination folder.</param>
/// <param name="LibraryName">Name of the library of the plugin.</param>
/// <param name="BySection">Whether the synchronization writes a folder per section.</param>
/// <param name="LibraryExists">Whether the library of the plugin already exists.</param>
/// <param name="Folders">Section folders of the destination.</param>
/// <param name="Libraries">Libraries of the server.</param>
/// <param name="Missing">Sections the library of the plugin does not index yet.</param>
public sealed record LibrarySectionMatrix(
    string Path,
    string LibraryName,
    bool BySection,
    bool LibraryExists,
    IReadOnlyList<LibrarySectionFolder> Folders,
    IReadOnlyList<LibrarySummary> Libraries,
    IReadOnlyList<string> Missing);

/// <summary>
/// What has to be done to add the folder of a section to a library.
/// </summary>
public enum LibraryLinkAction
{
    /// <summary>
    /// The request cannot be satisfied.
    /// </summary>
    Invalid,

    /// <summary>
    /// The folder has to be added to the library.
    /// </summary>
    Added,

    /// <summary>
    /// The library already indexes the folder.
    /// </summary>
    Existing
}

/// <summary>
/// Result of planning the association of a section folder with a library, before touching the server.
/// </summary>
/// <param name="Action">Action to apply.</param>
/// <param name="Folder">Normalized folder.</param>
/// <param name="LibraryName">Library that has to index the folder.</param>
/// <param name="Message">Human readable explanation.</param>
public sealed record LibraryLinkPlan(
    LibraryLinkAction Action,
    string Folder,
    string? LibraryName,
    string Message);

/// <summary>
/// Result of the «add the section to a library» request.
/// </summary>
/// <param name="Success">Whether the library indexes the folder.</param>
/// <param name="Outcome">
/// Machine readable outcome: <c>added</c>, <c>existing</c> or <c>invalid</c>.
/// </param>
/// <param name="Folder">Folder of the section.</param>
/// <param name="LibraryName">Library the folder was sent to.</param>
/// <param name="Message">Message shown to the user.</param>
public sealed record LibraryLinkStatus(
    bool Success,
    string Outcome,
    string Folder,
    string? LibraryName,
    string Message);

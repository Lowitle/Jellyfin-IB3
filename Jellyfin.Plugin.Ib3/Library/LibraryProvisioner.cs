using System.Text.Json;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Library;

/// <summary>
/// Creates (or points the library with the same name at the folders of the layout), so a fresh
/// installation does not need a trip through the «Add media library» wizard.
/// </summary>
/// <remarks>
/// <para>
/// The plugin writes every program under one folder per section of the platform («Sèries»,
/// «Documentals», «Actualitat»...) inside the destination folder, and the library indexes those eight
/// folders one by one instead of the destination folder: Jellyfin reads a folder that is not a media path
/// of the library as if it were a series of the library, so a library pointing at the destination shows
/// «Sèries» or «Documentals» as program names. With the folders of the sections as media paths the
/// library shows the sections as folders and a curated library can point at just one of them without any
/// copy.
/// </para>
/// <para>
/// The plugin only writes <c>.strm</c>, <c>.nfo</c> and <c>.srt</c> files: the library has to read the
/// local metadata (<c>Nfo</c>) and, because IB3 numbering does not match TVDB/TMDB, it should not be asked
/// to fetch online metadata for the episodes.
/// </para>
/// </remarks>
public static class LibraryProvisioner
{
    /// <summary>
    /// Library name used when the page does not send one.
    /// </summary>
    public const string DefaultLibraryName = "IB3";

    private static readonly JsonSerializerOptions CloneOptions = new();

    /// <summary>
    /// Creates the library (or completes the one that already indexes the layout) and asks Jellyfin to
    /// scan it.
    /// </summary>
    /// <param name="libraryManager">Library manager of the server.</param>
    /// <param name="rootPath">Destination folder of the plugin.</param>
    /// <param name="name">Library name; «IB3» when empty.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="libraryMonitor">Library monitor used to tell Jellyfin about the folders.</param>
    /// <param name="bySection">
    /// Whether the synchronization writes a folder per section, in which case the library gets one media
    /// path per section instead of the destination folder.
    /// </param>
    /// <returns>The result of the operation.</returns>
    public static LibraryProvisionStatus Provision(
        ILibraryManager libraryManager,
        string? rootPath,
        string? name,
        ILogger logger,
        ILibraryMonitor? libraryMonitor = null,
        bool bySection = true)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(logger);

        var folder = LibraryIO.Normalize(rootPath);
        if (folder is null)
        {
            return new LibraryProvisionStatus(
                false,
                "invalid",
                null,
                "No hay ninguna carpeta que indexar.",
                []);
        }

        // The destination folder has to be a writable folder of the server; the folders of the layout are
        // created under it by the synchronization.
        var check = LibraryPath.Check(folder);
        if (!check.IsValid || (check.Exists && !check.Writable))
        {
            return new LibraryProvisionStatus(
                false,
                "invalid",
                null,
                check.Error ?? "La carpeta de destino no se puede usar.",
                []);
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogError(ex, "IB3: no se pudo crear la carpeta de la biblioteca");

            return new LibraryProvisionStatus(
                false,
                "invalid",
                null,
                $"No se pudo crear la carpeta de la biblioteca: {ex.Message}",
                []);
        }

        // With the layout off the programs are written straight under the destination folder, so the
        // library indexes the folder itself; with the layout on it indexes the folder of each section.
        var paths = bySection ? LibraryCategory.Sections(folder) : [folder];

        if (bySection)
        {
            CreateSections(folder, logger);
        }

        // A copy of the folders of the server: adding a media path below cannot change the collection the
        // checks of this method walk.
        IReadOnlyList<VirtualFolderInfo> libraries = libraryManager.GetVirtualFolders()?.ToArray() ?? [];
        var libraryName = string.IsNullOrWhiteSpace(name) ? DefaultLibraryName : name.Trim();

        // A library that already indexes every folder of the layout only has to drop the destination folder
        // the version 1.1.0.0 added; the library that indexes the destination folder itself (what the
        // previous version created) is migrated, so the media paths end up being the folders of the layout
        // and never the destination folder that made Jellyfin read the section names as series. A curated
        // library that only points at one section is left alone: a library of the same name is completed
        // and, when there is none, a new one is created.
        var indexed = libraries.FirstOrDefault(library => paths.All(path => Owns(library, path)));
        if (indexed is not null)
        {
            return Complete(indexed, folder, paths, bySection, libraryManager, libraryMonitor, logger);
        }

        var legacy = libraries.FirstOrDefault(library => Owns(library, folder));

        if (legacy is not null)
        {
            return Complete(legacy, folder, paths, bySection, libraryManager, libraryMonitor, logger);
        }

        var sameName = libraries.FirstOrDefault(
            library => string.Equals(library.Name, libraryName, StringComparison.OrdinalIgnoreCase));

        return sameName is not null
            ? Complete(sameName, folder, paths, bySection, libraryManager, libraryMonitor, logger)
            : Create(libraryName, folder, paths, libraries, libraryManager, libraryMonitor, logger);
    }

    /// <summary>
    /// Creates the folders of the eight sections of the platform under the destination folder, so each one
    /// can be a media path of the library from the first scan instead of appearing when the synchronization
    /// writes the first program of the section.
    /// </summary>
    /// <param name="folder">Destination folder of the plugin.</param>
    /// <param name="logger">Logger.</param>
    /// <remarks>
    /// It is only called with the layout on, which is what writes the programs inside the folders of the
    /// sections. A folder that cannot be created does not fail the request; the synchronization recreates it
    /// when it writes the first program of the section.
    /// </remarks>
    private static void CreateSections(string folder, ILogger logger)
    {
        foreach (var section in LibraryCategory.All)
        {
            try
            {
                Directory.CreateDirectory(LibraryCategory.Folder(folder, section));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                logger.LogWarning(ex, "IB3: no se pudo crear la carpeta de la sección {Section}", section);
            }
        }
    }

    /// <summary>
    /// Makes the folders of the layout the media paths of the library: adds the ones that are missing and
    /// drops the folders the configuration no longer writes in.
    /// </summary>
    /// <param name="library">Library that indexes the destination folder or some of its sections.</param>
    /// <param name="folder">Destination folder of the plugin.</param>
    /// <param name="paths">Folders the library has to index.</param>
    /// <param name="bySection">Whether the programs are written inside the folder of their section.</param>
    /// <param name="libraryManager">Library manager of the server.</param>
    /// <param name="libraryMonitor">Library monitor used to tell Jellyfin about the folders.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The result of the operation.</returns>
    private static LibraryProvisionStatus Complete(
        VirtualFolderInfo library,
        string folder,
        IReadOnlyList<string> paths,
        bool bySection,
        ILibraryManager libraryManager,
        ILibraryMonitor? libraryMonitor,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(library.Name))
        {
            return new LibraryProvisionStatus(
                false,
                "conflict",
                library.Name,
                $"Ya existe una biblioteca «{library.Name}» con otras carpetas. Añade las carpetas de IB3 a mano desde el panel de bibliotecas.",
                Folders(library));
        }

        var current = Folders(library);
        var sections = LibraryCategory.Sections(folder);
        var missing = paths
            .Where(path => !current.Any(location => LibraryPath.SamePath(location, path)))
            .ToList();

        // The folders the library must stop indexing. With the layout off the programs are written straight
        // under the destination folder, so the folders of the sections the previous configuration added are
        // the ones dropped: a program would be indexed twice otherwise and half of it would be left behind.
        // With the layout on the destination folder is the one dropped, but only once the library indexes
        // every folder of the layout: its section names are read as series and the sections themselves would
        // be indexed twice.
        var covered = paths.All(path => missing.Any(entry => LibraryPath.SamePath(entry, path))
            || current.Any(location => LibraryPath.SamePath(location, path)));

        List<string> dropped;

        if (!covered)
        {
            dropped = [];
        }
        else if (bySection)
        {
            dropped = [.. current.Where(location => LibraryPath.SamePath(location, folder))];
        }
        else
        {
            dropped = [.. current.Where(location => sections.Any(section => LibraryPath.SamePath(section, location)))];
        }

        if (missing.Count == 0 && dropped.Count == 0)
        {
            return new LibraryProvisionStatus(
                true,
                "existing",
                library.Name,
                $"La carpeta de IB3 ya está indexada por la biblioteca «{library.Name}».",
                paths);
        }

        try
        {
            foreach (var path in missing)
            {
                libraryManager.AddMediaPath(library.Name, new MediaPathInfo(path));
                libraryMonitor?.ReportFileSystemChanged(path);
            }

            foreach (var path in dropped)
            {
                libraryManager.RemoveMediaPath(library.Name, path);
                libraryMonitor?.ReportFileSystemChanged(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            logger.LogError(ex, "IB3: no se pudo actualizar la biblioteca {Library}", library.Name);

            return new LibraryProvisionStatus(
                false,
                "conflict",
                library.Name,
                $"No se pudo actualizar la biblioteca «{library.Name}»: {ex.Message}",
                Folders(library));
        }

        logger.LogInformation(
            "IB3: biblioteca {Library} actualizada: {Added} carpetas añadidas, {Dropped} quitadas",
            library.Name,
            missing.Count,
            dropped.Count);

        var message = (missing.Count, dropped.Count) switch
        {
            (0, > 0) when bySection =>
                $"La biblioteca «{library.Name}» ya no apunta a «{folder}»: indexa las {paths.Count} carpetas de las secciones de IB3.",
            (_, > 0) when bySection =>
                $"La biblioteca «{library.Name}» indexa las {paths.Count} carpetas de las secciones de IB3 y ya no la carpeta de destino «{folder}».",
            (_, > 0) =>
                $"La biblioteca «{library.Name}» indexa ahora la carpeta «{folder}» y ya no las {dropped.Count} carpetas de las secciones de IB3.",
            (1, _) =>
                $"La carpeta «{missing[0]}» se ha añadido a la biblioteca «{library.Name}».",
            _ =>
                $"Se han añadido {missing.Count} carpetas de IB3 a la biblioteca «{library.Name}»."
        };

        return new LibraryProvisionStatus(true, "updated", library.Name, message, paths);
    }

    /// <summary>
    /// Creates the library that indexes the folders of the layout.
    /// </summary>
    /// <param name="libraryName">Name of the library.</param>
    /// <param name="folder">Destination folder of the plugin.</param>
    /// <param name="paths">Folders the library has to index.</param>
    /// <param name="libraries">Libraries of the server, used as a template.</param>
    /// <param name="libraryManager">Library manager of the server.</param>
    /// <param name="libraryMonitor">Library monitor used to tell Jellyfin about the folders.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The result of the operation.</returns>
    private static LibraryProvisionStatus Create(
        string libraryName,
        string folder,
        IReadOnlyList<string> paths,
        IReadOnlyList<VirtualFolderInfo> libraries,
        ILibraryManager libraryManager,
        ILibraryMonitor? libraryMonitor,
        ILogger logger)
    {
        try
        {
            libraryManager.AddVirtualFolder(
                libraryName,
                CollectionTypeOptions.tvshows,
                BuildOptions(paths, libraries),
                refreshLibrary: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IB3: no se pudo crear la biblioteca {Library}", libraryName);

            return new LibraryProvisionStatus(
                false,
                "invalid",
                libraryName,
                $"No se pudo crear la biblioteca «{libraryName}»: {ex.Message}",
                []);
        }

        foreach (var path in paths)
        {
            libraryMonitor?.ReportFileSystemChanged(path);
        }

        logger.LogInformation(
            "IB3: biblioteca {Library} creada con {Count} carpetas para {Folder}",
            libraryName,
            paths.Count,
            folder);

        return new LibraryProvisionStatus(
            true,
            "created",
            libraryName,
            paths.Count > 1
                ? $"Biblioteca «{libraryName}» creada; Jellyfin indexará las {paths.Count} carpetas de las secciones de IB3."
                : $"Biblioteca «{libraryName}» creada; Jellyfin indexará {folder}.",
            paths);
    }

    /// <summary>
    /// Builds the options of the library: the local <c>.nfo</c> files are the source of truth (IB3
    /// numbering does not match TVDB/TMDB) and a template of an existing library of the same type is reused
    /// so the image and metadata providers the user already tuned are kept.
    /// </summary>
    /// <param name="locations">Folders of the new library.</param>
    /// <param name="libraries">Libraries of the server.</param>
    /// <returns>The options of the library.</returns>
    private static LibraryOptions BuildOptions(IReadOnlyList<string> locations, IReadOnlyList<VirtualFolderInfo> libraries)
    {
        var template = libraries
            .FirstOrDefault(library =>
                library.CollectionType == CollectionTypeOptions.tvshows && library.LibraryOptions is not null)
            ?.LibraryOptions;

        var options = template is null ? new LibraryOptions() : Clone(template);

        options.Enabled = true;
        options.PathInfos = [.. locations.Select(location => new MediaPathInfo(location))];

        return options;
    }

    private static LibraryOptions Clone(LibraryOptions options)
        => JsonSerializer.Deserialize<LibraryOptions>(
            JsonSerializer.Serialize(options, CloneOptions),
            CloneOptions) ?? new LibraryOptions();

    private static IReadOnlyList<string> Folders(VirtualFolderInfo library)
        => library.Locations is { Length: > 0 } locations ? [.. locations] : [];

    private static bool Owns(VirtualFolderInfo library, string folder)
        => Folders(library).Any(location => LibraryPath.SamePath(location, folder));
}

/// <summary>
/// Result of the «create library» request.
/// </summary>
/// <param name="Success">Whether the folder ended up in a library.</param>
/// <param name="Outcome">
/// Machine readable outcome: <c>created</c>, <c>updated</c>, <c>existing</c>, <c>conflict</c> or <c>invalid</c>.
/// </param>
/// <param name="LibraryName">Library that was created or updated.</param>
/// <param name="Message">Message shown to the user.</param>
/// <param name="LibraryPaths">Folders of the layout the library indexes after the request.</param>
public sealed record LibraryProvisionStatus(
    bool Success,
    string Outcome,
    string? LibraryName,
    string Message,
    IReadOnlyList<string> LibraryPaths);

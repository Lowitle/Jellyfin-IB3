using System.Text.Json;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Library;

/// <summary>
/// Creates (or reuses) the library that points at the destination folder, so a fresh installation does not
/// need a trip through the «Add media library» wizard.
/// </summary>
/// <remarks>
/// <para>
/// The plugin writes every program under one folder of its kind («Sèries», «Documentals»...) inside the
/// destination folder, so a single library of type «Programas de TV» pointing at the destination indexes
/// all of them: Jellyfin walks the subfolders and a curated library can point at just one of the folders
/// without any copy.
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
    /// Creates the library (or adds the folder to the library with the same name) and asks Jellyfin to scan
    /// it.
    /// </summary>
    /// <param name="libraryManager">Library manager of the server.</param>
    /// <param name="rootPath">Destination folder of the plugin, which the library indexes.</param>
    /// <param name="name">Library name; «IB3» when empty.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="libraryMonitor">Library monitor used to tell Jellyfin about the folder.</param>
    /// <returns>The result of the operation.</returns>
    public static LibraryProvisionStatus Provision(
        ILibraryManager libraryManager,
        string? rootPath,
        string? name,
        ILogger logger,
        ILibraryMonitor? libraryMonitor = null)
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

        var libraries = libraryManager.GetVirtualFolders() ?? [];
        var libraryName = string.IsNullOrWhiteSpace(name) ? DefaultLibraryName : name.Trim();

        var indexed = libraries.FirstOrDefault(library => Owns(library, folder));
        if (indexed is not null)
        {
            return new LibraryProvisionStatus(
                true,
                "existing",
                indexed.Name,
                $"La carpeta de IB3 ya está indexada por la biblioteca «{indexed.Name}».",
                Folders(indexed));
        }

        var sameName = libraries.FirstOrDefault(
            library => string.Equals(library.Name, libraryName, StringComparison.OrdinalIgnoreCase));

        if (sameName is not null)
        {
            return Append(sameName, folder, libraryManager, libraryMonitor, logger);
        }

        return Create(libraryName, folder, libraries, libraryManager, libraryMonitor, logger);
    }

    /// <summary>
    /// Adds the destination folder to a library of the same name that indexes other folders.
    /// </summary>
    /// <param name="library">Library of the same name.</param>
    /// <param name="folder">Folder the library has to index.</param>
    /// <param name="libraryManager">Library manager of the server.</param>
    /// <param name="libraryMonitor">Library monitor used to tell Jellyfin about the folder.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The result of the operation.</returns>
    private static LibraryProvisionStatus Append(
        VirtualFolderInfo library,
        string folder,
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
                $"Ya existe una biblioteca «{library.Name}» con otras carpetas. Añade la carpeta de IB3 a mano desde el panel de bibliotecas.",
                Folders(library));
        }

        try
        {
            libraryManager.AddMediaPath(library.Name, new MediaPathInfo(folder));
            libraryMonitor?.ReportFileSystemChanged(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            logger.LogError(ex, "IB3: no se pudo añadir {Folder} a la biblioteca {Library}", folder, library.Name);

            return new LibraryProvisionStatus(
                false,
                "conflict",
                library.Name,
                $"No se pudo añadir «{folder}» a la biblioteca «{library.Name}»: {ex.Message}",
                Folders(library));
        }

        logger.LogInformation("IB3: carpeta {Folder} añadida a la biblioteca {Library}", folder, library.Name);

        return new LibraryProvisionStatus(
            true,
            "updated",
            library.Name,
            $"La carpeta de IB3 «{folder}» se ha añadido a la biblioteca «{library.Name}».",
            [.. Folders(library), folder]);
    }

    /// <summary>
    /// Creates the library that indexes the destination folder.
    /// </summary>
    /// <param name="libraryName">Name of the library.</param>
    /// <param name="folder">Folder to index.</param>
    /// <param name="libraries">Libraries of the server, used as a template.</param>
    /// <param name="libraryManager">Library manager of the server.</param>
    /// <param name="libraryMonitor">Library monitor used to tell Jellyfin about the folder.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The result of the operation.</returns>
    private static LibraryProvisionStatus Create(
        string libraryName,
        string folder,
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
                BuildOptions(folder, libraries),
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

        libraryMonitor?.ReportFileSystemChanged(folder);

        logger.LogInformation("IB3: biblioteca {Library} creada para {Folder}", libraryName, folder);

        return new LibraryProvisionStatus(
            true,
            "created",
            libraryName,
            $"Biblioteca «{libraryName}» creada; Jellyfin indexará {folder}.",
            [folder]);
    }

    /// <summary>
    /// Builds the options of the library: the local <c>.nfo</c> files are the source of truth (IB3
    /// numbering does not match TVDB/TMDB) and a template of an existing library of the same type is reused
    /// so the image and metadata providers the user already tuned are kept.
    /// </summary>
    /// <param name="location">Folder of the new library.</param>
    /// <param name="libraries">Libraries of the server.</param>
    /// <returns>The options of the library.</returns>
    private static LibraryOptions BuildOptions(string location, IReadOnlyList<VirtualFolderInfo> libraries)
    {
        var template = libraries
            .FirstOrDefault(library =>
                library.CollectionType == CollectionTypeOptions.tvshows && library.LibraryOptions is not null)
            ?.LibraryOptions;

        var options = template is null ? new LibraryOptions() : Clone(template);

        options.Enabled = true;
        options.PathInfos = [new MediaPathInfo(location)];

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
/// <param name="LibraryPaths">Folders the library indexes after the request.</param>
public sealed record LibraryProvisionStatus(
    bool Success,
    string Outcome,
    string? LibraryName,
    string Message,
    IReadOnlyList<string> LibraryPaths);

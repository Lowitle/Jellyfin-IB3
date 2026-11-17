using Jellyfin.Plugin.Ib3.Client;
using Jellyfin.Plugin.Ib3.Configuration;
using Jellyfin.Plugin.Ib3.Library;
using Jellyfin.Plugin.Ib3.Sync;
using Jellyfin.Plugin.Ib3.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ib3.Api;

/// <summary>
/// Endpoints used by the configuration page: the search of the catalog, the resolution of a program, the
/// check of the destination folder, the provisioning of the library and the manual refresh.
/// </summary>
[ApiController]
[Authorize]
[Route("IB3")]
public sealed class Ib3Controller : ControllerBase
{
    private readonly Ib3Client _client;
    private readonly ITaskManager _taskManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<Ib3Controller> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ib3Controller"/> class.
    /// </summary>
    /// <param name="client">IB3 API client.</param>
    /// <param name="taskManager">Task manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="libraryMonitor">Library monitor, used to make Jellyfin look at the new folder.</param>
    /// <param name="logger">Logger.</param>
    public Ib3Controller(
        Ib3Client client,
        ITaskManager taskManager,
        ILibraryManager libraryManager,
        ILibraryMonitor libraryMonitor,
        ILogger<Ib3Controller> logger)
    {
        _client = client;
        _taskManager = taskManager;
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Searches the catalog of IB3 by title or slug.
    /// </summary>
    /// <param name="query">Text typed by the user; an empty query returns the newest programs.</param>
    /// <param name="limit">Maximum number of matches.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matches, best first.</returns>
    [HttpGet("Programs/Search")]
    public async Task<ActionResult<IReadOnlyList<ProgramMatch>>> SearchProgramsAsync(
        [FromQuery] string? query,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var matches = await _client
            .SearchProgramsAsync(query, limit <= 0 ? 50 : limit, cancellationToken)
            .ConfigureAwait(false);

        return Ok(matches);
    }

    /// <summary>
    /// Resolves a reference typed by the user (a URL of <c>totib3.org</c>, a slug, a title or an id) and
    /// reports how many videos IB3 publishes for it.
    /// </summary>
    /// <param name="value">Reference typed by the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The program, or 404 when the reference does not point at any program.</returns>
    [HttpGet("Programs/Resolve")]
    public async Task<ActionResult<Ib3ProgramSummary>> ResolveAsync(
        [FromQuery] string? value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BadRequest("Escribe el nombre, el slug o la URL del programa.");
        }

        var program = await _client.FindProgramAsync(value, cancellationToken).ConfigureAwait(false);
        if (program is null || string.IsNullOrWhiteSpace(program.Id))
        {
            return NotFound($"«{value.Trim()}» no corresponde a ningún programa del catálogo de IB3.");
        }

        var programId = program.Id.Trim();
        var medias = await _client.GetMediasAsync(programId, cancellationToken).ConfigureAwait(false);

        var playable = medias.Count(media => Ib3Client.PickFile(media) is not null);
        var expired = medias.Count(Ib3SyncService.IsExpired);
        var last = medias.Count > 0
            ? Ib3Text.ParseDate(medias[0].Broadcast)
            : null;

        return Ok(new Ib3ProgramSummary(
            programId,
            program.Slug ?? string.Empty,
            Ib3SyncService.SeriesTitle(program, new Ib3ProgramReference { Reference = value }),
            Ib3SyncService.KindLabel(program),
            Ib3Client.Ib3CategoryName(program),
            Ib3Text.Sentence(program.Description),
            Ib3Client.WebUrl(program.Slug),
            medias.Count,
            playable,
            expired,
            last,
            medias.Count == 0
                ? "IB3 no publica ningún vídeo de este programa ahora mismo."
                : $"IB3 publica {medias.Count} vídeos: {playable} se pueden añadir a la biblioteca"
                    + (expired > 0 ? $" y {expired} han caducado." : ".")));
    }

    /// <summary>
    /// Checks the destination folder: whether it exists, whether Jellyfin can write into it and whether it
    /// is inside the server.
    /// </summary>
    /// <param name="path">Folder typed by the user.</param>
    /// <returns>The result of the check.</returns>
    [HttpGet("Path/Check")]
    public ActionResult<LibraryPathStatus> CheckPath([FromQuery] string? path)
        => Ok(LibraryPath.Check(path));

    /// <summary>
    /// Lists the sections of the platform and their subcategories, so the page names them from a single
    /// place and never repeats the folders of the layout.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sections of the layout, with the subcategories each one publishes.</returns>
    [HttpGet("Kinds")]
    public async Task<ActionResult<IReadOnlyList<Ib3Kind>>> ListKindsAsync(CancellationToken cancellationToken)
    {
        var sections = await _client.GetCategoriesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(sections
            .Select(section => new Ib3Kind(
                section.Name,
                section.Name,
                [.. section.Subcategories.Select(subcategory => subcategory.Name)]))
            .ToList());
    }

    /// <summary>
    /// Lists the programs a section of the platform publishes, so the page can add them without searching
    /// one by one.
    /// </summary>
    /// <param name="category">Name or id of the section («Sèries», «Documentals»...).</param>
    /// <param name="subcategory">Name or id of the subcategory, or empty for the whole section.</param>
    /// <param name="limit">Maximum number of programs; 200 when omitted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The programs, newest first; 400 when the section is not one of the eight of the platform.</returns>
    [HttpGet("Programs/Section")]
    public async Task<ActionResult<IReadOnlyList<Ib3SectionProgram>>> ListSectionProgramsAsync(
        [FromQuery] string? category,
        [FromQuery] string? subcategory,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var section = Ib3Categories.Find(category);
        if (section is null)
        {
            return BadRequest("La sección no es una de las de totib3.org.");
        }

        var child = Ib3Categories.FindSubcategory(section, subcategory);

        var programs = await _client
            .GetCategoryProgramsAsync(
                section.Id,
                child?.Id,
                limit <= 0 ? 200 : Math.Min(limit, 500),
                cancellationToken)
            .ConfigureAwait(false);

        // A program with no id cannot be mirrored: the synchronization resolves the reference by the id.
        return Ok(programs
            .Where(program => !string.IsNullOrWhiteSpace(program.Id))
            .Select(program => new Ib3SectionProgram(
                program.Id!.Trim(),
                program.Slug ?? string.Empty,
                Ib3SyncService.SeriesTitle(program, new Ib3ProgramReference()),
                Ib3Client.Ib3CategoryName(program),
                Ib3Client.Ib3SubcategoryName(program)))
            .ToList());
    }

    /// <summary>
    /// Creates the library that indexes the folders of the layout, or completes the library that already
    /// indexes them.
    /// </summary>
    /// <param name="path">Destination folder; the configured one when omitted.</param>
    /// <param name="name">Library name; «IB3» when omitted.</param>
    /// <returns>The result of the operation, always with HTTP 200 so the page shows the reason of a
    /// rejection.</returns>
    [HttpPost("Library/Create")]
    public ActionResult<LibraryProvisionStatus> CreateLibrary(
        [FromQuery] string? path,
        [FromQuery] string? name)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var root = string.IsNullOrWhiteSpace(path) ? configuration.DestinationPath : path.Trim();

        var status = LibraryProvisioner.Provision(
            _libraryManager,
            root,
            string.IsNullOrWhiteSpace(name) ? configuration.LibraryName : name,
            _logger,
            _libraryMonitor,
            configuration.OrganizeByKind);

        if (status.Success)
        {
            _logger.LogInformation("IB3: biblioteca {Library} → {Message}", status.LibraryName, status.Message);
        }
        else
        {
            _logger.LogWarning("IB3: no se pudo preparar la biblioteca: {Message}", status.Message);
        }

        return Ok(status);
    }

    /// <summary>
    /// Lists the section folders of the destination and the libraries of the server, so the page can add a
    /// section to a library the user already has.
    /// </summary>
    /// <remarks>
    /// The sections are always listed, even with the layout off: the table then shows the folders the
    /// library indexes with «Separar los programas en carpetas por sección» enabled.
    /// </remarks>
    /// <param name="path">Destination folder; the configured one when omitted.</param>
    /// <param name="name">Library name; the configured one when omitted.</param>
    /// <param name="bySection">Whether the synchronization writes a folder per section.</param>
    /// <returns>The section folders and the libraries of the server.</returns>
    [HttpGet("Library/Folders")]
    public ActionResult<LibrarySectionMatrix> GetLibraryFolders(
        [FromQuery] string? path,
        [FromQuery] string? name,
        [FromQuery] bool? bySection)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var target = string.IsNullOrWhiteSpace(path) ? configuration.DestinationPath : path.Trim();
        var folder = LibraryIO.Normalize(target);

        if (folder is null)
        {
            return BadRequest("Escribe la carpeta de destino antes de ver las secciones y las bibliotecas.");
        }

        return Ok(LibrarySections.Describe(
            folder,
            string.IsNullOrWhiteSpace(name) ? configuration.LibraryName : name,
            bySection ?? configuration.OrganizeByKind,
            LibrarySections.ToLibraries(_libraryManager.GetVirtualFolders() ?? [])));
    }

    /// <summary>
    /// Adds the folder of a section to a library of the server.
    /// </summary>
    /// <remarks>
    /// The folder is not removed from the library of the plugin: Jellyfin indexes it in both libraries. The
    /// status is always returned with HTTP 200 and a <c>success</c> flag, so the page can show the reason of
    /// a rejection instead of a generic error.
    /// </remarks>
    /// <param name="folder">Folder of the section, as the table of the page read it.</param>
    /// <param name="library">Library that has to index the folder.</param>
    /// <param name="path">Destination folder; the configured one when omitted.</param>
    /// <returns>The result of the operation.</returns>
    [HttpPost("Library/Associate")]
    public ActionResult<LibraryLinkStatus> AssociateSection(
        [FromQuery] string? folder,
        [FromQuery] string? library,
        [FromQuery] string? path)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var root = LibraryIO.Normalize(string.IsNullOrWhiteSpace(path) ? configuration.DestinationPath : path);
        var target = LibraryIO.Normalize(folder);

        if (root is null)
        {
            return Ok(new LibraryLinkStatus(
                false,
                "invalid",
                target ?? folder?.Trim() ?? string.Empty,
                library,
                "Escribe la carpeta de destino antes de añadir una sección a otra biblioteca."));
        }

        // Only a folder of the layout can be added: the endpoint must not become a way to attach any folder
        // of the server to a library behind the back of the «Add media library» wizard.
        if (target is null
            || !LibraryCategory.Sections(root).Any(section => LibraryPath.SamePath(section, target)))
        {
            return Ok(new LibraryLinkStatus(
                false,
                "invalid",
                target ?? folder?.Trim() ?? string.Empty,
                library,
                "La carpeta no es una de las secciones de IB3: elige una fila de la tabla."));
        }

        var status = LibrarySections.Associate(_libraryManager, target, library, _logger, _libraryMonitor);

        if (status.Success)
        {
            _logger.LogInformation(
                "IB3: sección {Folder} → biblioteca {Library} ({Outcome})",
                status.Folder,
                status.LibraryName,
                status.Outcome);
        }
        else
        {
            _logger.LogWarning("IB3: no se pudo añadir la sección a la biblioteca: {Message}", status.Message);
        }

        return Ok(status);
    }

    /// <summary>
    /// Applies the configured refresh interval to the scheduled task.
    /// </summary>
    /// <returns>No content.</returns>
    [HttpPost("Triggers")]
    public ActionResult ApplyTriggers()
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        Ib3TriggerScheduler.Apply(_taskManager, configuration, _logger);

        return NoContent();
    }

    /// <summary>
    /// Starts the refresh task immediately.
    /// </summary>
    /// <remarks>
    /// The stock <c>ScheduledTasks/Running/{id}</c> endpoint looks the task up by its GUID, not by its key,
    /// so the page cannot use it without a round trip.
    /// </remarks>
    /// <returns>No content when the task was queued; 404 when it is not registered yet.</returns>
    [HttpPost("Refresh")]
    public ActionResult RunNow()
    {
        var worker = _taskManager.ScheduledTasks
            .FirstOrDefault(task => task.ScheduledTask is Ib3RefreshTask);

        if (worker is null)
        {
            _logger.LogWarning("IB3: la tarea programada no está registrada; no se puede ejecutar");

            return NotFound("La tarea programada no está registrada todavía. Reinicia Jellyfin.");
        }

        _ = _taskManager.Execute(worker, new TaskOptions());

        return NoContent();
    }
}

/// <summary>
/// What the configuration page shows of a program once it is resolved.
/// </summary>
/// <param name="Id">Id of the program (the GUID of the API).</param>
/// <param name="Slug">Slug of the public page.</param>
/// <param name="Title">Title of the series the synchronization writes.</param>
/// <param name="Kind">Kind of program («Sèrie», «Documental»...).</param>
/// <param name="Category">Category of IB3 the program is filed under.</param>
/// <param name="Description">Description of the program.</param>
/// <param name="WebUrl">Public page of the program, or <c>null</c> when it has no slug.</param>
/// <param name="Videos">Videos IB3 publishes.</param>
/// <param name="Playable">Videos with a file the library can play.</param>
/// <param name="Expired">Videos whose rights expired.</param>
/// <param name="LastBroadcast">Broadcast date of the newest video.</param>
/// <param name="Message">Human readable summary of the state of the program.</param>
public sealed record Ib3ProgramSummary(
    string Id,
    string Slug,
    string Title,
    string Kind,
    string? Category,
    string? Description,
    string? WebUrl,
    int Videos,
    int Playable,
    int Expired,
    DateTimeOffset? LastBroadcast,
    string Message);

/// <summary>
/// A section of the layout the programs are written to, with the subcategories it publishes.
/// </summary>
/// <param name="Value">Name of the section, as stored in the configuration.</param>
/// <param name="Label">Name the page shows.</param>
/// <param name="Subcategories">Subcategories of the section, in the order of the platform.</param>
public sealed record Ib3Kind(string Value, string Label, IReadOnlyList<string> Subcategories);

/// <summary>
/// A program a section of the platform publishes, as the page needs it to add it to the list.
/// </summary>
/// <param name="Id">Id of the program (the GUID of the API).</param>
/// <param name="Slug">Slug of the public page.</param>
/// <param name="Title">Title of the series the synchronization writes.</param>
/// <param name="Category">Section of the platform the program is filed under.</param>
/// <param name="Subcategory">Subcategory the program is filed under, or <c>null</c>.</param>
public sealed record Ib3SectionProgram(
    string Id,
    string Slug,
    string Title,
    string? Category,
    string? Subcategory);

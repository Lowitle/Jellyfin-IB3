namespace Jellyfin.Plugin.Ib3.Library;

/// <summary>
/// Removes what the plugin wrote for a program, whatever the shape of the folder: a series folder or a
/// category folder that holds it.
/// </summary>
/// <remarks>
/// The cleanup is what keeps the library honest when a program changes its folder —a new title, another
/// kind of program, the layout turned on or off—, so a write never leaves a stale copy of the same program
/// behind.
/// </remarks>
internal static class LibraryCleanup
{
    /// <summary>
    /// Deletes the folder the plugin wrote for a program.
    /// </summary>
    /// <param name="folder">Folder to clean: a series folder or a category folder.</param>
    /// <param name="programId">IB3 program id the folder has to belong to.</param>
    /// <returns>The number of folders deleted.</returns>
    /// <remarks>
    /// A folder that is not one of the plugin —or that holds anything the plugin did not write— is left
    /// alone: the folders of the library can be shared with the user.
    /// </remarks>
    internal static int TryDeleteProgram(string? folder, string? programId)
        => StrmLibrary.TryDeleteSeriesFolder(folder, programId) ? 1 : 0;
}

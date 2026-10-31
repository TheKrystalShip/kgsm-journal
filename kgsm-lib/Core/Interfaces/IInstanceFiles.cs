using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// The single jailed filesystem authority for a game-server instance's working directory — list, read,
/// write/create, delete, rename. The FIRST kgsm-lib service that does direct <c>System.IO</c> (every
/// other service shells the <c>kgsm</c> engine); it resolves the jail root via
/// <see cref="IInstanceService.GetInstanceInfo(string)"/>'s <c>WorkingDir</c>, then does the byte I/O
/// itself. Both the assistant's file tools and kgsm-api's file-browser route through this one
/// implementation so a "jailed write" behaves identically everywhere.
/// </summary>
/// <remarks>
/// Failure-channel convention: every method returns a <see cref="FileOpResult"/> /
/// <see cref="FileOpResult{T}"/> whose <see cref="FileOpResult.Outcome"/> reports what happened —
/// including an escaped jail, a missing instance, or a filesystem error — and none of these methods
/// throw for an expected failure. Argument validation (e.g. a null/blank instance name) still throws,
/// per the rest of kgsm-lib's convention.
/// <para>
/// The jail: the root is <c>Instance.WorkingDir</c>, canonicalised (POSIX <c>realpath</c> — resolving
/// <c>.</c>/<c>..</c> and symlinks at EVERY path component, not just the leaf) fresh on every call — it
/// is never cached, because instances get reinstalled. A candidate path is rejected with
/// <see cref="FileOpOutcome.OutOfJail"/> unless its own canonicalised real path is the root or a
/// descendant of it. Only regular files are opened for read/write; directories are only listed (or, with
/// <see cref="DeleteOptions.AllowDir"/>, deleted when empty); FIFOs/sockets/devices are never opened.
/// </para>
/// <para>
/// Size caps are always a caller-supplied parameter (<c>maxBytes</c> / <see cref="WriteOptions.MaxBytes"/>)
/// — this interface enforces no lib-wide default, so each consumer (the assistant, kgsm-api) sets its own
/// ceiling.
/// </para>
/// </remarks>
public interface IInstanceFiles
{
    /// <summary>
    /// Lists one directory level (no recursion) inside the instance's jail.
    /// </summary>
    /// <param name="instance">The instance whose working directory to list within.</param>
    /// <param name="subdir">The directory to list, relative to the instance's working directory; null
    /// or empty lists the working directory itself.</param>
    /// <param name="maxEntries">Caps the number of entries returned; extra entries set
    /// <see cref="DirListing.Truncated"/> rather than being silently dropped without a signal.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> with the listing; <see cref="FileOpOutcome.OutOfJail"/> if
    /// <paramref name="subdir"/> escapes the jail; <see cref="FileOpOutcome.NotFound"/> if it does not
    /// exist; <see cref="FileOpOutcome.NotADirectory"/> if it is not a directory;
    /// <see cref="FileOpOutcome.InstanceUnavailable"/> if the instance/its working dir cannot be resolved.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="instance"/> is null or whitespace.</exception>
    FileOpResult<DirListing> List(string instance, string? subdir, int maxEntries);

    /// <summary>
    /// Walks the instance's tree below <paramref name="subdir"/> and returns the entries whose name —
    /// or whose path relative to the jail root, when <paramref name="pattern"/> contains a
    /// <c>/</c> — matches the glob <paramref name="pattern"/> (<c>*</c> and <c>?</c>), case-insensitively.
    /// <para>
    /// <b>Symlinked directories are never descended into.</b> That, rather than a check applied
    /// afterwards, is what keeps a recursive walk inside the jail: a link cannot be followed out if it
    /// is not followed at all. Matched entries are additionally re-resolved through the same
    /// containment rule every other operation uses.
    /// </para>
    /// <para>
    /// Bounded by <paramref name="options"/>, and the result distinguishes "more matched than were
    /// returned" from "the walk stopped early" — a caller must not read the second as the tree having
    /// been exhausted.
    /// </para>
    /// </summary>
    FileOpResult<FindResult> Find(string instance, string pattern, string? subdir, FindOptions? options = null);

    /// <summary>
    /// Searches the contents of the instance's text files below <paramref name="subdir"/> for the
    /// .NET regular expression <paramref name="pattern"/>, returning matching lines with their paths.
    /// <para>
    /// Same walk containment as <see cref="Find"/>. Candidates over
    /// <see cref="FileSearchOptions.MaxFileBytes"/> are skipped unread, and binary files are skipped by
    /// the same NUL-scan-plus-UTF-8 test <see cref="Read"/> uses — a byte blob has no lines to report.
    /// </para>
    /// <para>
    /// An invalid or catastrophically slow expression is refused as
    /// <see cref="FileOpOutcome.InvalidArgument"/> rather than being allowed to run: the pattern comes
    /// from a caller, and a backtracking regex over thousands of files is a denial of service.
    /// </para>
    /// </summary>
    FileOpResult<FileSearchResult> Search(string instance, string pattern, string? subdir, FileSearchOptions? options = null);

    /// <summary>
    /// Reads a text file inside the instance's jail.
    /// </summary>
    /// <param name="instance">The instance whose working directory to read within.</param>
    /// <param name="relPath">The file to read, relative to the instance's working directory.</param>
    /// <param name="maxBytes">The byte-length ceiling; a larger file is refused (checked before the
    /// bytes are read) rather than partially read.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> with the content, size, mtime, and an sha256 etag;
    /// <see cref="FileOpOutcome.OutOfJail"/>/<see cref="FileOpOutcome.NotFound"/>/
    /// <see cref="FileOpOutcome.NotAFile"/> as for <see cref="List"/>; <see cref="FileOpOutcome.TooLarge"/>
    /// over <paramref name="maxBytes"/>; <see cref="FileOpOutcome.Binary"/> if the bytes are not valid
    /// UTF-8 text.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="instance"/> is null or whitespace.</exception>
    FileOpResult<FileContent> Read(string instance, string relPath, long maxBytes);

    /// <summary>
    /// Atomically writes (or, with <see cref="WriteOptions.AllowCreate"/>, creates) a text file inside
    /// the instance's jail. The write is atomic (temp file in the same directory → fsync → mode-preserve
    /// → rename) — a crash mid-write never corrupts the target.
    /// </summary>
    /// <param name="instance">The instance whose working directory to write within.</param>
    /// <param name="relPath">The file to write, relative to the instance's working directory.</param>
    /// <param name="content">The new file content (UTF-8 encoded on disk, no BOM).</param>
    /// <param name="opts">Create/etag/backup/size-cap options — see <see cref="WriteOptions"/>.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> with the new size/mtime/etag;
    /// <see cref="FileOpOutcome.OutOfJail"/> as for <see cref="List"/>;
    /// <see cref="FileOpOutcome.NotFound"/> if the target is missing and
    /// <see cref="WriteOptions.AllowCreate"/> is false, or its parent directory does not exist;
    /// <see cref="FileOpOutcome.NotADirectory"/> if the parent path is not a directory;
    /// <see cref="FileOpOutcome.NotAFile"/> if the existing target is a directory or special file;
    /// <see cref="FileOpOutcome.Binary"/> if the EXISTING target is binary (refuses to clobber it);
    /// <see cref="FileOpOutcome.EtagMismatch"/> if <see cref="WriteOptions.ExpectedEtag"/> is stale;
    /// <see cref="FileOpOutcome.TooLarge"/> if the new content exceeds <see cref="WriteOptions.MaxBytes"/>.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="instance"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    FileOpResult<FileStat> Write(string instance, string relPath, string content, WriteOptions opts);

    /// <summary>
    /// Deletes a file (or, with <see cref="DeleteOptions.AllowDir"/>, an EMPTY directory — never
    /// recursive) inside the instance's jail.
    /// </summary>
    /// <param name="instance">The instance whose working directory to delete within.</param>
    /// <param name="relPath">The path to delete, relative to the instance's working directory.</param>
    /// <param name="opts">Delete options — see <see cref="DeleteOptions"/>.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> on success; <see cref="FileOpOutcome.OutOfJail"/> as for
    /// <see cref="List"/>; <see cref="FileOpOutcome.NotFound"/> if absent;
    /// <see cref="FileOpOutcome.NotAFile"/> for a directory when <see cref="DeleteOptions.AllowDir"/> is
    /// false (or for a symlink/special file); <see cref="FileOpOutcome.IoError"/> for a non-empty
    /// directory, or any other filesystem failure.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="instance"/> is null or whitespace.</exception>
    FileOpResult Delete(string instance, string relPath, DeleteOptions opts);

    /// <summary>
    /// Renames/moves a file or directory inside the instance's jail. Both the source and the destination
    /// must resolve within the jail.
    /// </summary>
    /// <param name="instance">The instance whose working directory to operate within.</param>
    /// <param name="fromRel">The current path, relative to the instance's working directory.</param>
    /// <param name="toRel">The new path, relative to the instance's working directory.</param>
    /// <param name="opts">Rename options — see <see cref="RenameOptions"/>.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> with the moved entry's stat; <see cref="FileOpOutcome.OutOfJail"/>
    /// if EITHER <paramref name="fromRel"/> or <paramref name="toRel"/> escapes the jail;
    /// <see cref="FileOpOutcome.NotFound"/> if the source is absent; <see cref="FileOpOutcome.TargetExists"/>
    /// if the destination exists and <see cref="RenameOptions.Overwrite"/> is false.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="instance"/> is null or whitespace.</exception>
    FileOpResult<FileStat> Rename(string instance, string fromRel, string toRel, RenameOptions opts);
}

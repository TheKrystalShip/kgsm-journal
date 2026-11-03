using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Native;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// The default <see cref="IInstanceFiles"/> — the single jailed filesystem authority for an instance's
/// working directory. Injects <see cref="IInstanceService"/> (to resolve the jail root) and
/// <see cref="ILogger{TCategoryName}"/> — deliberately NOT <see cref="IKgsmCommandExecutor"/>, since this
/// is the first kgsm-lib service that does direct <c>System.IO</c> rather than shelling the engine.
/// </summary>
/// <remarks>
/// The jail:
/// <list type="number">
/// <item>The root is <c>Instance.WorkingDir</c>, resolved fresh on every call (no caching — instances
///   get reinstalled) and canonicalised via <see cref="InstanceJail.CanonicalRealPath"/>.</item>
/// <item>A candidate relative path is joined, then canonicalised the same way — resolving <c>.</c>/<c>..</c>
///   AND symlinks at EVERY path component (not just the leaf), following symlink chains up to
///   <see cref="InstanceJail.MaxSymlinkHops"/> — and the result must equal the root or be a <c>root + "/"</c>-prefixed
///   descendant (ordinal), else <see cref="FileOpOutcome.OutOfJail"/>. A non-existent tail component is
///   accepted verbatim (so a create/rename target resolves).</item>
/// <item>File-type gating is via <see cref="LibC.Lstat"/> (kgsm-lib's first P/Invoke): only
///   <c>Regular</c> is opened for read/write, <c>Directory</c> for list, and FIFO/socket/device
///   (<c>Special</c>) is never opened.</item>
/// <item>Binary detection is an 8&#160;KB NUL scan plus a full strict-UTF-8 decode.</item>
/// <item>Writes are atomic: a temp file in the same directory → <c>fsync</c> → mode-preserve → rename —
///   never truncate-in-place.</item>
/// </list>
/// </remarks>
public sealed class InstanceFiles : IInstanceFiles
{
    private const int BinaryScanBytes = 8192; // NUL-byte scan window

    private readonly IInstanceService _instances;
    private readonly ILogger<InstanceFiles> _logger;

    /// <summary>Initializes a new instance of the <see cref="InstanceFiles"/> class.</summary>
    /// <param name="instances">Resolves an instance's <c>WorkingDir</c> — the jail root.</param>
    /// <param name="logger">The logger to use for logging.</param>
    public InstanceFiles(IInstanceService instances, ILogger<InstanceFiles> logger)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public FileOpResult<DirListing> List(string instance, string? subdir, int maxEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instance, nameof(instance));

        if (!TryRoot(instance, out string root, out FileOpOutcome rootFailure))
            return FileOpResult<DirListing>.Fail(rootFailure);
        if (!InstanceJail.TryResolve(root, subdir, out string real, out _))
            return FileOpResult<DirListing>.Fail(FileOpOutcome.OutOfJail);

        LstatKind kind = LibC.Lstat(real);
        if (kind == LstatKind.Missing)
            return FileOpResult<DirListing>.Fail(FileOpOutcome.NotFound);
        if (kind != LstatKind.Directory)
            return FileOpResult<DirListing>.Fail(FileOpOutcome.NotADirectory);

        var all = new List<FileEntry>();
        IEnumerable<string> names;
        try { names = Directory.EnumerateFileSystemEntries(real); }
        catch (IOException) { return FileOpResult<DirListing>.Fail(FileOpOutcome.NotFound); }
        catch (UnauthorizedAccessException ex) { return FileOpResult<DirListing>.Fail(FileOpOutcome.IoError, ex.Message); }

        // lstat every entry (not just the cap) so dirs-first ordering — and therefore which entries
        // survive truncation — is deterministic; the cap is a render bound, not an enumeration one.
        foreach (string entryPath in names)
        {
            FileEntry? entry = DescribeEntry(entryPath);
            if (entry is not null) all.Add(entry);
        }

        all.Sort(static (a, b) =>
        {
            bool da = a.Kind == FileKind.Dir, db = b.Kind == FileKind.Dir;
            if (da != db) return da ? -1 : 1; // dirs first
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        bool truncated = all.Count > maxEntries;
        int take = Math.Max(0, maxEntries);
        IReadOnlyList<FileEntry> page = truncated ? all.GetRange(0, take) : all;
        return FileOpResult<DirListing>.Ok(new DirListing { Entries = page, Truncated = truncated });
    }

    /// <inheritdoc/>
    public FileOpResult<FindResult> Find(string instance, string pattern, string? subdir, FindOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instance, nameof(instance));
        if (string.IsNullOrWhiteSpace(pattern))
            return FileOpResult<FindResult>.Fail(FileOpOutcome.InvalidArgument, "a search pattern is required");

        FindOptions opts = options ?? new FindOptions();
        if (!TryWalkRoot(instance, subdir, out string root, out string start, out FileOpOutcome failure))
            return FileOpResult<FindResult>.Fail(failure);

        // A pattern naming a path segment is matched against the whole relative path; a bare name is
        // matched against the file name alone. That is what lets "*.ini" mean "anywhere" while
        // "Config/*.ini" still means "under a Config directory".
        bool pathScoped = pattern.Contains('/', StringComparison.Ordinal);

        var matches = new List<FindMatch>();
        bool truncated = false;
        WalkOutcome walk = Walk(root, start, opts, entry =>
        {
            string candidate = pathScoped ? entry.RelativePath : Path.GetFileName(entry.RelativePath);
            if (!FileSystemName.MatchesSimpleExpression(pattern, candidate, ignoreCase: true))
                return true;

            if (matches.Count >= opts.MaxResults) { truncated = true; return false; }
            matches.Add(new FindMatch(entry.RelativePath, entry.Kind, entry.SizeBytes, entry.Mtime));
            return true;
        });

        matches.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal));
        return FileOpResult<FindResult>.Ok(new FindResult
        {
            Matches = matches,
            Truncated = truncated,
            ScanLimitHit = walk.LimitHit,
            EntriesScanned = walk.Scanned,
        });
    }

    /// <inheritdoc/>
    public FileOpResult<FileSearchResult> Search(
        string instance, string pattern, string? subdir, FileSearchOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instance, nameof(instance));
        if (string.IsNullOrWhiteSpace(pattern))
            return FileOpResult<FileSearchResult>.Fail(FileOpOutcome.InvalidArgument, "a search pattern is required");

        FileSearchOptions opts = options ?? new FileSearchOptions();

        Regex regex;
        try
        {
            RegexOptions ro = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
            if (opts.IgnoreCase) ro |= RegexOptions.IgnoreCase;
            // NonBacktracking removes catastrophic backtracking as a category rather than bounding it
            // with a timeout; a caller-supplied expression is not one we can assume is well-behaved.
            regex = new Regex(pattern, ro);
        }
        catch (ArgumentException ex)
        {
            return FileOpResult<FileSearchResult>.Fail(FileOpOutcome.InvalidArgument, ex.Message);
        }

        if (!TryWalkRoot(instance, subdir, out string root, out string start, out FileOpOutcome failure))
            return FileOpResult<FileSearchResult>.Fail(failure);

        var hits = new List<SearchHit>();
        bool truncated = false;
        int filesRead = 0;
        bool readLimitHit = false;

        WalkOutcome walk = Walk(root, start, opts.Walk ?? new FindOptions(), entry =>
        {
            if (entry.Kind != FileKind.File) return true;
            if (entry.SizeBytes is > 0 && entry.SizeBytes > opts.MaxFileBytes) return true;
            if (filesRead >= opts.MaxFilesRead) { readLimitHit = true; return false; }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(Path.Combine(root, entry.RelativePath)); }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }

            filesRead++;
            if (LooksBinary(bytes)) return true;

            string text;
            try { text = Encoding.UTF8.GetString(bytes); }
            catch (ArgumentException) { return true; }

            int line = 0;
            foreach (string raw in text.Split('\n'))
            {
                line++;
                if (!regex.IsMatch(raw)) continue;

                if (hits.Count >= opts.MaxHits) { truncated = true; return false; }
                hits.Add(new SearchHit(entry.RelativePath, line, raw.TrimEnd('\r')));
                if (opts.FilesOnly) return true;   // one hit per file is the whole report
            }

            return true;
        });

        return FileOpResult<FileSearchResult>.Ok(new FileSearchResult
        {
            Hits = hits,
            Truncated = truncated,
            ScanLimitHit = walk.LimitHit || readLimitHit,
            FilesRead = filesRead,
        });
    }

    /// <summary>One entry seen by <see cref="Walk"/>, with its path relative to the jail root.</summary>
    private readonly record struct WalkEntry(string RelativePath, FileKind Kind, long? SizeBytes, DateTimeOffset? Mtime);

    /// <summary>How a walk ended: how much it saw, and whether it stopped on a budget.</summary>
    private readonly record struct WalkOutcome(int Scanned, bool LimitHit);

    /// <summary>
    /// Breadth-first walk from <paramref name="start"/>, invoking <paramref name="visit"/> per entry and
    /// stopping when it returns false.
    /// <para>
    /// <b>Every entry of a level is seen before any entry below it.</b> A walk that stops on its budget has
    /// then seen the shallow files a person looks for, the configs beside a game's data, rather than having
    /// spent the budget in the first deep folder it met: a world save of hundreds of thousands of files sits
    /// beside the four files of a server's config.
    /// </para>
    /// <para>
    /// <b>A symlinked directory is recorded and never descended into.</b> Containment therefore does not
    /// rest on a check applied after the fact — the walk simply has no path out of the jail. Directories
    /// named <c>backups</c> are skipped unless asked for.
    /// </para>
    /// </summary>
    private WalkOutcome Walk(string root, string start, FindOptions opts, Func<WalkEntry, bool> visit)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((start, 0));
        int scanned = 0;

        while (queue.Count > 0)
        {
            (string dir, int depth) = queue.Dequeue();

            IEnumerable<string> names;
            try { names = Directory.EnumerateFileSystemEntries(dir); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (string full in names)
            {
                if (scanned >= opts.MaxEntriesScanned)
                    return new WalkOutcome(scanned, true);
                scanned++;

                LstatKind lk = LibC.Lstat(full);
                FileKind kind = lk switch
                {
                    LstatKind.Regular => FileKind.File,
                    LstatKind.Directory => FileKind.Dir,
                    LstatKind.Symlink => FileKind.Symlink,
                    LstatKind.Missing => FileKind.Special,
                    _ => FileKind.Special,
                };
                if (lk == LstatKind.Missing) continue;

                string rel = Path.GetRelativePath(root, full).Replace('\\', '/');
                long? size = null;
                DateTimeOffset? mtime = null;
                if (kind == FileKind.File)
                {
                    try
                    {
                        var fi = new FileInfo(full);
                        size = fi.Length;
                        mtime = fi.LastWriteTimeUtc;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                if (!visit(new WalkEntry(rel, kind, size, mtime)))
                    return new WalkOutcome(scanned, false);

                // Descend only into real directories. A symlink is reported above and then left alone,
                // which is the whole containment guarantee for this walk.
                if (kind != FileKind.Dir || depth + 1 > opts.MaxDepth) continue;
                if (!opts.IncludeBackups
                    && string.Equals(Path.GetFileName(full), "backups", StringComparison.OrdinalIgnoreCase))
                    continue;

                queue.Enqueue((full, depth + 1));
            }
        }

        return new WalkOutcome(scanned, false);
    }

    /// <summary>Resolves the jail root and the walk's starting directory, both jail-checked.</summary>
    private bool TryWalkRoot(
        string instance, string? subdir, out string root, out string start, out FileOpOutcome failure)
    {
        start = string.Empty;
        failure = FileOpOutcome.Ok;

        if (!TryRoot(instance, out root, out failure))
            return false;
        if (!InstanceJail.TryResolve(root, subdir, out string real, out _))
        {
            failure = FileOpOutcome.OutOfJail;
            return false;
        }

        LstatKind kind = LibC.Lstat(real);
        if (kind == LstatKind.Missing) { failure = FileOpOutcome.NotFound; return false; }
        if (kind != LstatKind.Directory) { failure = FileOpOutcome.NotADirectory; return false; }

        start = real;
        return true;
    }

    /// <inheritdoc/>
    public FileOpResult<FileContent> Read(string instance, string relPath, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instance, nameof(instance));

        if (!TryRoot(instance, out string root, out FileOpOutcome rootFailure))
            return FileOpResult<FileContent>.Fail(rootFailure);
        if (!InstanceJail.TryResolve(root, relPath, out string real, out _))
            return FileOpResult<FileContent>.Fail(FileOpOutcome.OutOfJail);

        LstatKind kind = LibC.Lstat(real);
        if (kind == LstatKind.Missing)
            return FileOpResult<FileContent>.Fail(FileOpOutcome.NotFound);
        if (kind != LstatKind.Regular)
            return FileOpResult<FileContent>.Fail(FileOpOutcome.NotAFile);

        long size;
        try { size = new FileInfo(real).Length; }
        catch (IOException) { return FileOpResult<FileContent>.Fail(FileOpOutcome.NotFound); }
        if (size > maxBytes)
            return FileOpResult<FileContent>.Fail(FileOpOutcome.TooLarge);

        byte[] bytes;
        try { bytes = File.ReadAllBytes(real); }
        catch (IOException) { return FileOpResult<FileContent>.Fail(FileOpOutcome.NotFound); }
        catch (UnauthorizedAccessException ex) { return FileOpResult<FileContent>.Fail(FileOpOutcome.IoError, ex.Message); }

        if (LooksBinary(bytes))
            return FileOpResult<FileContent>.Fail(FileOpOutcome.Binary);

        DateTimeOffset mtime;
        try { mtime = new FileInfo(real).LastWriteTimeUtc; }
        catch (IOException) { mtime = default; }

        return FileOpResult<FileContent>.Ok(new FileContent
        {
            Content = Encoding.UTF8.GetString(bytes),
            SizeBytes = bytes.LongLength,
            Mtime = mtime,
            Etag = Etag(bytes),
        });
    }

    /// <inheritdoc/>
    public FileOpResult<FileStat> Write(string instance, string relPath, string content, WriteOptions opts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instance, nameof(instance));
        ArgumentNullException.ThrowIfNull(content, nameof(content));
        opts ??= new WriteOptions();

        if (!TryRoot(instance, out string root, out FileOpOutcome rootFailure))
            return FileOpResult<FileStat>.Fail(rootFailure);
        if (!InstanceJail.TryResolve(root, relPath, out string real, out _))
            return FileOpResult<FileStat>.Fail(FileOpOutcome.OutOfJail);

        LstatKind kind = LibC.Lstat(real);
        byte[]? current = null;

        if (kind == LstatKind.Missing)
        {
            if (!opts.AllowCreate)
                return FileOpResult<FileStat>.Fail(FileOpOutcome.NotFound);

            // The parent must already exist and be a directory — creation never builds a deep tree.
            string? parent = Path.GetDirectoryName(real);
            if (string.IsNullOrEmpty(parent))
                return FileOpResult<FileStat>.Fail(FileOpOutcome.OutOfJail);

            LstatKind parentKind = LibC.Lstat(parent);
            if (parentKind == LstatKind.Missing)
                return FileOpResult<FileStat>.Fail(FileOpOutcome.NotFound);
            if (parentKind != LstatKind.Directory)
                return FileOpResult<FileStat>.Fail(FileOpOutcome.NotADirectory);
        }
        else if (kind != LstatKind.Regular)
        {
            return FileOpResult<FileStat>.Fail(FileOpOutcome.NotAFile); // refuse to clobber a dir/special
        }
        else
        {
            try { current = File.ReadAllBytes(real); }
            catch (IOException) { return FileOpResult<FileStat>.Fail(FileOpOutcome.NotFound); }
            catch (UnauthorizedAccessException ex) { return FileOpResult<FileStat>.Fail(FileOpOutcome.IoError, ex.Message); }

            if (LooksBinary(current))
                return FileOpResult<FileStat>.Fail(FileOpOutcome.Binary); // refuse to clobber a binary file

            if (!string.IsNullOrEmpty(opts.ExpectedEtag)
                && !string.Equals(opts.ExpectedEtag, Etag(current), StringComparison.Ordinal))
                return FileOpResult<FileStat>.Fail(FileOpOutcome.EtagMismatch);
        }

        byte[] newBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        if (newBytes.LongLength > opts.MaxBytes)
            return FileOpResult<FileStat>.Fail(FileOpOutcome.TooLarge);

        if (opts.Backup && current is { Length: > 0 })
        {
            try { File.WriteAllBytes(real + ".kgsmbak", current); }
            catch (IOException) { /* best-effort backup — never fails the write over it */ }
        }

        // Atomic write: temp in the SAME dir → fsync → preserve mode (existing target only) → rename.
        string dir = Path.GetDirectoryName(real)!;
        string tmp = Path.Combine(dir, "." + Path.GetFileName(real) + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(newBytes, 0, newBytes.Length);
                fs.Flush(flushToDisk: true); // fsync
            }
            if (!OperatingSystem.IsWindows() && kind == LstatKind.Regular) // mode-preserve only makes sense for an overwrite
            {
                try { File.SetUnixFileMode(tmp, File.GetUnixFileMode(real)); } catch { /* mode best-effort */ }
            }
            File.Move(tmp, real, overwrite: true); // rename(2) — atomic on the same filesystem
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore cleanup failure */ }
            return FileOpResult<FileStat>.Fail(FileOpOutcome.IoError, ex.Message);
        }

        DateTimeOffset mtime;
        try { mtime = new FileInfo(real).LastWriteTimeUtc; }
        catch (IOException) { mtime = default; }

        return FileOpResult<FileStat>.Ok(new FileStat
        {
            SizeBytes = newBytes.LongLength,
            Mtime = mtime,
            Etag = Etag(newBytes),
        });
    }

    /// <inheritdoc/>
    public FileOpResult Delete(string instance, string relPath, DeleteOptions opts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instance, nameof(instance));
        opts ??= new DeleteOptions();

        if (!TryRoot(instance, out string root, out FileOpOutcome rootFailure))
            return FileOpResult.Fail(rootFailure);
        if (!InstanceJail.TryResolve(root, relPath, out string real, out _))
            return FileOpResult.Fail(FileOpOutcome.OutOfJail);

        LstatKind kind = LibC.Lstat(real);
        if (kind == LstatKind.Missing)
            return FileOpResult.Fail(FileOpOutcome.NotFound);

        if (kind == LstatKind.Directory)
        {
            if (!opts.AllowDir)
                return FileOpResult.Fail(FileOpOutcome.NotAFile);

            try
            {
                if (Directory.EnumerateFileSystemEntries(real).Any())
                    return FileOpResult.Fail(FileOpOutcome.IoError, "directory is not empty"); // never recursive
                Directory.Delete(real);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return FileOpResult.Fail(FileOpOutcome.IoError, ex.Message);
            }
            return FileOpResult.Ok();
        }

        if (kind != LstatKind.Regular)
            return FileOpResult.Fail(FileOpOutcome.NotAFile); // symlink/special — refused

        try { File.Delete(real); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileOpResult.Fail(FileOpOutcome.IoError, ex.Message);
        }
        return FileOpResult.Ok();
    }

    /// <inheritdoc/>
    public FileOpResult<FileStat> Rename(string instance, string fromRel, string toRel, RenameOptions opts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instance, nameof(instance));
        opts ??= new RenameOptions();

        if (!TryRoot(instance, out string root, out FileOpOutcome rootFailure))
            return FileOpResult<FileStat>.Fail(rootFailure);
        if (!InstanceJail.TryResolve(root, fromRel, out string fromReal, out _))
            return FileOpResult<FileStat>.Fail(FileOpOutcome.OutOfJail);
        if (!InstanceJail.TryResolve(root, toRel, out string toReal, out _))
            return FileOpResult<FileStat>.Fail(FileOpOutcome.OutOfJail);

        LstatKind fromKind = LibC.Lstat(fromReal);
        if (fromKind == LstatKind.Missing)
            return FileOpResult<FileStat>.Fail(FileOpOutcome.NotFound);

        LstatKind toKind = LibC.Lstat(toReal);
        if (toKind != LstatKind.Missing && !opts.Overwrite)
            return FileOpResult<FileStat>.Fail(FileOpOutcome.TargetExists);

        try
        {
            if (fromKind == LstatKind.Directory)
                Directory.Move(fromReal, toReal);
            else
                File.Move(fromReal, toReal, overwrite: opts.Overwrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileOpResult<FileStat>.Fail(FileOpOutcome.IoError, ex.Message);
        }

        long size = 0;
        string etag = string.Empty;
        if (LibC.Lstat(toReal) == LstatKind.Regular)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(toReal);
                size = bytes.LongLength;
                etag = Etag(bytes);
            }
            catch (IOException) { /* best-effort stat */ }
        }

        DateTimeOffset mtime;
        try { mtime = new FileInfo(toReal).LastWriteTimeUtc; }
        catch (IOException) { mtime = default; }

        return FileOpResult<FileStat>.Ok(new FileStat { SizeBytes = size, Mtime = mtime, Etag = etag });
    }

    // ---- jail root -----------------------------------------------------------------------------

    /// <summary>Resolves the instance's canonicalised jail root, fresh every call (never cached).</summary>
    private bool TryRoot(string instance, out string root, out FileOpOutcome failure)
    {
        root = string.Empty;
        failure = FileOpOutcome.Ok;

        Instance? info;
        try { info = _instances.GetInstanceInfo(instance); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve instance info for {Instance}", instance);
            failure = FileOpOutcome.InstanceUnavailable;
            return false;
        }

        string? workingDir = info?.WorkingDir;
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            failure = FileOpOutcome.InstanceUnavailable;
            return false;
        }

        try { root = InstanceJail.CanonicalRealPath(Path.GetFullPath(workingDir)); }
        catch (IOException)
        {
            failure = FileOpOutcome.InstanceUnavailable; // e.g. a symlink loop in the working dir itself
            return false;
        }
        return true;
    }

    // ---- the jail (the load-bearing security boundary) -----------------------------------------



    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>Describes one directory entry from its own (un-followed) type — symlinks are listed but
    /// not resolved here (that's only for read/write/traverse targets).</summary>
    private static FileEntry? DescribeEntry(string entryPath)
    {
        string name = Path.GetFileName(entryPath);
        LstatKind kind = LibC.Lstat(entryPath);
        DateTimeOffset? mtime = TryMtime(entryPath);

        return kind switch
        {
            LstatKind.Directory => new FileEntry(name, FileKind.Dir, null, mtime),
            LstatKind.Symlink => new FileEntry(name, FileKind.Symlink, null, mtime),
            LstatKind.Special => new FileEntry(name, FileKind.Special, null, mtime),
            LstatKind.Regular => new FileEntry(name, FileKind.File, TryLength(entryPath), mtime),
            _ => null, // vanished between readdir and lstat — drop it
        };
    }

    private static DateTimeOffset? TryMtime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return null; }
    }

    private static long? TryLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return null; }
    }

    /// <summary>Heuristic "is this binary, not text": a NUL byte in the first 8&#160;KB, or any invalid
    /// UTF-8 sequence anywhere (strict decode).</summary>
    private static bool LooksBinary(byte[] bytes)
    {
        int scan = Math.Min(bytes.Length, BinaryScanBytes);
        for (int i = 0; i < scan; i++)
            if (bytes[i] == 0) return true;
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return false;
        }
        catch (DecoderFallbackException) { return true; }
    }

    /// <summary>Honest content identity for optimistic concurrency — <c>sha256:&lt;hex&gt;</c> over the
    /// raw bytes.</summary>
    private static string Etag(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
}

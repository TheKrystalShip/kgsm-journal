using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Tests for <see cref="InstanceFiles"/> — the jailed instance-filesystem authority. This IS the
/// security boundary (path traversal, symlink escapes, special files, size/binary/etag policy), so it
/// runs against a REAL temp-dir jail rather than mocked <c>System.IO</c> — the same shape kgsm-api's own
/// <c>InstanceFileServiceTests</c> used for its (now-ported) jail.
/// </summary>
public sealed class InstanceFilesTests : IDisposable
{
    private const string InstanceName = "test-instance";

    private readonly string _tempBase;
    private readonly string _root;
    private readonly Mock<IInstanceService> _mockInstances;
    private readonly InstanceFiles _sut;

    public InstanceFilesTests()
    {
        // root = <tempBase>/inst — a known, non-random leaf name so the sibling-prefix test can create
        // a real "<tempBase>/inst-evil" neighbour (a naive `real.StartsWith(root)` containment check,
        // without the trailing separator, would wrongly admit it).
        _tempBase = Directory.CreateTempSubdirectory("kgsm-instancefiles-").FullName;
        _root = Path.Combine(_tempBase, "inst");
        Directory.CreateDirectory(_root);

        _mockInstances = new Mock<IInstanceService>();
        _mockInstances.Setup(x => x.GetInstanceInfo(InstanceName))
            .Returns(new Instance { Name = InstanceName, WorkingDir = _root });

        _sut = new InstanceFiles(_mockInstances.Object, NullLogger<InstanceFiles>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempBase, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private string Abs(string rel) => Path.Combine(_root, rel);

    private static void WriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteText(string path, string text) =>
        WriteBytes(path, new UTF8Encoding(false).GetBytes(text));

    /// <summary>Creates a POSIX FIFO via the <c>mkfifo</c> coreutil (always present on the Linux hosts
    /// this repo targets) — the special-file case <see cref="LibC"/>'s lstat gate must refuse.</summary>
    private static void CreateFifo(string path)
    {
        var psi = new ProcessStartInfo("mkfifo")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(path);
        using var proc = Process.Start(psi)!;
        proc.WaitForExit(5000);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"mkfifo failed: {proc.StandardError.ReadToEnd()}");
    }

    // ---- constructor guards ------------------------------------------------------------------------

    [Fact]
    public void Constructor_NullInstanceService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new InstanceFiles(null!, NullLogger<InstanceFiles>.Instance));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new InstanceFiles(_mockInstances.Object, null!));
    }

    // ---- argument validation (representative — ThrowIfNullOrWhiteSpace is shared across methods) ----

    [Fact]
    public void List_NullInstance_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.List(null!, null, 100));
    }

    [Fact]
    public void List_WhitespaceInstance_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _sut.List("   ", null, 100));
    }

    [Fact]
    public void Write_NullContent_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _sut.Write(InstanceName, "a.txt", null!, new WriteOptions { AllowCreate = true, MaxBytes = 1_000 }));
    }

    // ---- instance resolution -------------------------------------------------------------------------

    [Fact]
    public void Read_UnknownInstance_ReturnsInstanceUnavailable()
    {
        var result = _sut.Read("no-such-instance", "a.txt", 1_000);
        Assert.Equal(FileOpOutcome.InstanceUnavailable, result.Outcome);
    }

    [Fact]
    public void Read_InstanceWithBlankWorkingDir_ReturnsInstanceUnavailable()
    {
        _mockInstances.Setup(x => x.GetInstanceInfo("blank-wd"))
            .Returns(new Instance { Name = "blank-wd", WorkingDir = "" });

        var result = _sut.Read("blank-wd", "a.txt", 1_000);
        Assert.Equal(FileOpOutcome.InstanceUnavailable, result.Outcome);
    }

    // ---- path traversal ------------------------------------------------------------------------------

    [Theory]
    [InlineData("../escape.txt")]           // one level up — already outside a leaf jail dir
    [InlineData("../../etc/passwd")]        // multiple levels up
    [InlineData("/../etc/passwd")]          // leading-slash + traversal still escapes after normalization
    public void Read_TraversalEscape_ReturnsOutOfJail(string rel)
    {
        var result = _sut.Read(InstanceName, rel, 1_000);
        Assert.Equal(FileOpOutcome.OutOfJail, result.Outcome);
    }

    [Fact]
    public void Read_NulByteInPath_ReturnsOutOfJail()
    {
        var result = _sut.Read(InstanceName, "abc\0def.txt", 1_000);
        Assert.Equal(FileOpOutcome.OutOfJail, result.Outcome);
    }

    [Fact]
    public void Read_BareAbsolutePath_IsConfinedToJail_NeverReachesHostPath()
    {
        // A caller-supplied leading '/' is neutralized to jail-relative (the ported kgsm-api design:
        // the caller's root IS the jail, so "/etc/passwd" means "<jail>/etc/passwd", not the host's
        // /etc/passwd). The security property under test is that it can NEVER read the real host file —
        // it must not be Ok, and it resolves to a (non-existent) path inside the jail, not OutOfJail.
        var result = _sut.Read(InstanceName, "/etc/passwd", 1_000);
        Assert.NotEqual(FileOpOutcome.Ok, result.Outcome);
        Assert.Equal(FileOpOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public void Read_SiblingPrefixDirectory_ReturnsOutOfJail()
    {
        // root is "<tempBase>/inst"; "<tempBase>/inst-evil" is a REAL sibling starting with the same
        // string prefix. A naive `real.StartsWith(root)` (no trailing separator) would wrongly admit it.
        string evilDir = Path.Combine(_tempBase, "inst-evil");
        Directory.CreateDirectory(evilDir);
        WriteText(Path.Combine(evilDir, "secret.txt"), "should never be visible");

        var result = _sut.Read(InstanceName, "../inst-evil/secret.txt", 1_000);
        Assert.Equal(FileOpOutcome.OutOfJail, result.Outcome);
    }

    // ---- symlink escapes -------------------------------------------------------------------------

    [Fact]
    public void Read_LeafSymlinkEscapingJail_ReturnsOutOfJail()
    {
        string outside = Directory.CreateTempSubdirectory("kgsm-instancefiles-outside-").FullName;
        try
        {
            string target = Path.Combine(outside, "secret.txt");
            WriteText(target, "host secret");
            File.CreateSymbolicLink(Abs("leaf-link.txt"), target);

            var result = _sut.Read(InstanceName, "leaf-link.txt", 1_000);
            Assert.Equal(FileOpOutcome.OutOfJail, result.Outcome);
        }
        finally { try { Directory.Delete(outside, recursive: true); } catch { } }
    }

    [Fact]
    public void Read_IntermediateDirectorySymlinkEscapingJail_ReturnsOutOfJail()
    {
        // The leaf component ("insider.txt") is not itself a symlink — only a full every-component
        // realpath walk (not a leaf-only recheck) catches this.
        string outside = Directory.CreateTempSubdirectory("kgsm-instancefiles-outside-").FullName;
        try
        {
            WriteText(Path.Combine(outside, "insider.txt"), "host secret");
            Directory.CreateSymbolicLink(Abs("subdir-link"), outside);

            var result = _sut.Read(InstanceName, "subdir-link/insider.txt", 1_000);
            Assert.Equal(FileOpOutcome.OutOfJail, result.Outcome);
        }
        finally { try { Directory.Delete(outside, recursive: true); } catch { } }
    }

    [Fact]
    public async Task Read_SymlinkLoop_IsHandledWithoutHanging()
    {
        File.CreateSymbolicLink(Abs("loopA"), Abs("loopB"));
        File.CreateSymbolicLink(Abs("loopB"), Abs("loopA"));

        Task<FileOpResult<FileContent>> task = Task.Run(() => _sut.Read(InstanceName, "loopA", 1_000));
        Task completedTask = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(task, completedTask); // a symlink loop must be rejected, not hang the caller
        FileOpResult<FileContent> result = await task;
        Assert.Equal(FileOpOutcome.OutOfJail, result.Outcome);
    }

    // ---- special files ------------------------------------------------------------------------------

    [Fact]
    public void Read_Fifo_ReturnsNotAFile()
    {
        string fifo = Abs("myfifo");
        CreateFifo(fifo);

        var result = _sut.Read(InstanceName, "myfifo", 1_000);
        Assert.Equal(FileOpOutcome.NotAFile, result.Outcome);
    }

    [Fact]
    public void Delete_Fifo_ReturnsNotAFile()
    {
        string fifo = Abs("myfifo2");
        CreateFifo(fifo);

        var result = _sut.Delete(InstanceName, "myfifo2", new DeleteOptions());
        Assert.Equal(FileOpOutcome.NotAFile, result.Outcome);
        Assert.True(File.Exists(fifo)); // refused — the FIFO is left in place
    }

    // ---- read caps / binary --------------------------------------------------------------------------

    [Fact]
    public void Read_OverMaxBytes_ReturnsTooLarge()
    {
        WriteText(Abs("big.txt"), new string('x', 100));

        var result = _sut.Read(InstanceName, "big.txt", maxBytes: 10);
        Assert.Equal(FileOpOutcome.TooLarge, result.Outcome);
    }

    [Fact]
    public void Read_BinaryFile_ReturnsBinary()
    {
        WriteBytes(Abs("bin.dat"), [0x00, 0x01, 0x02, 0x66, 0x6f, 0x6f]);

        var result = _sut.Read(InstanceName, "bin.dat", 1_000);
        Assert.Equal(FileOpOutcome.Binary, result.Outcome);
    }

    [Fact]
    public void Read_MissingFile_ReturnsNotFound()
    {
        var result = _sut.Read(InstanceName, "nope.txt", 1_000);
        Assert.Equal(FileOpOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public void Read_Directory_ReturnsNotAFile()
    {
        Directory.CreateDirectory(Abs("adir"));

        var result = _sut.Read(InstanceName, "adir", 1_000);
        Assert.Equal(FileOpOutcome.NotAFile, result.Outcome);
    }

    [Fact]
    public void Read_ExistingFile_ReturnsContentAndNonEmptyEtag()
    {
        WriteText(Abs("hello.txt"), "hello world");

        var result = _sut.Read(InstanceName, "hello.txt", 1_000);

        Assert.True(result.IsOk);
        Assert.Equal("hello world", result.Value!.Content);
        Assert.Equal(11, result.Value.SizeBytes);
        Assert.StartsWith("sha256:", result.Value.Etag);
    }

    // ---- write ----------------------------------------------------------------------------------------

    [Fact]
    public void Write_OverwriteExistingFile_UpdatesContentAtomically()
    {
        WriteText(Abs("cfg.ini"), "old content");

        var result = _sut.Write(InstanceName, "cfg.ini", "new content",
            new WriteOptions { MaxBytes = 1_000 });

        Assert.True(result.IsOk);
        Assert.Equal("new content", File.ReadAllText(Abs("cfg.ini")));
        // no leftover temp files from the atomic rename
        Assert.DoesNotContain(Directory.EnumerateFiles(_root), f => Path.GetFileName(f).Contains(".tmp-"));
    }

    [Fact]
    public void Write_MissingTarget_AllowCreateFalse_ReturnsNotFound()
    {
        var result = _sut.Write(InstanceName, "new.txt", "content",
            new WriteOptions { AllowCreate = false, MaxBytes = 1_000 });

        Assert.Equal(FileOpOutcome.NotFound, result.Outcome);
        Assert.False(File.Exists(Abs("new.txt")));
    }

    [Fact]
    public void Write_MissingTarget_AllowCreateTrue_ExistingDir_Creates()
    {
        var result = _sut.Write(InstanceName, "new.txt", "content",
            new WriteOptions { AllowCreate = true, MaxBytes = 1_000 });

        Assert.True(result.IsOk);
        Assert.Equal("content", File.ReadAllText(Abs("new.txt")));
    }

    [Fact]
    public void Write_MissingTarget_AllowCreateTrue_MissingParentDir_ReturnsNotFound()
    {
        var result = _sut.Write(InstanceName, "missingdir/new.txt", "content",
            new WriteOptions { AllowCreate = true, MaxBytes = 1_000 });

        // Never creates a deep tree — the parent must already exist.
        Assert.Equal(FileOpOutcome.NotFound, result.Outcome);
        Assert.False(Directory.Exists(Abs("missingdir")));
    }

    [Fact]
    public void Write_TargetIsDirectory_ReturnsNotAFile()
    {
        Directory.CreateDirectory(Abs("adir"));

        var result = _sut.Write(InstanceName, "adir", "content",
            new WriteOptions { AllowCreate = true, MaxBytes = 1_000 });

        Assert.Equal(FileOpOutcome.NotAFile, result.Outcome);
    }

    [Fact]
    public void Write_OverMaxBytes_ReturnsTooLarge_AndDoesNotModifyFile()
    {
        WriteText(Abs("cfg.ini"), "orig");

        var result = _sut.Write(InstanceName, "cfg.ini", new string('y', 100),
            new WriteOptions { MaxBytes = 10 });

        Assert.Equal(FileOpOutcome.TooLarge, result.Outcome);
        Assert.Equal("orig", File.ReadAllText(Abs("cfg.ini")));
    }

    [Fact]
    public void Write_ExistingBinaryTarget_RefusesClobber()
    {
        WriteBytes(Abs("bin.dat"), [0x00, 0x01, 0x02]);

        var result = _sut.Write(InstanceName, "bin.dat", "text",
            new WriteOptions { MaxBytes = 1_000 });

        Assert.Equal(FileOpOutcome.Binary, result.Outcome);
    }

    [Fact]
    public void Write_StaleExpectedEtag_ReturnsEtagMismatch()
    {
        WriteText(Abs("cfg.ini"), "v1");

        var result = _sut.Write(InstanceName, "cfg.ini", "v2",
            new WriteOptions { MaxBytes = 1_000, ExpectedEtag = "sha256:not-the-real-one" });

        Assert.Equal(FileOpOutcome.EtagMismatch, result.Outcome);
        Assert.Equal("v1", File.ReadAllText(Abs("cfg.ini")));
    }

    [Fact]
    public void Write_CorrectExpectedEtag_Succeeds()
    {
        WriteText(Abs("cfg.ini"), "v1");
        string realEtag = _sut.Read(InstanceName, "cfg.ini", 1_000).Value!.Etag;

        var result = _sut.Write(InstanceName, "cfg.ini", "v2",
            new WriteOptions { MaxBytes = 1_000, ExpectedEtag = realEtag });

        Assert.True(result.IsOk);
        Assert.Equal("v2", File.ReadAllText(Abs("cfg.ini")));
    }

    [Fact]
    public void Write_BackupOptionOn_CreatesKgsmbakOfOldContent()
    {
        WriteText(Abs("cfg.ini"), "old content");

        var result = _sut.Write(InstanceName, "cfg.ini", "new content",
            new WriteOptions { MaxBytes = 1_000, Backup = true });

        Assert.True(result.IsOk);
        Assert.True(File.Exists(Abs("cfg.ini.kgsmbak")));
        Assert.Equal("old content", File.ReadAllText(Abs("cfg.ini.kgsmbak")));
        Assert.Equal("new content", File.ReadAllText(Abs("cfg.ini")));
    }

    [Fact]
    public void Write_BackupOptionOff_NoKgsmbakCreated()
    {
        WriteText(Abs("cfg2.ini"), "old content");

        _sut.Write(InstanceName, "cfg2.ini", "new content", new WriteOptions { MaxBytes = 1_000 });

        Assert.False(File.Exists(Abs("cfg2.ini.kgsmbak")));
    }

    [Fact]
    public void Write_TargetEscapesJail_ReturnsOutOfJail()
    {
        var result = _sut.Write(InstanceName, "../escape.txt", "content",
            new WriteOptions { AllowCreate = true, MaxBytes = 1_000 });

        Assert.Equal(FileOpOutcome.OutOfJail, result.Outcome);
    }

    // ---- delete -----------------------------------------------------------------------------------

    [Fact]
    public void Delete_ExistingFile_Deletes()
    {
        WriteText(Abs("gone.txt"), "bye");

        var result = _sut.Delete(InstanceName, "gone.txt", new DeleteOptions());

        Assert.True(result.IsOk);
        Assert.False(File.Exists(Abs("gone.txt")));
    }

    [Fact]
    public void Delete_MissingFile_ReturnsNotFound()
    {
        var result = _sut.Delete(InstanceName, "nope.txt", new DeleteOptions());
        Assert.Equal(FileOpOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public void Delete_Directory_AllowDirFalse_ReturnsNotAFile()
    {
        Directory.CreateDirectory(Abs("adir"));

        var result = _sut.Delete(InstanceName, "adir", new DeleteOptions { AllowDir = false });

        Assert.Equal(FileOpOutcome.NotAFile, result.Outcome);
        Assert.True(Directory.Exists(Abs("adir")));
    }

    [Fact]
    public void Delete_EmptyDirectory_AllowDirTrue_Deletes()
    {
        Directory.CreateDirectory(Abs("emptydir"));

        var result = _sut.Delete(InstanceName, "emptydir", new DeleteOptions { AllowDir = true });

        Assert.True(result.IsOk);
        Assert.False(Directory.Exists(Abs("emptydir")));
    }

    [Fact]
    public void Delete_NonEmptyDirectory_AllowDirTrue_RefusesRecursiveDelete()
    {
        Directory.CreateDirectory(Abs("fulldir"));
        WriteText(Abs("fulldir/child.txt"), "x");

        var result = _sut.Delete(InstanceName, "fulldir", new DeleteOptions { AllowDir = true });

        Assert.False(result.IsOk);
        Assert.True(Directory.Exists(Abs("fulldir")));
        Assert.True(File.Exists(Abs("fulldir/child.txt")));
    }

    [Fact]
    public void Delete_TargetEscapesJail_ReturnsOutOfJail()
    {
        var result = _sut.Delete(InstanceName, "../escape.txt", new DeleteOptions());
        Assert.Equal(FileOpOutcome.OutOfJail, result.Outcome);
    }

    // ---- rename -----------------------------------------------------------------------------------

    [Fact]
    public void Rename_InJail_MovesFileAndPreservesContent()
    {
        WriteText(Abs("from.txt"), "payload");

        var result = _sut.Rename(InstanceName, "from.txt", "to.txt", new RenameOptions());

        Assert.True(result.IsOk);
        Assert.False(File.Exists(Abs("from.txt")));
        Assert.Equal("payload", File.ReadAllText(Abs("to.txt")));
    }

    [Fact]
    public void Rename_MissingSource_ReturnsNotFound()
    {
        var result = _sut.Rename(InstanceName, "nope.txt", "to.txt", new RenameOptions());
        Assert.Equal(FileOpOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public void Rename_DestinationExists_OverwriteFalse_ReturnsTargetExists()
    {
        WriteText(Abs("from.txt"), "a");
        WriteText(Abs("to.txt"), "b");

        var result = _sut.Rename(InstanceName, "from.txt", "to.txt", new RenameOptions { Overwrite = false });

        Assert.Equal(FileOpOutcome.TargetExists, result.Outcome);
        Assert.Equal("a", File.ReadAllText(Abs("from.txt")));
        Assert.Equal("b", File.ReadAllText(Abs("to.txt")));
    }

    [Fact]
    public void Rename_DestinationExists_OverwriteTrue_Replaces()
    {
        WriteText(Abs("from.txt"), "a");
        WriteText(Abs("to.txt"), "b");

        var result = _sut.Rename(InstanceName, "from.txt", "to.txt", new RenameOptions { Overwrite = true });

        Assert.True(result.IsOk);
        Assert.Equal("a", File.ReadAllText(Abs("to.txt")));
    }

    [Fact]
    public void Rename_ToEscapesJail_ReturnsOutOfJail()
    {
        WriteText(Abs("from.txt"), "a");

        var result = _sut.Rename(InstanceName, "from.txt", "../escape.txt", new RenameOptions());

        Assert.Equal(FileOpOutcome.OutOfJail, result.Outcome);
        Assert.True(File.Exists(Abs("from.txt"))); // untouched
    }

    [Fact]
    public void Rename_FromEscapesJail_ReturnsOutOfJail()
    {
        var result = _sut.Rename(InstanceName, "../escape.txt", "to.txt", new RenameOptions());
        Assert.Equal(FileOpOutcome.OutOfJail, result.Outcome);
    }

    // ---- list -----------------------------------------------------------------------------------

    [Fact]
    public void List_WorkingDirectory_ReturnsEntries()
    {
        WriteText(Abs("a.txt"), "a");
        WriteText(Abs("b.txt"), "b");
        Directory.CreateDirectory(Abs("cdir"));

        var result = _sut.List(InstanceName, null, 100);

        Assert.True(result.IsOk);
        Assert.Equal(3, result.Value!.Entries.Count);
        Assert.False(result.Value.Truncated);
        // dirs-first ordering
        Assert.Equal(FileKind.Dir, result.Value.Entries[0].Kind);
    }

    [Fact]
    public void List_OverMaxEntries_SetsTruncated()
    {
        for (int i = 0; i < 5; i++)
            WriteText(Abs($"f{i}.txt"), "x");

        var result = _sut.List(InstanceName, null, 2);

        Assert.True(result.IsOk);
        Assert.Equal(2, result.Value!.Entries.Count);
        Assert.True(result.Value.Truncated);
    }

    [Fact]
    public void List_MissingSubdir_ReturnsNotFound()
    {
        var result = _sut.List(InstanceName, "nope", 100);
        Assert.Equal(FileOpOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public void List_TargetIsFileNotDirectory_ReturnsNotADirectory()
    {
        WriteText(Abs("notadir.txt"), "x");

        var result = _sut.List(InstanceName, "notadir.txt", 100);

        Assert.Equal(FileOpOutcome.NotADirectory, result.Outcome);
    }

    [Fact]
    public void List_SubdirEscapesJail_ReturnsOutOfJail()
    {
        var result = _sut.List(InstanceName, "../", 100);
        Assert.Equal(FileOpOutcome.OutOfJail, result.Outcome);
    }

    // --- Find / Search: the recursive walk, and the containment it must not lose -------------------

    [Fact]
    public void Find_MatchesByNameAnywhereInTheTree()
    {
        Directory.CreateDirectory(Abs("install/Pal/Saved/Config/LinuxServer"));
        File.WriteAllText(Abs("install/Pal/Saved/Config/LinuxServer/PalWorldSettings.ini"), "x=1");
        File.WriteAllText(Abs("install/other.txt"), "no");

        var result = _sut.Find(InstanceName, "*.ini", subdir: null);

        Assert.Equal(FileOpOutcome.Ok, result.Outcome);
        Assert.Equal(
            new[] { "install/Pal/Saved/Config/LinuxServer/PalWorldSettings.ini" },
            result.Value!.Matches.Select(m => m.Path));
    }

    [Fact]
    public void Find_WithPathScopedPattern_MatchesAgainstTheRelativePath()
    {
        Directory.CreateDirectory(Abs("a/Config"));
        Directory.CreateDirectory(Abs("b"));
        File.WriteAllText(Abs("a/Config/server.ini"), "x");
        File.WriteAllText(Abs("b/server.ini"), "x");

        var result = _sut.Find(InstanceName, "*/Config/*.ini", subdir: null);

        Assert.Equal(new[] { "a/Config/server.ini" }, result.Value!.Matches.Select(m => m.Path));
    }

    [Fact]
    public void Find_DoesNotDescendIntoASymlinkedDirectory_SoTheWalkCannotLeaveTheJail()
    {
        // A secret outside the jail, reachable only by following the link.
        string outside = Path.Combine(_tempBase, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.ini"), "top secret");

        Directory.CreateDirectory(Abs("install"));
        Directory.CreateSymbolicLink(Abs("install/escape"), outside);

        var result = _sut.Find(InstanceName, "*.ini", subdir: null);

        Assert.Equal(FileOpOutcome.Ok, result.Outcome);
        Assert.DoesNotContain(result.Value!.Matches, m => m.Path.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Find_ReportsTruncationSeparatelyFromAnIncompleteWalk()
    {
        Directory.CreateDirectory(Abs("many"));
        for (int i = 0; i < 10; i++)
            File.WriteAllText(Abs($"many/f{i}.ini"), "x");

        var capped = _sut.Find(InstanceName, "*.ini", null, new FindOptions(MaxResults: 3));
        Assert.True(capped.Value!.Truncated);
        Assert.False(capped.Value!.ScanLimitHit);      // it finished looking; it just returned fewer
        Assert.Equal(3, capped.Value!.Matches.Count);

        var starved = _sut.Find(InstanceName, "*.ini", null, new FindOptions(MaxEntriesScanned: 2));
        Assert.True(starved.Value!.ScanLimitHit);      // it stopped looking — NOT "that is all there is"
    }

    [Fact]
    public void Find_SeesTheShallowConfigBeforeSpendingItsBudgetInADeepWorldFolder()
    {
        Directory.CreateDirectory(Abs("saves/Saves/Multiplayer/default/map"));
        for (int i = 0; i < 50; i++)
            File.WriteAllText(Abs($"saves/Saves/Multiplayer/default/map/chunk{i}.bin"), "x");
        Directory.CreateDirectory(Abs("saves/Server"));
        File.WriteAllText(Abs("saves/Server/default.ini"), "Mods=");

        var found = _sut.Find(InstanceName, "*.ini", null, new FindOptions(MaxEntriesScanned: 12));

        Assert.True(found.Value!.ScanLimitHit);
        Assert.Contains("saves/Server/default.ini", found.Value!.Matches.Select(m => m.Path));
    }

    [Fact]
    public void Find_SkipsBackupsUnlessAsked()
    {
        Directory.CreateDirectory(Abs("backups/old"));
        File.WriteAllText(Abs("backups/old/server.ini"), "archived");
        File.WriteAllText(Abs("server.ini"), "live");

        Assert.Equal(new[] { "server.ini" },
            _sut.Find(InstanceName, "*.ini", null).Value!.Matches.Select(m => m.Path));

        var withBackups = _sut.Find(InstanceName, "*.ini", null, new FindOptions(IncludeBackups: true));
        Assert.Equal(2, withBackups.Value!.Matches.Count);
    }

    [Fact]
    public void Search_ReportsMatchingLinesWithPathAndLineNumber()
    {
        Directory.CreateDirectory(Abs("cfg"));
        File.WriteAllText(Abs("cfg/a.ini"), "alpha=1\nMaxPlayers=32\ngamma=3\n");

        var result = _sut.Search(InstanceName, "MaxPlayers", subdir: null);

        Assert.Equal(FileOpOutcome.Ok, result.Outcome);
        SearchHit hit = Assert.Single(result.Value!.Hits);
        Assert.Equal("cfg/a.ini", hit.Path);
        Assert.Equal(2, hit.LineNumber);
        Assert.Equal("MaxPlayers=32", hit.Line);
    }

    [Fact]
    public void Search_SkipsBinaryFiles_AndRefusesAnUncompilableExpression()
    {
        File.WriteAllBytes(Abs("blob.dat"), new byte[] { 0x00, 0x01, (byte)'h', (byte)'i' });
        Assert.Empty(_sut.Search(InstanceName, "hi", null).Value!.Hits);

        var bad = _sut.Search(InstanceName, "([unclosed", null);
        Assert.Equal(FileOpOutcome.InvalidArgument, bad.Outcome);
    }

    [Fact]
    public void Search_DoesNotReadThroughASymlinkedDirectory()
    {
        string outside = Path.Combine(_tempBase, "outside2");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "creds.cfg"), "password=hunter2");

        Directory.CreateSymbolicLink(Abs("escape2"), outside);

        var result = _sut.Search(InstanceName, "hunter2", subdir: null);

        Assert.Equal(FileOpOutcome.Ok, result.Outcome);
        Assert.Empty(result.Value!.Hits);
    }

    [Fact]
    public void Find_RefusesASubdirThatEscapesTheJail()
    {
        Assert.Equal(FileOpOutcome.OutOfJail, _sut.Find(InstanceName, "*", "../..").Outcome);
        Assert.Equal(FileOpOutcome.OutOfJail, _sut.Search(InstanceName, "x", "../..").Outcome);
    }
}

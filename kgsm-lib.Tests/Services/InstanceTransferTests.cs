using System.Text.Json;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// The transfer verbs of <see cref="InstanceService"/> and <see cref="BlueprintService"/>: the command each
/// issues, and how each shape the engine prints is read, a refusing verdict on a non-zero exit included.
/// </summary>
public class InstanceTransferTests
{
    private const string ManifestJson = """
        {"format":1,"engine_version":"3.18.0-rc34",
         "instance":{"id":"factorio-7f3a","blueprint":"factorio","runtime":"native","display_name":null,
           "autostart":true,"has_run":true,"library":"default","working_dir":"/opt/instances/factorio/factorio-7f3a",
           "ports":[{"start":34197,"end":34197,"protocol":"udp"},{"start":27015,"end":27015,"protocol":"tcp"}]},
         "blueprint":{"name":"factorio","custom":false,"sha256":"abc"},
         "requirements":["steamcmd"],
         "parts":[{"id":"tree","bytes":4823011328,"files":1873},
                  {"id":"image-1","ref":"ghcr.io/x/y:latest","image_id":"sha256:1","bytes":912345678}]}
        """;

    private const string VerdictJson = """
        {"ok":false,"refusals":[
           {"exit_code":62,"code":"port_conflict","detail":"34197/udp is declared by factorio-1"},
           {"exit_code":61,"code":"requirement_missing","detail":"steamcmd"}],
         "library":"default","blueprint":"factorio"}
        """;

    private readonly Mock<IKgsmCommandExecutor> _executor = new();
    private readonly InstanceService _service;

    public InstanceTransferTests()
    {
        _service = new InstanceService(
            _executor.Object,
            new Mock<ILogSubscriptionService>().Object,
            new Mock<ILifecycleService>().Object,
            new Mock<ILogger<InstanceService>>().Object);
    }

    private static TransferManifest Manifest() =>
        JsonSerializer.Deserialize(ManifestJson, KgsmJsonContext.Default.TransferManifest)!;

    private void StdinReturns(KgsmResult result, Action<string, string[]>? seen = null) =>
        _executor
            .Setup(x => x.ExecuteWithInput(It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<TimeSpan>(), It.IsAny<string>(), It.IsAny<string[]>()))
            .Callback((IReadOnlyDictionary<string, string>? _, TimeSpan _, string stdin, string[] args) => seen?.Invoke(stdin, args))
            .Returns(result);

    // --- Manifest ---

    [Fact]
    public void Manifest_ReadsEveryField()
    {
        TransferManifest manifest = Manifest();

        Assert.Equal(1, manifest.Format);
        Assert.Equal("3.18.0-rc34", manifest.EngineVersion);
        Assert.Equal("factorio-7f3a", manifest.Instance.Id);
        Assert.Null(manifest.Instance.DisplayName);
        Assert.True(manifest.Instance.Autostart);
        Assert.True(manifest.Instance.HasRun);
        Assert.Equal(["udp", "tcp"], manifest.Instance.Ports.Select(p => p.Protocol));
        Assert.False(manifest.Blueprint.Custom);
        Assert.Equal(["steamcmd"], manifest.Requirements);
        Assert.Equal(4823011328L, manifest.Parts[0].Bytes);
        Assert.Equal(1873, manifest.Parts[0].Files);
        Assert.Equal("sha256:1", manifest.Parts[1].ImageId);
    }

    [Fact]
    public void Manifest_RoundTripsInTheEnginesSnakeCase()
    {
        string json = JsonSerializer.Serialize(Manifest(), KgsmJsonContext.Default.TransferManifest);

        Assert.Contains("\"engine_version\":\"3.18.0-rc34\"", json);
        Assert.Contains("\"working_dir\"", json);
        Assert.Contains("\"image_id\":\"sha256:1\"", json);
        Assert.DoesNotContain("\"ref\":null", json);
        Assert.Equal(Manifest(), JsonSerializer.Deserialize(json, KgsmJsonContext.Default.TransferManifest), new ManifestComparer());
    }

    private sealed class ManifestComparer : IEqualityComparer<TransferManifest?>
    {
        public bool Equals(TransferManifest? x, TransferManifest? y) =>
            JsonSerializer.Serialize(x, KgsmJsonContext.Default.TransferManifest)
            == JsonSerializer.Serialize(y, KgsmJsonContext.Default.TransferManifest);

        public int GetHashCode(TransferManifest? obj) => 0;
    }

    // --- Export ---

    [Fact]
    public void ExportManifest_WithLock_IssuesTheVerbAndReadsTheManifest()
    {
        string[]? issued = null;
        _executor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Callback((TimeSpan _, string[] a) => issued = a)
            .Returns(new KgsmResult(0, ManifestJson));

        TransferOutcome<TransferManifest> outcome = _service.ExportManifest("factorio-7f3a", "tok-1");

        Assert.Equal(["instances", "export", "factorio-7f3a", "--manifest", "--lock", "tok-1"], issued);
        Assert.True(outcome.IsSuccess);
        Assert.Equal("factorio-7f3a", outcome.Value!.Instance.Id);
    }

    [Fact]
    public void ExportManifest_WithoutLock_OmitsTheFlag()
    {
        string[]? issued = null;
        _executor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Callback((TimeSpan _, string[] a) => issued = a)
            .Returns(new KgsmResult(0, ManifestJson));

        _service.ExportManifest("factorio-7f3a");

        Assert.Equal(["instances", "export", "factorio-7f3a", "--manifest"], issued);
    }

    [Theory]
    [InlineData(TransferExitCodes.InstanceLocked)]
    [InlineData(TransferExitCodes.NotFound)]
    public void ExportManifest_Refused_ReturnsTheExitCodeAndNoValue(int code)
    {
        _executor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Returns(new KgsmResult(code, "", "refused"));

        TransferOutcome<TransferManifest> outcome = _service.ExportManifest("factorio-7f3a", "tok-1");

        Assert.False(outcome.IsSuccess);
        Assert.Equal(code, outcome.Result.ExitCode);
        Assert.Equal("refused", outcome.Result.Stderr);
    }

    [Fact]
    public void ExportManifest_SuccessWithUnreadableOutput_Throws()
    {
        _executor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Returns(new KgsmResult(0, "not json"));

        Assert.Throws<KgsmException>(() => _service.ExportManifest("factorio-7f3a"));
    }

    [Fact]
    public async Task ExportPart_StreamsIntoTheDestinationWithTheLock()
    {
        string[]? issued = null;
        MemoryStream destination = new();
        _executor
            .Setup(x => x.ExecuteStreamingAsync(It.IsAny<IReadOnlyDictionary<string, string>?>(), null, destination, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyDictionary<string, string>? _, Stream? _, Stream? _, string[] a, CancellationToken _) => issued = a)
            .ReturnsAsync(new StreamResult(0, ""));

        StreamResult result = await _service.ExportPartAsync("factorio-7f3a", "tree", "tok-1", destination);

        Assert.True(result.IsSuccess);
        Assert.Equal(["instances", "export", "factorio-7f3a", "--part", "tree", "--lock", "tok-1"], issued);
    }

    [Fact]
    public void CommitExport_IssuesTheVerbUnderTheUninstallTimeout()
    {
        string[]? issued = null;
        TimeSpan? timeout = null;
        _executor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Callback((TimeSpan t, string[] a) => { timeout = t; issued = a; })
            .Returns(new KgsmResult(0));

        KgsmResult result = _service.CommitExport("factorio-7f3a", "tok-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(["instances", "export", "factorio-7f3a", "--commit", "--lock", "tok-1"], issued);
        Assert.Equal(new KgsmTimeoutOptions().Uninstall, timeout);
    }

    [Fact]
    public void Unlock_WithToken_AndForce_IssueTheirOwnFlags()
    {
        List<string[]> issued = [];
        _executor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Callback((TimeSpan _, string[] a) => issued.Add(a))
            .Returns(new KgsmResult(0));

        _service.Unlock("factorio-7f3a", "tok-1");
        _service.Unlock("factorio-7f3a", force: true);

        Assert.Equal(["instances", "unlock", "factorio-7f3a", "--lock", "tok-1"], issued[0]);
        Assert.Equal(["instances", "unlock", "factorio-7f3a", "--force"], issued[1]);
    }

    [Fact]
    public void Unlock_WithNeither_Throws()
    {
        Assert.Throws<ArgumentException>(() => _service.Unlock("factorio-7f3a"));
    }

    [Fact]
    public void Provenance_RidesOnTheVerbs()
    {
        IReadOnlyDictionary<string, string>? env = null;
        _executor
            .Setup(x => x.Execute(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Callback((IReadOnlyDictionary<string, string> e, TimeSpan _, string[] _) => env = e)
            .Returns(new KgsmResult(0));

        _service.CommitExport("factorio-7f3a", "tok-1", actor: "u1", origin: "api");

        Assert.Equal("u1", env!["KGSM_EVENT_ACTOR"]);
        Assert.Equal("api", env["KGSM_EVENT_ORIGIN"]);
    }

    // --- Import check / begin ---

    [Fact]
    public void ImportCheck_SendsTheManifestOnStdinWithLibraryAndSuffix()
    {
        string? stdin = null;
        string[]? issued = null;
        StdinReturns(new KgsmResult(0, """{"ok":true,"refusals":[],"library":"default","blueprint":"factorio"}"""),
            (s, a) => { stdin = s; issued = a; });

        ImportVerdict verdict = _service.ImportCheck(Manifest(), "ssd", "migrated");

        Assert.True(verdict.Ok);
        Assert.Empty(verdict.Refusals);
        Assert.Equal(["instances", "import", "check", "--json", "--library", "ssd", "--blueprint-suffix", "migrated"], issued);
        Assert.Contains("\"engine_version\":\"3.18.0-rc34\"", stdin);
    }

    [Fact]
    public void ImportCheck_RefusingVerdictOnNonZeroExit_ListsEveryRefusalByCode()
    {
        StdinReturns(new KgsmResult(62, VerdictJson, "port conflict"));

        ImportVerdict verdict = _service.ImportCheck(Manifest());

        Assert.False(verdict.Ok);
        Assert.Equal([ImportRefusalCodes.PortConflict, ImportRefusalCodes.RequirementMissing],
            verdict.Refusals.Select(r => r.Code));
        Assert.Equal([62, 61], verdict.Refusals.Select(r => r.ExitCode));
        Assert.Equal("34197/udp is declared by factorio-1", verdict.Refusals[0].Detail);
        Assert.Equal("default", verdict.Library);
    }

    [Fact]
    public void ImportCheck_NoVerdict_Throws()
    {
        StdinReturns(new KgsmResult(1, "", "boom"));

        Assert.Throws<KgsmException>(() => _service.ImportCheck(Manifest()));
    }

    [Fact]
    public void ImportBegin_Accepted_ReturnsTheImportId()
    {
        StdinReturns(new KgsmResult(0, """{"import_id":"imp-0123456789ab"}"""));

        ImportBeginResult result = _service.ImportBegin(Manifest());

        Assert.True(result.Began);
        Assert.Equal("imp-0123456789ab", result.ImportId);
        Assert.Null(result.Verdict);
    }

    [Fact]
    public void ImportBegin_Refused_ReturnsTheVerdictInsteadOfAnId()
    {
        StdinReturns(new KgsmResult(62, VerdictJson));

        ImportBeginResult result = _service.ImportBegin(Manifest());

        Assert.False(result.Began);
        Assert.Equal(2, result.Verdict!.Refusals.Count);
    }

    [Fact]
    public void ImportBegin_NonZeroWithoutVerdict_Throws()
    {
        StdinReturns(new KgsmResult(1, "", "crashed"));

        Assert.Throws<KgsmException>(() => _service.ImportBegin(Manifest()));
    }

    [Fact]
    public void ImportBegin_NonZeroWithAnImportIdOnly_IsNotAVerdict()
    {
        StdinReturns(new KgsmResult(3, """{"import_id":"imp-0123456789ab"}"""));

        Assert.Throws<KgsmException>(() => _service.ImportBegin(Manifest()));
    }

    // --- Import part / finish / abort / list ---

    [Fact]
    public async Task ImportPart_StreamsTheSourceIntoTheVerb()
    {
        string[]? issued = null;
        MemoryStream source = new([1, 2, 3]);
        _executor
            .Setup(x => x.ExecuteStreamingAsync(It.IsAny<IReadOnlyDictionary<string, string>?>(), source, null, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyDictionary<string, string>? _, Stream? _, Stream? _, string[] a, CancellationToken _) => issued = a)
            .ReturnsAsync(new StreamResult(TransferExitCodes.TransferIntegrity, "image id differs"));

        StreamResult result = await _service.ImportPartAsync("imp-0123456789ab", "image-1", source);

        Assert.Equal(TransferExitCodes.TransferIntegrity, result.ExitCode);
        Assert.Equal(["instances", "import", "part", "imp-0123456789ab", "image-1"], issued);
    }

    [Fact]
    public void ImportFinish_ReadsTheInstance()
    {
        string[]? issued = null;
        _executor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Callback((TimeSpan _, string[] a) => issued = a)
            .Returns(new KgsmResult(0, """{"instance":"factorio-91ab","blueprint":"factorio","library":"default"}"""));

        TransferOutcome<ImportedInstance> outcome = _service.ImportFinish("imp-0123456789ab");

        Assert.Equal(["instances", "import", "finish", "imp-0123456789ab", "--json"], issued);
        Assert.Equal("factorio-91ab", outcome.Value!.Instance);
        Assert.Equal("factorio", outcome.Value.Blueprint);
        Assert.Equal("default", outcome.Value.Library);
    }

    [Fact]
    public void ImportFinish_IntegrityFailure_ReturnsTheCode()
    {
        _executor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Returns(new KgsmResult(TransferExitCodes.TransferIntegrity, "", "file count differs"));

        TransferOutcome<ImportedInstance> outcome = _service.ImportFinish("imp-0123456789ab");

        Assert.False(outcome.IsSuccess);
        Assert.Equal(TransferExitCodes.TransferIntegrity, outcome.Result.ExitCode);
    }

    [Fact]
    public void ImportAbort_IssuesTheVerb()
    {
        string[]? issued = null;
        _executor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Callback((TimeSpan _, string[] a) => issued = a)
            .Returns(new KgsmResult(0));

        Assert.True(_service.ImportAbort("imp-0123456789ab").IsSuccess);
        Assert.Equal(["instances", "import", "abort", "imp-0123456789ab"], issued);
    }

    [Fact]
    public void ImportList_ReadsRowsAndTheEmptyList()
    {
        _executor
            .SetupSequence(x => x.Execute(It.IsAny<string[]>()))
            .Returns(new KgsmResult(0, """[{"import_id":"imp-0123456789ab","library":"default","instance":"factorio-7f3a","blueprint":"factorio","began":"2026-10-29T10:00:00Z"}]"""))
            .Returns(new KgsmResult(0, "[]"));

        List<StagedImport> rows = _service.ImportList().Value!;
        List<StagedImport> none = _service.ImportList().Value!;

        StagedImport row = Assert.Single(rows);
        Assert.Equal("imp-0123456789ab", row.ImportId);
        Assert.Equal("factorio-7f3a", row.Instance);
        Assert.Equal("2026-10-29T10:00:00Z", row.Began);
        Assert.Empty(none);
    }

    // --- Requirements ---

    [Fact]
    public void BlueprintRequirements_AbsentRequirementOnNonZeroExit_IsStillRead()
    {
        Mock<IKgsmCommandExecutor> executor = new();
        executor
            .Setup(x => x.Probe(It.Is<string[]>(a => a.SequenceEqual(new[] { "blueprints", "requirements", "factorio", "--json" }))))
            .Returns(new KgsmResult(TransferExitCodes.RequirementMissing,
                """{"blueprint":"factorio","requirements":[{"id":"steamcmd","present":true},{"id":"container-runtime","present":false}]}"""));
        BlueprintService blueprints = new(executor.Object, new Mock<ILogger<BlueprintService>>().Object);

        BlueprintRequirements? result = blueprints.BlueprintRequirements("factorio");

        Assert.NotNull(result);
        Assert.False(result.AllPresent);
        Assert.Equal(["steamcmd", "container-runtime"], result.Requirements.Select(r => r.Id));
        Assert.Equal([true, false], result.Requirements.Select(r => r.Present));
    }

    [Fact]
    public void BlueprintRequirements_NoAnswer_IsNullNotPresent()
    {
        Mock<IKgsmCommandExecutor> executor = new();
        executor.Setup(x => x.Probe(It.IsAny<string[]>())).Returns(new KgsmResult(25, "", "unknown blueprint"));
        BlueprintService blueprints = new(executor.Object, new Mock<ILogger<BlueprintService>>().Object);

        Assert.Null(blueprints.BlueprintRequirements("nope"));
    }

    // --- Watchdog refusal ---

    [Fact]
    public void WatchdogActionResult_CarriesTheRefusalKind()
    {
        WatchdogActionResult result = JsonSerializer.Deserialize(
            """{"instance":"factorio-7f3a","ok":false,"message":"locked for transfer","refusal":"instance_locked"}""",
            KgsmJsonContext.Default.WatchdogActionResult)!;

        Assert.Equal(WatchdogRefusals.InstanceLocked, result.Refusal);
        Assert.NotEqual(WatchdogRefusals.NoRoom, result.Refusal);
    }

    [Fact]
    public void WatchdogActionResult_WithoutRefusal_IsNull()
    {
        WatchdogActionResult result = JsonSerializer.Deserialize(
            """{"instance":"a","ok":true,"message":"started"}""", KgsmJsonContext.Default.WatchdogActionResult)!;

        Assert.Null(result.Refusal);
    }
}

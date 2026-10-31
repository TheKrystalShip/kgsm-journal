using System.Text.Json;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Tests for the InstanceService class.
///
/// InstanceService is a facade over two collaborators: <see cref="IKgsmCommandExecutor"/>
/// (for direct <c>instances</c> commands and JSON reads) and <see cref="ILifecycleService"/>
/// (for operational verbs — start/stop/restart/status/is-active/logs). Tests here assert
/// the facade's own responsibilities: input validation, the exact command it issues, and
/// that it forwards lifecycle calls. The behavioral contract of the lifecycle verbs
/// (failure channels, log splitting) is covered in <see cref="LifecycleServiceTests"/>.
///
/// Failure-channel convention asserted below: methods returning <see cref="KgsmResult"/>
/// encode failure in the result (<c>IsSuccess == false</c>) and never throw on a non-zero
/// exit; methods returning a nullable type return null on failure.
/// </summary>
public class InstanceServiceTests
{
    private readonly Mock<IKgsmCommandExecutor> _mockCommandExecutor;
    private readonly Mock<ILogSubscriptionService> _mockLogSubscriptionService;
    private readonly Mock<ILifecycleService> _mockLifecycleService;
    private readonly Mock<ILogger<InstanceService>> _mockLogger;
    private readonly InstanceService _instanceService;

    private const string Instance = "my-server";

    public InstanceServiceTests()
    {
        _mockCommandExecutor = new Mock<IKgsmCommandExecutor>();
        _mockLogSubscriptionService = new Mock<ILogSubscriptionService>();
        _mockLifecycleService = new Mock<ILifecycleService>();
        _mockLogger = new Mock<ILogger<InstanceService>>();
        _instanceService = new InstanceService(
            _mockCommandExecutor.Object,
            _mockLogSubscriptionService.Object,
            _mockLifecycleService.Object,
            _mockLogger.Object);
    }

    private static bool ArgsAre(string[] actual, params string[] expected)
        => actual.SequenceEqual(expected);

    // --- Constructor guards ---

    [Fact]
    public void Constructor_NullCommandExecutor_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new InstanceService(
            null!, _mockLogSubscriptionService.Object, _mockLifecycleService.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_NullLogSubscriptionService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new InstanceService(
            _mockCommandExecutor.Object, null!, _mockLifecycleService.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_NullLifecycleService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new InstanceService(
            _mockCommandExecutor.Object, _mockLogSubscriptionService.Object, null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new InstanceService(
            _mockCommandExecutor.Object, _mockLogSubscriptionService.Object, _mockLifecycleService.Object, null!));
    }

    // --- GetAll : ExecuteForJson<Dictionary<string, Instance>>("instances list --detailed --json") ---

    [Fact]
    public void GetAll_SuccessfulExecution_ReturnsInstances()
    {
        var expected = new Dictionary<string, Instance>
        {
            [Instance] = new Instance { Name = Instance }
        };
        _mockCommandExecutor
            .Setup(x => x.ExecuteForJson<Dictionary<string, Instance>>(
                It.Is<string[]>(a => ArgsAre(a, "instances", "list", "--detailed", "--json")),
                It.IsAny<Action<JsonSerializerOptions>?>(),
                It.IsAny<Dictionary<string, Instance>?>()))
            .Returns(expected);

        Dictionary<string, Instance> result = _instanceService.GetAll();

        Assert.Single(result);
        Assert.Equal(Instance, result[Instance].Name);
    }

    [Fact]
    public void GetAll_CommandReturnsNull_ReturnsEmptyDictionary()
    {
        _mockCommandExecutor
            .Setup(x => x.ExecuteForJson<Dictionary<string, Instance>>(
                It.IsAny<string[]>(),
                It.IsAny<Action<JsonSerializerOptions>?>(),
                It.IsAny<Dictionary<string, Instance>?>()))
            .Returns((Dictionary<string, Instance>?)null);

        Dictionary<string, Instance> result = _instanceService.GetAll();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // --- GetInstanceInfo : ExecuteForJson<Instance>("instances info <name> --json") ---

    [Fact]
    public void GetInstanceInfo_NullInstanceName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.GetInstanceInfo(null!));
    }

    [Fact]
    public void GetInstanceInfo_SuccessfulExecution_ReturnsInstance()
    {
        _mockCommandExecutor
            .Setup(x => x.ExecuteForJson<Instance>(
                It.Is<string[]>(a => ArgsAre(a, "instances", "info", Instance, "--json")),
                It.IsAny<Action<JsonSerializerOptions>?>(),
                It.IsAny<Instance?>()))
            .Returns(new Instance { Name = Instance });

        Instance? result = _instanceService.GetInstanceInfo(Instance);

        Assert.NotNull(result);
        Assert.Equal(Instance, result!.Name);
    }

    [Fact]
    public void GetInstanceInfo_ExecutionFails_ReturnsNull()
    {
        // Instance? return type: failure is signalled by null, not an exception.
        _mockCommandExecutor
            .Setup(x => x.ExecuteForJson<Instance>(
                It.IsAny<string[]>(),
                It.IsAny<Action<JsonSerializerOptions>?>(),
                It.IsAny<Instance?>()))
            .Returns((Instance?)null);

        Assert.Null(_instanceService.GetInstanceInfo(Instance));
    }

    // --- GetInstanceStatus : ExecuteForJson<InstanceRuntimeStatus>("instances status <name> --json") ---

    [Fact]
    public void GetInstanceStatus_NullInstanceName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.GetInstanceStatus(null!));
    }

    [Fact]
    public void GetInstanceStatus_SuccessfulExecution_ReturnsStatus()
    {
        _mockCommandExecutor
            .Setup(x => x.ExecuteForJson<InstanceRuntimeStatus>(
                It.Is<string[]>(a => ArgsAre(a, "instances", "status", Instance, "--json")),
                It.IsAny<Action<JsonSerializerOptions>?>(),
                It.IsAny<InstanceRuntimeStatus?>()))
            .Returns(new InstanceRuntimeStatus { InstanceName = Instance, Status = true });

        InstanceRuntimeStatus? result = _instanceService.GetInstanceStatus(Instance);

        Assert.NotNull(result);
        Assert.Equal(Instance, result!.InstanceName);
        Assert.True(result.Status);
    }

    [Fact]
    public void GetInstanceStatus_ExecutionFails_ReturnsNull()
    {
        // InstanceRuntimeStatus? return type: failure is null, not a KgsmException.
        _mockCommandExecutor
            .Setup(x => x.ExecuteForJson<InstanceRuntimeStatus>(
                It.IsAny<string[]>(),
                It.IsAny<Action<JsonSerializerOptions>?>(),
                It.IsAny<InstanceRuntimeStatus?>()))
            .Returns((InstanceRuntimeStatus?)null);

        Assert.Null(_instanceService.GetInstanceStatus(Instance));
    }

    // --- GetAllStatuses (bulk fleet read) : uses the injected command executor directly ---

    [Fact]
    public void GetAllStatuses_Default_RequestsNonFastBulkStatus()
    {
        var expected = new Dictionary<string, Reading<InstanceRuntimeStatus>>
        {
            ["7dtd"] = Reading<InstanceRuntimeStatus>.Measured(new() { InstanceName = "7dtd", Status = true })
        };
        _mockCommandExecutor
            .Setup(x => x.ExecuteForJson<Dictionary<string, Reading<InstanceRuntimeStatus>>>(
                It.Is<string[]>(a => ArgsAre(a, "instances", "list", "--status", "--json")),
                It.IsAny<Action<JsonSerializerOptions>>(),
                It.IsAny<Dictionary<string, Reading<InstanceRuntimeStatus>>>()))
            .Returns(expected);

        var result = _instanceService.GetAllStatuses();

        Assert.Same(expected, result);
        Assert.Equal(ReadingState.Measured, result["7dtd"].State);
        Assert.True(result["7dtd"].Value!.Status);
    }

    [Fact]
    public void GetAllStatuses_Fast_AppendsFastFlag()
    {
        _mockCommandExecutor
            .Setup(x => x.ExecuteForJson<Dictionary<string, Reading<InstanceRuntimeStatus>>>(
                It.Is<string[]>(a => ArgsAre(a, "instances", "list", "--status", "--json", "--fast")),
                It.IsAny<Action<JsonSerializerOptions>>(),
                It.IsAny<Dictionary<string, Reading<InstanceRuntimeStatus>>>()))
            .Returns(new Dictionary<string, Reading<InstanceRuntimeStatus>>());

        var result = _instanceService.GetAllStatuses(fast: true);

        Assert.NotNull(result);
        _mockCommandExecutor.Verify(x => x.ExecuteForJson<Dictionary<string, Reading<InstanceRuntimeStatus>>>(
            It.Is<string[]>(a => a.Contains("--fast")),
            It.IsAny<Action<JsonSerializerOptions>>(),
            It.IsAny<Dictionary<string, Reading<InstanceRuntimeStatus>>>()), Times.Once);
    }

    [Fact]
    public void GetAllStatuses_CommandReturnsNull_ReturnsEmptyDictionary()
    {
        _mockCommandExecutor
            .Setup(x => x.ExecuteForJson<Dictionary<string, Reading<InstanceRuntimeStatus>>>(
                It.IsAny<string[]>(),
                It.IsAny<Action<JsonSerializerOptions>>(),
                It.IsAny<Dictionary<string, Reading<InstanceRuntimeStatus>>>()))
            .Returns((Dictionary<string, Reading<InstanceRuntimeStatus>>?)null);

        var result = _instanceService.GetAllStatuses();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // --- CheckUpdate : Execute(timeout, "instances", "check-update", name, ["--emit"]) ---
    // Carries a timeout because it reaches the game's upstream, and the tight default meant for
    // local reads would kill a registry probe the engine was about to answer.

    [Fact]
    public void CheckUpdate_NullInstanceName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.CheckUpdate(null!));
    }

    [Fact]
    public void CheckUpdate_ByDefault_DoesNotEmit()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(
                It.IsAny<TimeSpan>(),
                It.Is<string[]>(a => ArgsAre(a, "instances", "check-update", Instance))))
            .Returns(new KgsmResult(new ProcessResult(0, "Already up to date", string.Empty)));

        KgsmResult result = _instanceService.CheckUpdate(Instance);

        Assert.True(result.IsSuccess);
        // --emit is what writes the record that makes a repeated sweep silent. A caller that only
        // wants an answer must not consume an announcement, so it is never passed by default.
        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => a.Contains("--emit"))), Times.Never);
    }

    [Fact]
    public void CheckUpdate_WithEmit_PassesTheFlag()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(
                It.IsAny<TimeSpan>(),
                It.Is<string[]>(a => ArgsAre(a, "instances", "check-update", Instance, "--emit"))))
            .Returns(new KgsmResult(new ProcessResult(0, string.Empty, string.Empty)));

        KgsmResult result = _instanceService.CheckUpdate(Instance, emit: true);

        Assert.True(result.IsSuccess);
        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "instances", "check-update", Instance, "--emit"))),
            Times.Once);
    }

    [Fact]
    public void CheckUpdate_WithProvenance_StampsEnvOnTheEmittingCall()
    {
        SetupEnvTimeout();

        _instanceService.CheckUpdate(Instance, emit: true, actor: "system:scheduler", origin: "system");

        // --emit is the only form that produces an event, so this is the one that has an actor to get
        // wrong: without the stamp the engine attributes the announcement to whichever OS user the
        // sweep runs as, and an unattended check reads as a person having asked for it.
        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e =>
                e["KGSM_EVENT_ACTOR"] == "system:scheduler" && e["KGSM_EVENT_ORIGIN"] == "system"),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "instances", "check-update", Instance, "--emit"))), Times.Once);
    }

    [Fact]
    public void CheckUpdate_WithoutProvenance_TakesThePlainTimeoutPath_NoEnvOverload()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Returns(new KgsmResult(new ProcessResult(0, string.Empty, string.Empty)));

        _instanceService.CheckUpdate(Instance);

        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<string[]>()), Times.Never);
    }

    // --- Install : Execute(timeout, "install", blueprint, [--library, --version, --name]) ---

    [Fact]
    public void Install_NullBlueprintName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.Install(null!));
    }

    [Fact]
    public void Install_ValidBlueprint_IssuesInstallCommand()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(
                It.IsAny<TimeSpan>(),
                It.Is<string[]>(a => ArgsAre(a, "install", "valheim"))))
            .Returns(new KgsmResult(new ProcessResult(0, "installed", string.Empty)));

        KgsmResult result = _instanceService.Install("valheim");

        Assert.True(result.IsSuccess);
        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "install", "valheim"))), Times.Once);
    }

    [Fact]
    public void Install_WithAllParameters_PassesEveryFlag()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(
                It.IsAny<TimeSpan>(),
                It.Is<string[]>(a => ArgsAre(a,
                    "install", "valheim",
                    "--library", "ssd",
                    "--version", "1.0.0",
                    "--name", "my-server"))))
            .Returns(new KgsmResult(new ProcessResult(0, "installed", string.Empty)));

        KgsmResult result = _instanceService.Install("valheim", "ssd", "1.0.0", "my-server");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Install_WithId_PassesIdAndLabelAsSeparateFlags()
    {
        // The two are not interchangeable: --id names the identifier every path and downstream store
        // keys on, --name is the label a person reads and changes later.
        _mockCommandExecutor
            .Setup(x => x.Execute(
                It.IsAny<TimeSpan>(),
                It.Is<string[]>(a => ArgsAre(a,
                    "install", "valheim",
                    "--name", "Ana's Valheim",
                    "--id", "valheim-prod"))))
            .Returns(new KgsmResult(new ProcessResult(0, "installed", string.Empty)));

        KgsmResult result = _instanceService.Install("valheim", displayName: "Ana's Valheim", id: "valheim-prod");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Install_WithoutId_LetsTheEngineMintOne()
    {
        // No --id at all rather than an empty one: the engine generating the id is the ordinary path,
        // and an empty flag value would be a malformed id it has to refuse.
        _mockCommandExecutor
            .Setup(x => x.Execute(
                It.IsAny<TimeSpan>(),
                It.Is<string[]>(a => ArgsAre(a, "install", "valheim", "--name", "Ana's Valheim"))))
            .Returns(new KgsmResult(new ProcessResult(0, "installed", string.Empty)));

        KgsmResult result = _instanceService.Install("valheim", displayName: "Ana's Valheim");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Install_WithoutLibrary_PassesNoPlacementFlag()
    {
        // Placement is the engine's to resolve (default_library, else the sole registered
        // library). Passing a flag here would decide it in the wrong place.
        _mockCommandExecutor
            .Setup(x => x.Execute(
                It.IsAny<TimeSpan>(),
                It.Is<string[]>(a => ArgsAre(a, "install", "valheim", "--version", "1.0.0"))))
            .Returns(new KgsmResult(new ProcessResult(0, "installed", string.Empty)));

        KgsmResult result = _instanceService.Install("valheim", version: "1.0.0");

        Assert.True(result.IsSuccess);
        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => !a.Contains("--library"))), Times.Once);
    }

    // --- Uninstall : Execute(timeout, "uninstall", name, "--force") ---
    // Always --force: a programmatic uninstall is already confirmed at the calling surface, and kgsm's
    // uninstall is interactive-by-default (a no-TTY call without --force is a non-zero EC_CANCELLED no-op).

    [Fact]
    public void Uninstall_NullInstanceName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.Uninstall(null!));
    }

    [Fact]
    public void Uninstall_ValidInstance_IssuesForcedUninstallCommand()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(
                It.IsAny<TimeSpan>(),
                It.Is<string[]>(a => ArgsAre(a, "uninstall", Instance, "--force"))))
            .Returns(new KgsmResult(new ProcessResult(0, "uninstalled", string.Empty)));

        KgsmResult result = _instanceService.Uninstall(Instance);

        Assert.True(result.IsSuccess);
        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "uninstall", Instance, "--force"))), Times.Once);
    }

    // --- Move : Execute("instances", "move", name, "--library", target) ---

    [Fact]
    public void Move_NullArguments_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.Move(null!, "ssd"));
        Assert.Throws<ArgumentNullException>(() => _instanceService.Move(Instance, null!));
    }

    [Fact]
    public void Move_IssuesTheMoveCommandUnderTheMoveTimeout()
    {
        var timeouts = new KgsmTimeoutOptions();
        var service = new InstanceService(
            _mockCommandExecutor.Object,
            _mockLogSubscriptionService.Object,
            _mockLifecycleService.Object,
            _mockLogger.Object,
            new KgsmOptions { KgsmPath = "/opt/kgsm/kgsm.sh", Timeouts = timeouts });

        _mockCommandExecutor
            .Setup(x => x.Execute(
                timeouts.Move,
                It.Is<string[]>(a => ArgsAre(a, "instances", "move", Instance, "--library", "ssd"))))
            .Returns(new KgsmResult(new ProcessResult(0, "moved", string.Empty)));

        KgsmResult result = service.Move(Instance, "ssd");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Move_WithSkipSpaceCheck_PassesTheFlag()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(
                It.IsAny<TimeSpan>(),
                It.Is<string[]>(a => ArgsAre(
                    a, "instances", "move", Instance, "--library", "ssd", "--skip-space-check"))))
            .Returns(new KgsmResult(new ProcessResult(0, "moved", string.Empty)));

        KgsmResult result = _instanceService.Move(Instance, "ssd", skipSpaceCheck: true);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Move_RefusedByTheEngine_ReportsTheRefusalRatherThanThrowing()
    {
        // 57 is EC_INSTANCE_RUNNING. The refusal text names what blocked it, and that text is
        // what a surface shows — so it has to come back rather than become an exception message.
        _mockCommandExecutor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Returns(new KgsmResult(new ProcessResult(57, string.Empty, "Instance 'my-server' is running")));

        KgsmResult result = _instanceService.Move(Instance, "ssd");

        Assert.False(result.IsSuccess);
        Assert.Contains("is running", result.Stderr);
    }

    // --- GetInfo : Execute("instances", "info", name) (raw, not JSON) ---

    [Fact]
    public void GetInfo_NullInstanceName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.GetInfo(null!));
    }

    [Fact]
    public void GetInfo_ValidInstance_IssuesInfoCommand()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "info", Instance))))
            .Returns(new KgsmResult(new ProcessResult(0, "info...", string.Empty)));

        KgsmResult result = _instanceService.GetInfo(Instance);

        Assert.True(result.IsSuccess);
    }

    // --- Delegation to ILifecycleService (thin forwarding; behavior lives in LifecycleServiceTests) ---

    [Fact]
    public void GetStatus_NullInstanceName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.GetStatus(null!));
    }

    [Fact]
    public void GetStatus_ValidInstance_ForwardsToLifecycle()
    {
        var expected = new KgsmResult(new ProcessResult(0, "status", string.Empty));
        _mockLifecycleService.Setup(x => x.GetStatus(Instance)).Returns(expected);

        KgsmResult result = _instanceService.GetStatus(Instance);

        Assert.Same(expected, result);
        _mockLifecycleService.Verify(x => x.GetStatus(Instance), Times.Once);
    }

    [Fact]
    public void IsActive_NullInstanceName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.IsActive(null!));
    }

    [Fact]
    public void IsActive_ValidInstance_ForwardsLifecycleResult()
    {
        _mockLifecycleService.Setup(x => x.IsActive(Instance)).Returns(true);

        Assert.True(_instanceService.IsActive(Instance));
        _mockLifecycleService.Verify(x => x.IsActive(Instance), Times.Once);
    }

    [Fact]
    public void GetLogs_NullInstanceName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.GetLogs(null!));
    }

    [Fact]
    public void GetLogs_ValidInstance_ForwardsToLifecycle()
    {
        ICollection<string> expected = new[] { "line 1", "line 2" };
        _mockLifecycleService.Setup(x => x.GetLogs(Instance, It.IsAny<int>())).Returns(expected);

        ICollection<string> result = _instanceService.GetLogs(Instance);

        Assert.Same(expected, result);
        _mockLifecycleService.Verify(x => x.GetLogs(Instance, It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task GetLogsAsync_NullInstanceName_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _instanceService.GetLogsAsync(null!));
    }

    [Fact]
    public async Task GetLogsAsync_ValidInstance_ForwardsToLifecycle()
    {
        ICollection<string> expected = new[] { "line 1" };
        _mockLifecycleService
            .Setup(x => x.GetLogsAsync(Instance, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        ICollection<string> result = await _instanceService.GetLogsAsync(Instance);

        Assert.Same(expected, result);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("restart")]
    public void LifecycleVerb_NullInstanceName_ThrowsArgumentNullException(string verb)
    {
        Func<KgsmResult> act = verb switch
        {
            "start" => () => _instanceService.Start(null!),
            "stop" => () => _instanceService.Stop(null!),
            _ => () => _instanceService.Restart(null!),
        };

        Assert.Throws<ArgumentNullException>(() => act());
    }

    [Fact]
    public void Start_ValidInstance_ForwardsToLifecycle()
    {
        var expected = new KgsmResult(new ProcessResult(0, "started", string.Empty));
        _mockLifecycleService.Setup(x => x.Start(Instance, It.IsAny<string?>(), It.IsAny<string?>())).Returns(expected);

        Assert.Same(expected, _instanceService.Start(Instance));
        _mockLifecycleService.Verify(x => x.Start(Instance, It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void Stop_ValidInstance_ForwardsToLifecycle()
    {
        var expected = new KgsmResult(new ProcessResult(0, "stopped", string.Empty));
        _mockLifecycleService.Setup(x => x.Stop(Instance, It.IsAny<string?>(), It.IsAny<string?>())).Returns(expected);

        Assert.Same(expected, _instanceService.Stop(Instance));
    }

    [Fact]
    public void Restart_ValidInstance_ForwardsToLifecycle()
    {
        var expected = new KgsmResult(new ProcessResult(0, "restarted", string.Empty));
        _mockLifecycleService.Setup(x => x.Restart(Instance, It.IsAny<string?>(), It.IsAny<string?>())).Returns(expected);

        Assert.Same(expected, _instanceService.Restart(Instance));
    }

    // --- GenerateId : Execute("instances", "generate-id", blueprint, [--id, id]) ---

    [Fact]
    public void GenerateId_NullBlueprintName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.GenerateId(null!));
    }

    [Fact]
    public void GenerateId_WhitespaceBlueprintName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _instanceService.GenerateId("   "));
    }

    [Fact]
    public void GenerateId_SuccessfulExecution_ReturnsSuccessResult()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "generate-id", "valheim"))))
            .Returns(new KgsmResult(new ProcessResult(0, "valheim-abc", string.Empty)));

        KgsmResult result = _instanceService.GenerateId("valheim");

        Assert.True(result.IsSuccess);
        Assert.Equal("valheim-abc", result.Stdout);
    }

    [Fact]
    public void GenerateId_WithProposedId_PassesIdFlag()
    {
        // --id, not --name: generate-id answers about identifiers, and --name on this verb is the
        // engine's label flag, which it would have nothing to check.
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a =>
                ArgsAre(a, "instances", "generate-id", "valheim", "--id", "my-valheim"))))
            .Returns(new KgsmResult(new ProcessResult(0, "my-valheim", string.Empty)));

        KgsmResult result = _instanceService.GenerateId("valheim", "my-valheim");

        Assert.True(result.IsSuccess);
        Assert.Equal("my-valheim", result.Stdout);
    }

    [Fact]
    public void GenerateId_ExecutionFails_ReturnsFailureResult()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "generate-id", "unknown-blueprint"))))
            .Returns(new KgsmResult(new ProcessResult(1, string.Empty, "Blueprint not found")));

        KgsmResult result = _instanceService.GenerateId("unknown-blueprint");

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
    }

    // --- Save : Execute("instances", "save", name) ---

    [Fact]
    public void Save_NullInstanceName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.Save(null!));
    }

    [Fact]
    public void Save_SuccessfulExecution_ReturnsSuccessResult()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "save", "my-instance"))))
            .Returns(new KgsmResult(new ProcessResult(0, "Saved", string.Empty)));

        KgsmResult result = _instanceService.Save("my-instance");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Save_ExecutionFails_ReturnsFailureResult()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "save", "my-instance"))))
            .Returns(new KgsmResult(new ProcessResult(1, string.Empty, "Instance not running")));

        KgsmResult result = _instanceService.Save("my-instance");

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
    }

    // --- Announce : Execute("instances", "announce", name, message) ---

    [Fact]
    public void Announce_NullInstanceName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.Announce(null!, "back in 5"));
    }

    [Fact]
    public void Announce_NullMessage_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.Announce("my-instance", null!));
    }

    [Theory]
    [InlineData("first\nsecond")]
    [InlineData("first\rsecond")]
    [InlineData("trailing\n")]
    public void Announce_MessageWithALineBreak_ThrowsArgumentException(string message)
    {
        // A console reads one command per line, so a second line would deliver a
        // command nobody issued. Failing at the call site means no malformed argument
        // is spawned at all.
        Assert.Throws<ArgumentException>(() => _instanceService.Announce("my-instance", message));
    }

    [Fact]
    public void Announce_MessageWithProsePunctuation_IsPassedThroughUntouched()
    {
        // The engine's free-form input path rejects ! ? ( ), which is most of ordinary
        // prose. An announcement is prose, and the only character it may not carry is a
        // line break — everything else reaches the console verbatim.
        const string message = "Restarting in 5 minutes! (save first?)";
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "announce", "my-instance", message))))
            .Returns(Ok);

        KgsmResult result = _instanceService.Announce("my-instance", message);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Announce_ExecutionFails_ReturnsFailureResult()
    {
        // A game declaring no template, or an instance that is not running, is refused
        // by the engine — surfaced as a failed result, never as a silent success.
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "announce", "my-instance", "back in 5"))))
            .Returns(new KgsmResult(new ProcessResult(1, string.Empty, "does not support announcements")));

        KgsmResult result = _instanceService.Announce("my-instance", "back in 5");

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void Announce_WithProvenance_UsesTheDefaultTimeoutEnvOverload()
    {
        // announce is quick → the env overload WITHOUT an explicit timeout, so the
        // instance_announcement_sent event kgsm emits is attributable.
        _mockCommandExecutor
            .Setup(x => x.Execute(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string[]>()))
            .Returns(Ok);

        _instanceService.Announce("my-instance", "back in 5", actor: "system:scheduler", origin: "system");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e =>
                e["KGSM_EVENT_ACTOR"] == "system:scheduler" && e["KGSM_EVENT_ORIGIN"] == "system"),
            It.Is<string[]>(a => ArgsAre(a, "instances", "announce", "my-instance", "back in 5"))), Times.Once);
    }

    [Fact]
    public void Announce_NoProvenance_TakesThePlainNoEnvPath()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "announce", "my-instance", "back in 5"))))
            .Returns(Ok);

        _instanceService.Announce("my-instance", "back in 5");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<string[]>(a => ArgsAre(a, "instances", "announce", "my-instance", "back in 5"))), Times.Once);
    }

    // --- SendInput : Execute("instances", "input", name, command) ---

    [Fact]
    public void SendInput_NullInstanceName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.SendInput(null!, "say hello"));
    }

    [Fact]
    public void SendInput_NullCommand_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.SendInput("my-instance", null!));
    }

    [Fact]
    public void SendInput_SuccessfulExecution_ReturnsSuccessResult()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "input", "my-instance", "say hello"))))
            .Returns(new KgsmResult(new ProcessResult(0, "[INFO] hello", string.Empty)));

        KgsmResult result = _instanceService.SendInput("my-instance", "say hello");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void SendInput_ExecutionFails_ReturnsFailureResult()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "input", "my-instance", "say hello"))))
            .Returns(new KgsmResult(new ProcessResult(1, string.Empty, "Instance not running")));

        KgsmResult result = _instanceService.SendInput("my-instance", "say hello");

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void SendInput_WithProvenance_UsesTheDefaultTimeoutEnvOverload()
    {
        // input is quick → the env overload WITHOUT an explicit timeout, like config-set, so the
        // instance_input_sent event kgsm emits is attributable (actor/origin via KGSM_EVENT_*).
        _mockCommandExecutor
            .Setup(x => x.Execute(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string[]>()))
            .Returns(Ok);

        _instanceService.SendInput("my-instance", "say hello", actor: "discord:haru", origin: "ui");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e =>
                e["KGSM_EVENT_ACTOR"] == "discord:haru" && e["KGSM_EVENT_ORIGIN"] == "ui"),
            It.Is<string[]>(a => ArgsAre(a, "instances", "input", "my-instance", "say hello"))), Times.Once);
    }

    [Fact]
    public void SendInput_NoProvenance_TakesThePlainNoEnvPath()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "input", "my-instance", "say hello"))))
            .Returns(Ok);

        _instanceService.SendInput("my-instance", "say hello");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string[]>()), Times.Never);
    }

    // --- Kick/Ban/Unban : Execute("instances", <verb>, name, target) ---

    [Theory]
    [InlineData("kick")]
    [InlineData("ban")]
    [InlineData("unban")]
    public void Moderation_NullInstanceName_ThrowsArgumentException(string verb)
    {
        Assert.Throws<ArgumentNullException>(() => Moderate(verb, null!, "1.2.3.4"));
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("ban")]
    [InlineData("unban")]
    public void Moderation_NullTarget_ThrowsArgumentException(string verb)
    {
        Assert.Throws<ArgumentNullException>(() => Moderate(verb, "my-instance", null!));
    }

    [Theory]
    [InlineData("kick", "1.2.3.4\nsay pwned")]
    [InlineData("ban", "1.2.3.4\r\nsay pwned")]
    [InlineData("unban", "bad\rtarget")]
    public void Moderation_TargetWithLineBreak_ThrowsArgumentException(string verb, string target)
    {
        // The console reads one command per line, so a line break would smuggle a second
        // command in behind the moderation one. Rejected before a process is spawned.
        Assert.Throws<ArgumentException>(() => Moderate(verb, "my-instance", target));

        _mockCommandExecutor.Verify(x => x.Execute(It.IsAny<string[]>()), Times.Never);
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("ban")]
    [InlineData("unban")]
    public void Moderation_PassesTheTargetThroughUntouched(string verb)
    {
        // The engine owns substitution into the blueprint's template; the lib must not
        // build the console command itself, or there would be two answers that can differ.
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", verb, "my-instance", "95.19.50.122"))))
            .Returns(Ok);

        KgsmResult result = Moderate(verb, "my-instance", "95.19.50.122");

        Assert.True(result.IsSuccess);
        _mockCommandExecutor.Verify(
            x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", verb, "my-instance", "95.19.50.122"))),
            Times.Once);
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("ban")]
    [InlineData("unban")]
    public void Moderation_ExecutionFails_ReturnsFailureResult(string verb)
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", verb, "my-instance", "1.2.3.4"))))
            .Returns(new KgsmResult(new ProcessResult(1, string.Empty, "the server is not running")));

        KgsmResult result = Moderate(verb, "my-instance", "1.2.3.4");

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("ban")]
    [InlineData("unban")]
    public void Moderation_WithProvenance_UsesTheEnvOverload(string verb)
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string[]>()))
            .Returns(Ok);

        Moderate(verb, "my-instance", "1.2.3.4", actor: "discord:haru", origin: "ui");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e =>
                e["KGSM_EVENT_ACTOR"] == "discord:haru" && e["KGSM_EVENT_ORIGIN"] == "ui"),
            It.Is<string[]>(a => ArgsAre(a, "instances", verb, "my-instance", "1.2.3.4"))), Times.Once);
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("ban")]
    [InlineData("unban")]
    public void Moderation_NoProvenance_TakesThePlainNoEnvPath(string verb)
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", verb, "my-instance", "1.2.3.4"))))
            .Returns(Ok);

        Moderate(verb, "my-instance", "1.2.3.4");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string[]>()), Times.Never);
    }

    private KgsmResult Moderate(
        string verb, string instanceName, string target, string? actor = null, string? origin = null) => verb switch
        {
            "kick" => _instanceService.Kick(instanceName, target, actor, origin),
            "ban" => _instanceService.Ban(instanceName, target, actor, origin),
            "unban" => _instanceService.Unban(instanceName, target, actor, origin),
            _ => throw new ArgumentOutOfRangeException(nameof(verb))
        };

    // --- FindConfigPath : Execute("instances", "find", name) ---

    [Fact]
    public void FindConfigPath_NullInstanceName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.FindConfigPath(null!));
    }

    [Fact]
    public void FindConfigPath_SuccessfulExecution_ReturnsSuccessResult()
    {
        const string expectedPath = "/home/kgsm/instances/my-instance/my-instance.ini";
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "find", "my-instance"))))
            .Returns(new KgsmResult(new ProcessResult(0, expectedPath, string.Empty)));

        KgsmResult result = _instanceService.FindConfigPath("my-instance");

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedPath, result.Stdout);
    }

    [Fact]
    public void FindConfigPath_ExecutionFails_ReturnsFailureResult()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "find", "unknown-instance"))))
            .Returns(new KgsmResult(new ProcessResult(1, string.Empty, "Instance not found")));

        KgsmResult result = _instanceService.FindConfigPath("unknown-instance");

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
    }

    // --- GetInstanceConfigValue : Execute("instances", "config-get", name, key) ---

    [Fact]
    public void GetInstanceConfigValue_NullInstanceName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.GetInstanceConfigValue(null!, "auto_update"));
    }

    [Fact]
    public void GetInstanceConfigValue_NullKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.GetInstanceConfigValue("my-instance", null!));
    }

    [Fact]
    public void GetInstanceConfigValue_SuccessfulExecution_ReturnsValue()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "config-get", "my-instance", "auto_update"))))
            .Returns(new KgsmResult(new ProcessResult(0, "true", string.Empty)));

        KgsmResult result = _instanceService.GetInstanceConfigValue("my-instance", "auto_update");

        Assert.True(result.IsSuccess);
        Assert.Equal("true", result.Stdout);
    }

    [Fact]
    public void GetInstanceConfigValue_ExecutionFails_ReturnsFailureResult()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "config-get", "unknown-instance", "auto_update"))))
            .Returns(new KgsmResult(new ProcessResult(1, string.Empty, "Instance not found")));

        KgsmResult result = _instanceService.GetInstanceConfigValue("unknown-instance", "auto_update");

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
    }

    // --- SetInstanceConfigValue : Execute("instances", "config-set", name, "key=value") ---

    [Fact]
    public void SetInstanceConfigValue_NullInstanceName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.SetInstanceConfigValue(null!, "auto_update", "true"));
    }

    [Fact]
    public void SetInstanceConfigValue_NullKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.SetInstanceConfigValue("my-instance", null!, "true"));
    }

    [Fact]
    public void SetInstanceConfigValue_NullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.SetInstanceConfigValue("my-instance", "auto_update", null!));
    }

    [Fact]
    public void SetInstanceConfigValue_SuccessfulExecution_ReturnsSuccessResult()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "config-set", "my-instance", "auto_update=true"))))
            .Returns(new KgsmResult(new ProcessResult(0, "[SUCCESS] Set 'auto_update'", string.Empty)));

        KgsmResult result = _instanceService.SetInstanceConfigValue("my-instance", "auto_update", "true");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void SetInstanceConfigValue_ValueWithEqualsAndSpaces_PassedAsSingleArgvElement()
    {
        // The value contains spaces and an embedded '='; it must be joined to the
        // key as one argv element so kgsm can split it on the first '=' only.
        const string value = "--foo=bar baz";
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "config-set", "my-instance", "executable_arguments=--foo=bar baz"))))
            .Returns(new KgsmResult(new ProcessResult(0, string.Empty, string.Empty)));

        KgsmResult result = _instanceService.SetInstanceConfigValue("my-instance", "executable_arguments", value);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void SetInstanceConfigValue_EmptyValue_IsAllowedAndProducesTrailingEquals()
    {
        // Clearing a value is valid: key= with nothing after the '='.
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "config-set", "my-instance", "executable_arguments="))))
            .Returns(new KgsmResult(new ProcessResult(0, string.Empty, string.Empty)));

        KgsmResult result = _instanceService.SetInstanceConfigValue("my-instance", "executable_arguments", string.Empty);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void SetInstanceConfigValue_RefusedKey_ReturnsFailureResultNotException()
    {
        // A protected key is refused by kgsm with a non-zero exit code; the library
        // surfaces that as a failed result rather than throwing.
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a, "instances", "config-set", "my-instance", "name=hacked"))))
            .Returns(new KgsmResult(new ProcessResult(8, string.Empty, "'name' is a protected key")));

        KgsmResult result = _instanceService.SetInstanceConfigValue("my-instance", "name", "hacked");

        Assert.False(result.IsSuccess);
        Assert.Equal(8, result.ExitCode);
    }

    // --- SetMaintenanceWindows : one config-set of the windows, naming their author ---

    [Fact]
    public void SetMaintenanceWindows_NamesTheAuthor()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a,
                "instances", "config-set", "my-instance", "maintenance_windows=daily@05:00/backup",
                "--author", "usr_alice"))))
            .Returns(new KgsmResult(new ProcessResult(0, "ok", string.Empty)));

        KgsmResult result = _instanceService.SetMaintenanceWindows("my-instance", "daily@05:00/backup", "usr_alice");

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// No author is no flag at all: the engine then writes the windows with nobody recorded, which is
    /// the windows running nothing — never an empty author string the engine would refuse.
    /// </summary>
    [Fact]
    public void SetMaintenanceWindows_WithNoAuthor_PassesNoFlag()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a,
                "instances", "config-set", "my-instance", "maintenance_windows="))))
            .Returns(new KgsmResult(new ProcessResult(0, "ok", string.Empty)));

        KgsmResult result = _instanceService.SetMaintenanceWindows("my-instance", string.Empty, author: null);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void SetMaintenanceWindows_NamesTheAuthorsDisplayName()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a,
                "instances", "config-set", "my-instance", "maintenance_windows=daily@05:00/backup",
                "--author", "usr_alice", "--author-name", "Alice Doe"))))
            .Returns(new KgsmResult(new ProcessResult(0, "ok", string.Empty)));

        KgsmResult result = _instanceService.SetMaintenanceWindows(
            "my-instance", "daily@05:00/backup", "usr_alice", authorName: "Alice Doe");

        Assert.True(result.IsSuccess);
    }

    /// <summary>A name belongs to an author: with nobody named it is not passed.</summary>
    [Fact]
    public void SetMaintenanceWindows_WithNoAuthor_PassesNoName()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a => ArgsAre(a,
                "instances", "config-set", "my-instance", "maintenance_windows="))))
            .Returns(new KgsmResult(new ProcessResult(0, "ok", string.Empty)));

        KgsmResult result = _instanceService.SetMaintenanceWindows(
            "my-instance", string.Empty, author: null, authorName: "Alice Doe");

        Assert.True(result.IsSuccess);
    }

    // --- SetDisplayName : one config-set of display_name, text handed over verbatim ---

    [Fact]
    public void SetDisplayName_NullInstanceId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.SetDisplayName(null!, "Ana's Valheim"));
    }

    [Fact]
    public void SetDisplayName_NullDisplayName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.SetDisplayName("my-instance", null!));
    }

    [Fact]
    public void SetDisplayName_IssuesConfigSetOfDisplayName()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a =>
                ArgsAre(a, "instances", "config-set", "my-instance", "display_name=Weekend Server"))))
            .Returns(new KgsmResult(new ProcessResult(0, "ok", string.Empty)));

        KgsmResult result = _instanceService.SetDisplayName("my-instance", "Weekend Server");

        Assert.True(result.IsSuccess);
    }

    [Theory]
    // A label is free text and never reaches a path, so the engine owns the escaping and none of
    // this is quoted, escaped or rejected on the way out — doing any of it here would be a second
    // answer to disagree with the engine's own.
    [InlineData("Ana's \"Best\" Server")]
    [InlineData(@"C:\path\to\nowhere")]
    [InlineData("Sûper Ćool 🎮 Server")]
    [InlineData("cost: $100 `uname`")]
    [InlineData("--help")]
    public void SetDisplayName_HandsThePrintableLabelOverVerbatim(string label)
    {
        string[]? captured = CaptureDisplayNameWrite(label);

        Assert.Equal(["instances", "config-set", "my-instance", $"display_name={label}"], captured);
    }

    [Theory]
    // The one thing that is not handed over as typed. The engine renders the config to JSON with a
    // line-oriented parse that separates key from value with a tab, so a tab truncates the value
    // there and a newline turns the rest of the label into further config keys — an instance can be
    // made to report an id that is not its own. Both are silent, so the characters do not go.
    [InlineData("A\tB", "AB")]
    [InlineData("Nice\nname=victim", "Nicename=victim")]
    [InlineData("Nice\r\nruntime=container", "Niceruntime=container")]
    [InlineData("  leading and trailing  ", "leading and trailing")]
    [InlineData("\t\n  ", "")]
    public void SetDisplayName_ReducesTheLabelToOneLine(string label, string stored)
    {
        string[]? captured = CaptureDisplayNameWrite(label);

        Assert.Equal(["instances", "config-set", "my-instance", $"display_name={stored}"], captured);
    }

    private string[]? CaptureDisplayNameWrite(string label)
    {
        string[]? captured = null;
        _mockCommandExecutor
            .Setup(x => x.Execute(It.IsAny<string[]>()))
            .Callback<string[]>(a => captured = a)
            .Returns(new KgsmResult(new ProcessResult(0, "ok", string.Empty)));

        _instanceService.SetDisplayName("my-instance", label);

        Assert.NotNull(captured);
        return captured;
    }

    [Fact]
    public void SetDisplayName_EmptyLabel_IsTheClear()
    {
        // Clearing is a real operation, not a missing argument: the instance goes back to reading as
        // its id, which is what every reader of a config with no display_name already answers.
        _mockCommandExecutor
            .Setup(x => x.Execute(It.Is<string[]>(a =>
                ArgsAre(a, "instances", "config-set", "my-instance", "display_name="))))
            .Returns(new KgsmResult(new ProcessResult(0, "ok", string.Empty)));

        KgsmResult result = _instanceService.SetDisplayName("my-instance", string.Empty);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void SetDisplayName_WithProvenance_StampsActorAndOrigin()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(
                It.Is<IReadOnlyDictionary<string, string>>(e =>
                    e["KGSM_EVENT_ACTOR"] == "discord:Haru" && e["KGSM_EVENT_ORIGIN"] == "bot"),
                It.Is<string[]>(a =>
                    ArgsAre(a, "instances", "config-set", "my-instance", "display_name=Weekend Server"))))
            .Returns(new KgsmResult(new ProcessResult(0, "ok", string.Empty)));

        KgsmResult result = _instanceService.SetDisplayName("my-instance", "Weekend Server", "discord:Haru", "bot");

        Assert.True(result.IsSuccess);
    }

    // --- SetInstanceNote : three config-set calls, attribution first, body LAST ---

    // Capture every config-set assignment the service issues, in order.
    private List<string> CaptureNoteWrites(int failAfter = int.MaxValue)
    {
        var seen = new List<string>();
        _mockCommandExecutor
            .Setup(x => x.Execute(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string[]>()))
            .Returns((IReadOnlyDictionary<string, string> _, string[] args) =>
            {
                seen.Add(args[^1]);
                return seen.Count > failAfter
                    ? new KgsmResult(new ProcessResult(8, string.Empty, "engine refused the key"))
                    : new KgsmResult(new ProcessResult(0, string.Empty, string.Empty));
            });
        _mockCommandExecutor
            .Setup(x => x.Execute(It.IsAny<string[]>()))
            .Returns((string[] args) =>
            {
                seen.Add(args[^1]);
                return seen.Count > failAfter
                    ? new KgsmResult(new ProcessResult(8, string.Empty, "engine refused the key"))
                    : new KgsmResult(new ProcessResult(0, string.Empty, string.Empty));
            });
        return seen;
    }

    [Fact]
    public void SetInstanceNote_NullInstanceName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.SetInstanceNote(null!, "hi"));
    }

    [Fact]
    public void SetInstanceNote_NullBody_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _instanceService.SetInstanceNote(Instance, null!));
    }

    [Fact]
    public void SetInstanceNote_WritesAttributionFirstAndTheEncodedBodyLast()
    {
        List<string> writes = CaptureNoteWrites();

        InstanceNoteResult result = _instanceService.SetInstanceNote(Instance, "Modpack v2.4", "cristian", "ui");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [InstanceNote.UpdatedByKey, InstanceNote.UpdatedAtKey, InstanceNote.BodyKey],
            result.AppliedKeys);

        Assert.Equal(3, writes.Count);
        Assert.Equal("note_updated_by=cristian", writes[0]);
        Assert.StartsWith("note_updated_at=", writes[1]);
        // The body rides encoded — a raw body could brick the sourced config file.
        Assert.Equal($"note={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Modpack v2.4"))}", writes[2]);
    }

    [Fact]
    public void SetInstanceNote_StampsAnIsoUtcTimestamp()
    {
        List<string> writes = CaptureNoteWrites();

        _instanceService.SetInstanceNote(Instance, "hi", "cristian", "ui");

        string stamped = writes[1]["note_updated_at=".Length..];
        Assert.EndsWith("Z", stamped);
        DateTimeOffset parsed = DateTimeOffset.Parse(stamped, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True((DateTimeOffset.UtcNow - parsed).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void SetInstanceNote_NoActor_WritesAnEmptyUpdatedBy()
    {
        // Honest unknown — never a fabricated author.
        List<string> writes = CaptureNoteWrites();

        _instanceService.SetInstanceNote(Instance, "hi");

        Assert.Equal("note_updated_by=", writes[0]);
    }

    [Fact]
    public void SetInstanceNote_EmptyBody_IsTheClearAndStillStampsAttribution()
    {
        // Clearing blanks the body but records who cleared it and when.
        List<string> writes = CaptureNoteWrites();

        InstanceNoteResult result = _instanceService.SetInstanceNote(Instance, string.Empty, "cristian", "ui");

        Assert.True(result.IsSuccess);
        Assert.Equal("note_updated_by=cristian", writes[0]);
        Assert.Equal("note=", writes[2]);
    }

    [Fact]
    public void SetInstanceNote_OverLengthBody_ThrowsBeforeAnyWrite()
    {
        // The length check runs before the first config-set, so an over-long body leaves the
        // config completely untouched rather than stranding fresh attribution on the old note.
        List<string> writes = CaptureNoteWrites();

        Assert.Throws<ArgumentException>(() =>
            _instanceService.SetInstanceNote(Instance, new string('a', InstanceNote.MaxLength + 1), "cristian", "ui"));

        Assert.Empty(writes);
    }

    [Fact]
    public void SetInstanceNote_EngineRefusesTheBody_ReportsWhichKeysLanded()
    {
        // The write is a sequence, not a transaction: the caller must be able to say what applied.
        List<string> writes = CaptureNoteWrites(failAfter: 2);

        InstanceNoteResult result = _instanceService.SetInstanceNote(Instance, "hi", "cristian", "ui");

        Assert.False(result.IsSuccess);
        Assert.Equal([InstanceNote.UpdatedByKey, InstanceNote.UpdatedAtKey], result.AppliedKeys);
        Assert.Equal(InstanceNote.BodyKey, result.FailedKey);
        Assert.Equal(8, result.ExitCode);
        Assert.Equal("engine refused the key", result.Error);
    }

    [Fact]
    public void SetInstanceNote_FirstKeyRefused_StopsImmediately()
    {
        List<string> writes = CaptureNoteWrites(failAfter: 0);

        InstanceNoteResult result = _instanceService.SetInstanceNote(Instance, "hi", "cristian", "ui");

        Assert.False(result.IsSuccess);
        Assert.Empty(result.AppliedKeys);
        Assert.Equal(InstanceNote.UpdatedByKey, result.FailedKey);
        Assert.Single(writes);
    }

    // --- provenance (1.15.0): actor/origin propagate as KGSM_EVENT_* env on every mutation ----------
    // The long-running verbs (install/uninstall/update/backup/restore) take the env+timeout overload;
    // config-set takes the default-timeout env overload; start/stop/restart forward to the lifecycle
    // layer. A null/empty value is omitted (KGSM keeps its honest fallback), and with NO provenance the
    // plain (no-env) path is taken so existing behaviour is unchanged.

    private static readonly KgsmResult Ok = new(new ProcessResult(0, "ok", string.Empty));

    private void SetupEnvTimeout() => _mockCommandExecutor
        .Setup(x => x.Execute(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
        .Returns(Ok);

    [Fact]
    public void Install_WithProvenance_StampsActorAndOriginOnTheEnvTimeoutOverload()
    {
        SetupEnvTimeout();

        _instanceService.Install("valheim", actor: "discord:haru", origin: "discord");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e =>
                e["KGSM_EVENT_ACTOR"] == "discord:haru" && e["KGSM_EVENT_ORIGIN"] == "discord"),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "install", "valheim"))), Times.Once);
    }

    [Fact]
    public void Install_WithoutProvenance_TakesThePlainTimeoutPath_NoEnvOverload()
    {
        _mockCommandExecutor
            .Setup(x => x.Execute(It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Returns(Ok);

        _instanceService.Install("valheim");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<TimeSpan>(), It.Is<string[]>(a => ArgsAre(a, "install", "valheim"))), Times.Once);
        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<TimeSpan>(), It.IsAny<string[]>()), Times.Never);
    }

    [Fact]
    public void Uninstall_WithProvenance_StampsEnv()
    {
        SetupEnvTimeout();

        _instanceService.Uninstall("valheim", actor: "discord:haru", origin: "discord");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e => e["KGSM_EVENT_ORIGIN"] == "discord"),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "uninstall", "valheim", "--force"))), Times.Once);
    }

    [Fact]
    public void Move_WithProvenance_StampsEnv()
    {
        SetupEnvTimeout();

        _instanceService.Move("valheim", "ssd", actor: "user:heisen", origin: "web");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e => e["KGSM_EVENT_ORIGIN"] == "web"),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "instances", "move", "valheim", "--library", "ssd"))), Times.Once);
    }

    [Fact]
    public void Update_WithProvenance_StampsEnv()
    {
        SetupEnvTimeout();

        _instanceService.Update("valheim", actor: "discord:haru", origin: "assistant");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e =>
                e["KGSM_EVENT_ACTOR"] == "discord:haru" && e["KGSM_EVENT_ORIGIN"] == "assistant"),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "instances", "update", "valheim"))), Times.Once);
    }

    [Fact]
    public void CreateBackup_WithProvenance_StampsEnv()
    {
        SetupEnvTimeout();

        _instanceService.CreateBackup("valheim", actor: "discord:haru", origin: "discord");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e => e["KGSM_EVENT_ACTOR"] == "discord:haru"),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "instances", "create-backup", "valheim"))), Times.Once);
    }

    [Fact]
    public void CreateBackup_WithReasonAndRetention_ForwardsBoth()
    {
        SetupEnvTimeout();

        _instanceService.CreateBackup("valheim", actor: "system:reactor", origin: "system",
            reason: BackupReason.Incident, retention: BackupRetention.Pinned);

        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "instances", "create-backup", "valheim",
                "--reason=incident", "--retention=pinned"))), Times.Once);
    }

    [Fact]
    public void CreateBackup_WithoutAReason_SendsNoFlag()
    {
        // The engine's own default (manual) applies. Sending "--reason=manual" here would make the
        // library, not the engine, the place that decides what an unqualified backup is.
        SetupEnvTimeout();

        _instanceService.CreateBackup("valheim", actor: "discord:haru", origin: "discord");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "instances", "create-backup", "valheim"))), Times.Once);
    }

    [Theory]
    [InlineData("whenever")]
    [InlineData("Manual")]
    public void CreateBackup_WithAnUnknownReason_Throws(string reason)
    {
        // Refused before the process is spawned: an unrecognised word in the one record of what a
        // backup is reads as a classification without being one.
        Assert.Throws<ArgumentException>(() => _instanceService.CreateBackup("valheim", reason: reason));
    }

    [Fact]
    public void CreateBackup_WithAnUnknownRetention_Throws()
    {
        Assert.Throws<ArgumentException>(() => _instanceService.CreateBackup("valheim", retention: "forever"));
    }

    [Fact]
    public void PinBackup_WithProvenance_StampsEnv()
    {
        SetupEnvTimeout();

        _instanceService.PinBackup("valheim", "backup-1", actor: "discord:haru", origin: "ui");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e => e["KGSM_EVENT_ORIGIN"] == "ui"),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "instances", "pin-backup", "valheim", "backup-1"))), Times.Once);
    }

    [Fact]
    public void UnpinBackup_WithProvenance_StampsEnv()
    {
        SetupEnvTimeout();

        _instanceService.UnpinBackup("valheim", "backup-1", actor: "discord:haru", origin: "ui");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e => e["KGSM_EVENT_ACTOR"] == "discord:haru"),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "instances", "unpin-backup", "valheim", "backup-1"))), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PinBackup_WithBlankBackupName_Throws(string backupName)
    {
        Assert.Throws<ArgumentException>(() => _instanceService.PinBackup("valheim", backupName));
    }

    [Fact]
    public void RestoreBackup_WithProvenance_StampsEnv()
    {
        SetupEnvTimeout();

        _instanceService.RestoreBackup("valheim", "backup-1", actor: "discord:haru", origin: "discord");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e => e["KGSM_EVENT_ORIGIN"] == "discord"),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "instances", "restore-backup", "valheim", "backup-1"))), Times.Once);
    }

    [Fact]
    public void DeleteBackup_WithProvenance_StampsEnv()
    {
        SetupEnvTimeout();

        _instanceService.DeleteBackup("valheim", "backup-1", actor: "discord:haru", origin: "discord");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e => e["KGSM_EVENT_ORIGIN"] == "discord"),
            It.IsAny<TimeSpan>(),
            It.Is<string[]>(a => ArgsAre(a, "instances", "delete-backup", "valheim", "backup-1"))), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DeleteBackup_WithBlankBackupName_Throws(string? backupName)
    {
        // A blank id must never reach the engine: `instances delete-backup <instance> ""` would be a
        // missing-argument error there, but the throw keeps the failure at the caller where the bug is.
        // ThrowsAny, because null yields ArgumentNullException and blank yields ArgumentException —
        // both are the same contract to a caller, and xUnit's Assert.Throws is exact-type.
        Assert.ThrowsAny<ArgumentException>(() => _instanceService.DeleteBackup("valheim", backupName!));
    }

    [Fact]
    public void SetInstanceConfigValue_WithProvenance_UsesTheDefaultTimeoutEnvOverload()
    {
        // config-set is quick → the env overload WITHOUT an explicit timeout, not the env+timeout one.
        _mockCommandExecutor
            .Setup(x => x.Execute(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string[]>()))
            .Returns(Ok);

        _instanceService.SetInstanceConfigValue("valheim", "key", "val", actor: "discord:haru", origin: "discord");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e =>
                e["KGSM_EVENT_ACTOR"] == "discord:haru" && e["KGSM_EVENT_ORIGIN"] == "discord"),
            It.Is<string[]>(a => ArgsAre(a, "instances", "config-set", "valheim", "key=val"))), Times.Once);
    }

    [Fact]
    public void OnlyActor_OmitsOriginFromTheEnv_NeverFabricated()
    {
        SetupEnvTimeout();

        _instanceService.CreateBackup("valheim", actor: "system:watchdog");

        _mockCommandExecutor.Verify(x => x.Execute(
            It.Is<IReadOnlyDictionary<string, string>>(e =>
                e["KGSM_EVENT_ACTOR"] == "system:watchdog" && !e.ContainsKey("KGSM_EVENT_ORIGIN")),
            It.IsAny<TimeSpan>(), It.IsAny<string[]>()), Times.Once);
    }

    [Fact]
    public void Start_WithProvenance_ForwardsActorOriginToTheLifecycleLayer()
    {
        _instanceService.Start("valheim", actor: "discord:haru", origin: "discord");
        _mockLifecycleService.Verify(x => x.Start("valheim", "discord:haru", "discord"), Times.Once);
    }

    [Fact]
    public void StopAndRestart_WithProvenance_ForwardToTheLifecycleLayer()
    {
        _instanceService.Stop("valheim", actor: "discord:haru", origin: "assistant");
        _instanceService.Restart("valheim", actor: "api:token", origin: "api");
        _mockLifecycleService.Verify(x => x.Stop("valheim", "discord:haru", "assistant"), Times.Once);
        _mockLifecycleService.Verify(x => x.Restart("valheim", "api:token", "api"), Times.Once);
    }
}

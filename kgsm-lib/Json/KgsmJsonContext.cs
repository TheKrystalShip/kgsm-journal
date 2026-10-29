using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM;

/// <summary>
/// System.Text.Json source-generation context for every type the library
/// (de)serializes from KGSM output. Routing all deserialization through this
/// context (instead of reflection-based <see cref="JsonSerializer"/> overloads)
/// is what makes the library trim- and Native-AOT-safe — it eliminates the
/// IL2026/IL3050 warnings and removes runtime reflection-metadata building from
/// the hot path.
/// </summary>
/// <remarks>
/// Every type passed to <c>KgsmCommandExecutor.ExecuteForJson&lt;T&gt;</c> or
/// deserialized from the event socket MUST be registered below, otherwise it
/// throws <see cref="System.NotSupportedException"/> at runtime (there is no
/// reflection fallback). Enum members are matched case-insensitively, so KGSM's
/// lowercase wire values (e.g. <c>"systemd"</c>) bind to the PascalCase enum
/// members via the per-enum <c>[JsonConverter(JsonStringEnumConverter&lt;T&gt;)]</c>
/// attributes.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
// --- Command-executor result types ---
[JsonSerializable(typeof(Instance))]
[JsonSerializable(typeof(Dictionary<string, Instance>))]
[JsonSerializable(typeof(PortMapping))]
[JsonSerializable(typeof(List<PortMapping>))]
[JsonSerializable(typeof(Library))]
[JsonSerializable(typeof(List<Library>))]
[JsonSerializable(typeof(InstanceRuntimeStatus))]
[JsonSerializable(typeof(Reading<InstanceRuntimeStatus>))]
[JsonSerializable(typeof(Dictionary<string, Reading<InstanceRuntimeStatus>>))]
[JsonSerializable(typeof(SystemInfo))]
[JsonSerializable(typeof(Blueprint))]
[JsonSerializable(typeof(BlueprintMetadata))]
[JsonSerializable(typeof(Dictionary<string, Blueprint>))]
[JsonSerializable(typeof(BlueprintCandidate))]
[JsonSerializable(typeof(List<BlueprintCandidate>))]
[JsonSerializable(typeof(BlueprintCandidates))]
[JsonSerializable(typeof(BlueprintValidation))]
[JsonSerializable(typeof(InstanceBackup))]
[JsonSerializable(typeof(List<InstanceBackup>))]
[JsonSerializable(typeof(InstanceConfigEntry))]
[JsonSerializable(typeof(List<InstanceConfigEntry>))]
[JsonSerializable(typeof(HostPort))]
[JsonSerializable(typeof(List<HostPort>))]
[JsonSerializable(typeof(PortConflict))]
[JsonSerializable(typeof(List<PortConflict>))]
[JsonSerializable(typeof(List<string>))]
// --- Instance transfer (the manifest and the import verbs' answers) ---
[JsonSerializable(typeof(TransferManifest))]
[JsonSerializable(typeof(ImportVerdict))]
[JsonSerializable(typeof(ImportBegun))]
[JsonSerializable(typeof(ImportedInstance))]
[JsonSerializable(typeof(StagedImport))]
[JsonSerializable(typeof(List<StagedImport>))]
[JsonSerializable(typeof(BlueprintRequirements))]
[JsonSerializable(typeof(Dictionary<string, string>))]
// --- kgsm-watchdog control-surface DTOs (typed client over the unix socket) ---
[JsonSerializable(typeof(WatchdogActionResult))]
[JsonSerializable(typeof(WatchdogInstanceState))]
[JsonSerializable(typeof(WatchdogInstanceState[]))]
[JsonSerializable(typeof(WatchdogRunTimes))]
[JsonSerializable(typeof(WatchdogRunTimes[]))]
[JsonSerializable(typeof(WatchdogReadyState))]
[JsonSerializable(typeof(WatchdogPlayer))]
[JsonSerializable(typeof(WatchdogPlayer[]))]
[JsonSerializable(typeof(Dictionary<string, WatchdogPlayer[]>))]
[JsonSerializable(typeof(WatchdogInstancePresence))]
[JsonSerializable(typeof(Dictionary<string, WatchdogInstancePresence>))]
[JsonSerializable(typeof(WatchdogUpnpMapping))]
[JsonSerializable(typeof(WatchdogUpnpList))]
[JsonSerializable(typeof(WatchdogConsoleRun))]
[JsonSerializable(typeof(WatchdogConsoleRun[]))]
// --- Event envelope + every event data payload (the Type-keyed dispatch in EventService) ---
[JsonSerializable(typeof(EventWrapper))]
[JsonSerializable(typeof(EventCursor))]
// One cursor per producer, for a consumer reading more than one journal (FileFederatedEventCursorStore).
[JsonSerializable(typeof(Dictionary<string, EventCursor>))]
[JsonSerializable(typeof(InstanceCreatedData))]
[JsonSerializable(typeof(InstanceDirectoriesCreatedData))]
[JsonSerializable(typeof(InstanceFilesCreatedData))]
[JsonSerializable(typeof(InstanceDownloadStartedData))]
[JsonSerializable(typeof(InstanceDownloadFinishedData))]
[JsonSerializable(typeof(InstanceDownloadFailedData))]
[JsonSerializable(typeof(InstanceDownloadedData))]
[JsonSerializable(typeof(InstanceDeployStartedData))]
[JsonSerializable(typeof(InstanceDeployFinishedData))]
[JsonSerializable(typeof(InstanceDeployFailedData))]
[JsonSerializable(typeof(InstanceDeployedData))]
[JsonSerializable(typeof(InstanceRestartStartedData))]
[JsonSerializable(typeof(InstanceRestartStoppedData))]
[JsonSerializable(typeof(InstanceRestartFinishedData))]
[JsonSerializable(typeof(InstanceStopStartedData))]
[JsonSerializable(typeof(InstanceStopFinishedData))]
[JsonSerializable(typeof(InstanceUpdateStartedData))]
[JsonSerializable(typeof(InstanceUpdateFinishedData))]
[JsonSerializable(typeof(InstanceUpdateFailedData))]
[JsonSerializable(typeof(InstanceUpdatedData))]
[JsonSerializable(typeof(InstanceUpdateAvailableData))]
[JsonSerializable(typeof(InstanceVersionUpdatedData))]
[JsonSerializable(typeof(InstanceInstallationStartedData))]
[JsonSerializable(typeof(InstanceInstallationFinishedData))]
[JsonSerializable(typeof(InstanceInstalledData))]
[JsonSerializable(typeof(InstanceMovedData))]
[JsonSerializable(typeof(InstanceExportedData))]
[JsonSerializable(typeof(InstanceImportedData))]
[JsonSerializable(typeof(InstanceStartedData))]
[JsonSerializable(typeof(InstanceStoppedData))]
[JsonSerializable(typeof(InstanceRestartedData))]
[JsonSerializable(typeof(InstanceCrashedData))]
[JsonSerializable(typeof(InstanceFailedData))]
[JsonSerializable(typeof(InstanceReadyData))]
[JsonSerializable(typeof(InstanceBackupStartedData))]
[JsonSerializable(typeof(InstanceBackupFinishedData))]
[JsonSerializable(typeof(InstanceRestoreStartedData))]
[JsonSerializable(typeof(InstanceRestoreFinishedData))]
[JsonSerializable(typeof(InstanceBackupCreatedData))]
[JsonSerializable(typeof(InstanceBackupRestoredData))]
[JsonSerializable(typeof(InstanceBackupDeletedData))]
[JsonSerializable(typeof(InstanceBackupsPrunedData))]
[JsonSerializable(typeof(InstanceBackupPinnedData))]
[JsonSerializable(typeof(InstanceBackupUnpinnedData))]
[JsonSerializable(typeof(InstanceFilesRemovedData))]
[JsonSerializable(typeof(InstanceDirectoriesRemovedData))]
[JsonSerializable(typeof(InstanceRemovedData))]
[JsonSerializable(typeof(InstanceUninstallStartedData))]
[JsonSerializable(typeof(InstanceUninstallFinishedData))]
[JsonSerializable(typeof(InstanceUninstallFailedData))]
[JsonSerializable(typeof(InstanceUninstalledData))]
[JsonSerializable(typeof(InstancePortsOpenedData))]
[JsonSerializable(typeof(InstancePortsClosedData))]
[JsonSerializable(typeof(InstanceUpnpOpenedData))]
[JsonSerializable(typeof(InstanceUpnpReassertedData))]
[JsonSerializable(typeof(InstanceUpnpClosedData))]
[JsonSerializable(typeof(InstancePlayerJoinedData))]
[JsonSerializable(typeof(InstancePlayerLeftData))]
[JsonSerializable(typeof(InstancePlayerKickedData))]
[JsonSerializable(typeof(InstancePlayerBannedData))]
[JsonSerializable(typeof(InstancePlayerUnbannedData))]
[JsonSerializable(typeof(InstanceConfigChangedData))]
[JsonSerializable(typeof(InstanceDisplayNameChangedData))]
[JsonSerializable(typeof(InstanceInputSentData))]
[JsonSerializable(typeof(InstanceAnnouncementSentData))]
// Host-scoped monitoring facts (kgsm-monitor's journal).
[JsonSerializable(typeof(HostThresholdBreachedData))]
[JsonSerializable(typeof(HostThresholdClearedData))]
// The Control Panel's own facts (kgsm-api's journal).
[JsonSerializable(typeof(AuthSessionEventData))]
[JsonSerializable(typeof(AuthSessionRevokedData))]
[JsonSerializable(typeof(AuthLockedOutData))]
[JsonSerializable(typeof(AuthAdminRefusedData))]
[JsonSerializable(typeof(UserAccountEventData))]
[JsonSerializable(typeof(IdentityLinkEventData))]
[JsonSerializable(typeof(AssignmentEventData))]
[JsonSerializable(typeof(AuthorityRecordEventData))]
[JsonSerializable(typeof(CatalogEventData))]
[JsonSerializable(typeof(ServiceRequirementEventData))]
[JsonSerializable(typeof(ApplicationEventData))]
[JsonSerializable(typeof(TokenExchangeEventData))]
[JsonSerializable(typeof(ServiceProvisioningEventData))]
[JsonSerializable(typeof(ServiceConfigChangedEventData))]
[JsonSerializable(typeof(ServiceRestartedEventData))]
[JsonSerializable(typeof(LeafReadyEventData))]
[JsonSerializable(typeof(LeafDegradedEventData))]
[JsonSerializable(typeof(LeafRecoveredEventData))]
[JsonSerializable(typeof(LeafStoppingEventData))]
[JsonSerializable(typeof(AssistantClaimCorrectedEventData))]
[JsonSerializable(typeof(AssistantActionDeclinedEventData))]
[JsonSerializable(typeof(AssistantActionProposedEventData))]
[JsonSerializable(typeof(AssistantBlueprintAuthoringStartedEventData))]
[JsonSerializable(typeof(AssistantBlueprintAuthoredEventData))]
[JsonSerializable(typeof(ReactorDecidedEventData))]
[JsonSerializable(typeof(ReactorProposedEventData))]
[JsonSerializable(typeof(ReactorResolvedEventData))]
[JsonSerializable(typeof(ReactorActedEventData))]
[JsonSerializable(typeof(FileWrittenEventData))]
[JsonSerializable(typeof(BackupDownloadedEventData))]
[JsonSerializable(typeof(LibraryAddedData))]
[JsonSerializable(typeof(LibraryRemovedData))]
[JsonSerializable(typeof(BlueprintCreatedData))]
[JsonSerializable(typeof(BlueprintUpdatedData))]
[JsonSerializable(typeof(BlueprintRemovedData))]
[JsonSerializable(typeof(KgsmPaths))]
public partial class KgsmJsonContext : JsonSerializerContext
{
}

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance used by
/// <c>KgsmCommandExecutor</c> to deserialize KGSM CLI JSON. It pairs the
/// source-generated <see cref="KgsmJsonContext"/> type resolver (AOT-safe) with
/// the two global string-coercion converters KGSM's output requires: KGSM emits
/// some scalars as strings (<c>"0"</c>/<c>"1"</c>, <c>"30"</c>) rather than JSON
/// numbers/booleans, and many <c>Instance</c> bool/int properties carry no
/// per-property converter, so these must apply across the whole object graph.
/// </summary>
internal static class KgsmJson
{
    internal static readonly JsonSerializerOptions ExecutorOptions = BuildExecutorOptions();

    private static JsonSerializerOptions BuildExecutorOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = KgsmJsonContext.Default,
            PropertyNameCaseInsensitive = true,
        };

        // Global string -> bool/int coercion for KGSM's stringly-typed scalars.
        // Per-property [JsonConverter] attributes still take precedence where set.
        options.Converters.Add(new JsonStringToBoolConverter());
        options.Converters.Add(new JsonStringToIntConverter());

        // Maps KGSM's polymorphic bulk-status element (status object | error
        // object) to a Reading<InstanceRuntimeStatus>; CanConvert matches only
        // that one closed type, so ordering vs the scalar converters is moot.
        options.Converters.Add(new KgsmBulkStatusReadingConverter());

        return options;
    }
}

using TheKrystalShip.KGSM.Lifecycle;

namespace TheKrystalShip.KGSM.Events;

/// <summary>
/// What each engine event <em>is</em> — its subject, whether it reports a fact or a step inside one,
/// whether it reports a failure, and what kind of data each of its payload fields holds.
/// </summary>
/// <remarks>
/// <para>
/// The companion to <see cref="Services.EventService"/>'s type map. That one says which class an event
/// deserializes into; this says what a consumer needs to know to handle it without having worked out
/// the answer for itself. Every surface that renders the journal was deriving this independently,
/// which is how two of them came to disagree about whether a player's network address may be shown.
/// </para>
/// <para>
/// <b>This states facts and never policy.</b> <see cref="EventWeight.Phase"/> does not mean "hide
/// this" and <see cref="FieldSensitivity.Personal"/> does not mean "refuse this" — a consumer decides
/// what to do with a fact, and two consumers are allowed to decide differently. The moment this
/// carries a permission, the surfaces lose the right to differ and every one of them inherits
/// whichever surface wrote the rule.
/// </para>
/// <para>
/// Static data, no reflection: this library is embedded by Native-AOT consumers. The tests that keep
/// it honest — every typed event described, every declared payload property classified — do use
/// reflection, and are not AOT.
/// </para>
/// </remarks>
public static class KgsmEventCatalog
{
    /// <summary>
    /// What is known about <paramref name="type"/>.
    /// </summary>
    /// <remarks>
    /// <b>Never null.</b> An event type this build has never heard of gets a descriptor with
    /// <see cref="EventDescriptor.Known"/> false, so a consumer always has something to work from and
    /// never has to decide whether the lookup failed or the answer was "nothing". See
    /// <see cref="EventDescriptor.Known"/> for what such a descriptor does and does not claim.
    /// </remarks>
    public static EventDescriptor Describe(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Descriptors.TryGetValue(type, out EventDescriptor? known) ? known : Unrecognized(type);
    }

    /// <summary>Every event type this build classifies, in no particular order.</summary>
    public static IReadOnlyCollection<EventDescriptor> All => Descriptors.Values;

    /// <summary>
    /// What the event carrying <typeparamref name="TData"/> is called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inverse of the binding <see cref="Describe"/> reads forwards, and the reason a consumer
    /// that dispatches on a payload class never has to spell the name beside it. A typed handler
    /// already says which event it is for — the class is the identity — so asking here is a
    /// derivation, where a literal next to the registration is a second declaration that can drift
    /// from the first without anything noticing.
    /// </para>
    /// <para>
    /// <b>Throws when the class does not name one event.</b> A payload shared by several events (one
    /// shape carrying a from/to pair for every account move, say) names none of them on its own, and
    /// its producer's own constants are what say which. A class this build does not classify names
    /// nothing at all. Both are defects in the calling code rather than facts about a host, so they
    /// fail loudly instead of answering with a guess.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TData"/> is bound to no event, or to more than one.
    /// </exception>
    public static string NameOf<TData>() where TData : KgsmEventDataBase =>
        ByPayload.TryGetValue(typeof(TData), out string? name)
            ? name
            : throw new InvalidOperationException(
                $"{typeof(TData).Name} names no single event — it is either unclassified or shared by "
                + "several, and the producer's own constants are what name those.");

    /// <summary>
    /// A descriptor for a type nobody has classified.
    /// </summary>
    /// <remarks>
    /// The subject is read off the engine's own naming convention (<c>&lt;subject&gt;_&lt;verb&gt;</c>)
    /// rather than left unanswered: every consumer would otherwise write that same derivation itself,
    /// which is the duplication this catalog exists to remove. Everything else is the cautious answer —
    /// a fact, no outcome claimed, and <b>no fields</b>, which means "render nothing out of the
    /// payload", not "the payload is empty". That is deliberate: an unclassified event may carry
    /// anything, and a consumer that prints unclassified fields is how a credential reaches a channel.
    /// </remarks>
    private static EventDescriptor Unrecognized(string type) => new(
        Type: type,
        Subject: SubjectOf(type),
        Weight: EventWeight.Fact,
        Outcome: EventOutcome.Neutral,
        Fields: [],
        PayloadType: null,
        Known: false);

    /// <summary>
    /// What an unclassified event is about, read off its own namespace.
    /// </summary>
    /// <remarks>
    /// A name is dot-separated and its leading segment says what the event concerns, so a subject can
    /// be read from an event nothing has classified rather than guessed at. An unfamiliar namespace
    /// answers <see cref="EventSubject.Instance"/>: nearly everything on this host is about a server,
    /// and a subject is a routing hint rather than a permission — no consumer is allowed to render an
    /// unclassified payload whatever this says (see <see cref="Describe"/>'s empty field list).
    /// </remarks>
    private static EventSubject SubjectOf(string type)
    {
        int dot = type.IndexOf('.', StringComparison.Ordinal);
        ReadOnlySpan<char> head = dot < 0 ? type : type.AsSpan(0, dot);

        return head switch
        {
            "blueprint" => EventSubject.Blueprint,
            "library" => EventSubject.Library,
            "host" => EventSubject.Host,
            "leaf" or "service" => EventSubject.Service,
            "user" or "identity" or "auth" => EventSubject.Account,
            _ => EventSubject.Instance,
        };
    }

    // ---- the classification -------------------------------------------------------------------
    //
    // Grouped the way the engine emits them. A multi-step operation brackets its work with
    // <thing>_started / <thing>_finished around the one event that IS the news — those brackets are
    // Phase, the news is Fact, and a failure is always Fact because a step that did not happen is
    // exactly what somebody reading back needs to find.

    private static readonly Dictionary<string, EventDescriptor> Descriptors;

    // The payload-class inverse, holding only the classes that name exactly one event. A class bound
    // to several is absent rather than pointing at whichever was registered first: answering with one
    // of them would be a coin toss recorded as a fact.
    private static readonly Dictionary<Type, string> ByPayload;

    // Built in a static constructor rather than a field initializer, and it has to be: the shared
    // field definitions below are static initializers themselves, and those run in textual order —
    // building the table from a field initializer would read every one of them before it was assigned.
    // A static constructor body runs after all of them, whatever order the file is in.
    static KgsmEventCatalog()
    {
        Descriptors = Build();
        ByPayload = InvertByPayload(Descriptors.Values);
    }

    private static Dictionary<Type, string> InvertByPayload(IEnumerable<EventDescriptor> all)
    {
        var shared = new HashSet<Type>();
        var byPayload = new Dictionary<Type, string>();

        foreach (EventDescriptor descriptor in all)
        {
            if (descriptor.PayloadType is not { } payload) continue;
            if (!byPayload.TryAdd(payload, descriptor.Type)) shared.Add(payload);
        }

        foreach (Type payload in shared) byPayload.Remove(payload);
        return byPayload;
    }

    private static Dictionary<string, EventDescriptor> Build()
    {
        var all = new List<EventDescriptor>
        {
            // -- install -----------------------------------------------------------------------
            Instance<InstanceCreatedData>("server.install.created", EventWeight.Phase, fields: [Blueprint]),
            Instance<InstanceDirectoriesCreatedData>("server.install.directories_created", EventWeight.Phase),
            Instance<InstanceFilesCreatedData>("server.install.files_created", EventWeight.Phase),
            Instance<InstanceDownloadStartedData>("server.download.started", EventWeight.Phase),
            Instance<InstanceDownloadFinishedData>("server.download.finished", EventWeight.Phase),
            Instance<InstanceDownloadFailedData>("server.download.failed", EventWeight.Fact, EventOutcome.Failure, severity: EventSeverity.Danger),
            Instance<InstanceDownloadedData>("server.download.completed", EventWeight.Phase),
            Instance<InstanceDeployStartedData>("server.deploy.started", EventWeight.Phase),
            Instance<InstanceDeployFinishedData>("server.deploy.finished", EventWeight.Phase),
            Instance<InstanceDeployFailedData>("server.deploy.failed", EventWeight.Fact, EventOutcome.Failure, severity: EventSeverity.Danger),
            Instance<InstanceDeployedData>("server.deploy.completed", EventWeight.Phase),
            Instance<InstanceInstallationStartedData>("server.install.started", EventWeight.Phase, fields: [Blueprint]),
            Instance<InstanceInstallationFinishedData>("server.install.finished", EventWeight.Phase, fields: [Blueprint]),
            Instance<InstanceInstalledData>("server.installed", EventWeight.Fact, EventOutcome.Success, [Blueprint, LibraryName]),

            // -- placement ---------------------------------------------------------------------
            // Which disk an instance's files are on. Neutral: moving a server between two roots on
            // the same host changes nothing about the server, and the news is which library gave up
            // the space and which took it.
            Instance<InstanceMovedData>("server.moved", EventWeight.Fact, EventOutcome.Neutral,
                [Field("FromLibrary", FieldShape.Text), Field("ToLibrary", FieldShape.Text)]),

            // A transfer between nodes. The instance leaves one host and arrives on another under a
            // fresh id; each side states its own half, and neither is a failure.
            Instance<InstanceExportedData>("server.exported", EventWeight.Fact, EventOutcome.Neutral),
            Instance<InstanceImportedData>("server.imported", EventWeight.Fact, EventOutcome.Success, [Blueprint, LibraryName]),

            // -- uninstall ---------------------------------------------------------------------
            Instance<InstanceUninstallStartedData>("server.uninstall.started", EventWeight.Phase),
            Instance<InstanceUninstallFinishedData>("server.uninstall.finished", EventWeight.Phase),
            Instance<InstanceUninstallFailedData>("server.uninstall.failed", EventWeight.Fact, EventOutcome.Failure, severity: EventSeverity.Danger),
            // The nonce says which install went; like a session key it correlates and shows nothing.
            Instance<InstanceUninstalledData>("server.uninstalled", EventWeight.Fact, EventOutcome.Success,
                [Field("InstallNonce", FieldShape.Opaque)], severity: EventSeverity.Danger),
            Instance<InstanceFilesRemovedData>("server.uninstall.files_removed", EventWeight.Phase),
            Instance<InstanceDirectoriesRemovedData>("server.uninstall.directories_removed", EventWeight.Phase),
            Instance<InstanceRemovedData>("server.uninstall.removed", EventWeight.Phase),

            // -- run state ---------------------------------------------------------------------
            Instance<InstanceStartedData>("server.started", EventWeight.Fact),

            // The moment players can actually connect, which is not what instance_started reports —
            // that one says the process launched. Two facts about two different moments.
            Instance<InstanceReadyData>("server.ready", EventWeight.Fact, EventOutcome.Success),

            Instance<InstanceStoppedData>("server.stopped", EventWeight.Fact, severity: EventSeverity.Warn),
            Instance<InstanceStopStartedData>("server.stop.started", EventWeight.Phase),
            Instance<InstanceStopFinishedData>("server.stop.finished", EventWeight.Phase),
            Instance<InstanceRestartedData>("server.restarted", EventWeight.Fact),
            Instance<InstanceRestartStartedData>("server.restart.started", EventWeight.Phase),
            Instance<InstanceRestartStoppedData>("server.restart.stopped", EventWeight.Phase),
            Instance<InstanceRestartFinishedData>("server.restart.finished", EventWeight.Phase),
            // Danger, the same as the give-up. A server going down unasked is the fleet losing a
            // service, and the supervisor being on its way to bring it back does not make that
            // routine — the two are told apart by their names, which is what the split is for.
            Instance<InstanceCrashedData>("server.crashed", EventWeight.Fact, EventOutcome.Failure, [ExitCode, Restarts], severity: EventSeverity.Danger),
            Instance<InstanceFailedData>("server.crash.exhausted", EventWeight.Fact, EventOutcome.Failure, [ExitCode, Restarts], severity: EventSeverity.Danger),

            // -- versions ----------------------------------------------------------------------
            Instance<InstanceUpdateStartedData>("server.update.started", EventWeight.Phase),
            Instance<InstanceUpdateFinishedData>("server.update.finished", EventWeight.Phase),

            // The update run ended; whether the version moved is instance_version_updated's to say.
            Instance<InstanceUpdatedData>("server.update.completed", EventWeight.Phase),

            // The run ended WITHOUT the version moving, for a reason. Without it, a failed update and
            // a successful one that found nothing to do are the same two bracket lines — and since the
            // bracket alone is what a consumer settles a run on, a refused update reads as a completed
            // one. A Fact, like every other failure: a step that did not happen is exactly what
            // somebody reading back needs to find.
            Instance<InstanceUpdateFailedData>("server.update.failed", EventWeight.Fact, EventOutcome.Failure, severity: EventSeverity.Danger),

            Instance<InstanceUpdateAvailableData>("server.update.available", EventWeight.Fact, EventOutcome.Neutral,
                [Field("CurrentVersion", FieldShape.Version), Field("LatestVersion", FieldShape.Version)]),
            Instance<InstanceVersionUpdatedData>("server.updated", EventWeight.Fact, EventOutcome.Success,
                [Field("OldVersion", FieldShape.Version), Field("NewVersion", FieldShape.Version)]),

            // -- backups -----------------------------------------------------------------------
            // Both verbs are minutes of archiving on a large world, and a scheduler drives them
            // unattended — so each brackets its run the way the lifecycle verbs do, and a surface can
            // show the instance as busy for the whole of it rather than learning at the end.
            Instance<InstanceBackupStartedData>("backup.started", EventWeight.Phase),
            Instance<InstanceBackupFinishedData>("backup.finished", EventWeight.Phase),
            Instance<InstanceRestoreStartedData>("backup.restore.started", EventWeight.Phase),
            Instance<InstanceRestoreFinishedData>("backup.restore.finished", EventWeight.Phase),
            Instance<InstanceBackupCreatedData>("backup.created", EventWeight.Fact, EventOutcome.Success, [Source, Version]),
            Instance<InstanceBackupRestoredData>("backup.restored", EventWeight.Fact, EventOutcome.Success, [Source, Version], severity: EventSeverity.Warn),
            Instance<InstanceBackupDeletedData>("backup.deleted", EventWeight.Fact, EventOutcome.Neutral, [Source], severity: EventSeverity.Warn),

            // Retention is a policy an operator revises, and both directions are facts worth having:
            // pinning is why an archive outlived the rotation, unpinning is why one stopped doing so.
            Instance<InstanceBackupPinnedData>("backup.pinned", EventWeight.Fact, EventOutcome.Neutral, [Source]),
            Instance<InstanceBackupUnpinnedData>("backup.unpinned", EventWeight.Fact, EventOutcome.Neutral, [Source], severity: EventSeverity.Warn),

            Instance<InstanceBackupsPrunedData>("backup.pruned", EventWeight.Fact, EventOutcome.Neutral,
                [Field("Deleted", FieldShape.Number), Field("Kept", FieldShape.Number),
                 Field("Pinned", FieldShape.Number)]),

            // -- the doors ---------------------------------------------------------------------
            // A host firewall rule and a router NAT forward are different facts about different
            // machines, and both bracket a run rather than stepping through one — so both are facts.
            Instance<InstancePortsOpenedData>("network.ports.opened", EventWeight.Fact, EventOutcome.Neutral, [Ports]),
            Instance<InstancePortsClosedData>("network.ports.closed", EventWeight.Fact, EventOutcome.Neutral, [Ports], severity: EventSeverity.Warn),
            Instance<InstanceUpnpOpenedData>("network.upnp.opened", EventWeight.Fact, EventOutcome.Neutral, [Ports]),
            Instance<InstanceUpnpClosedData>("network.upnp.closed", EventWeight.Fact, EventOutcome.Neutral, [Ports], severity: EventSeverity.Warn),
            Instance<InstanceUpnpReassertedData>("network.upnp.reasserted", EventWeight.Fact, EventOutcome.Neutral, [Ports], severity: EventSeverity.Warn),

            // -- host monitoring ---------------------------------------------------------------
            // A breach and a recovery are two immutable facts, not one row that changes: the journal is
            // append-only, and the live view of the same condition is the alert feed, which answers a
            // different question. Neither is a Failure — a value crossing a line is a measurement, and
            // how loudly to say so is the reading surface's business.
            Host<HostThresholdBreachedData>("host.threshold.breached", EventOutcome.Neutral,
                [Field("EpisodeId", FieldShape.Opaque), Field("RuleKey", FieldShape.Text),
                 Field("Metric", FieldShape.Text), Field("Scope", FieldShape.Text),
                 Field("Ref", FieldShape.Text), Field("ServerId", FieldShape.Text),
                 Field("Threshold", FieldShape.Number), Field("PeakValue", FieldShape.Number),
                 Field("PeakBand", FieldShape.Text), Field("OpenedTs", FieldShape.Number),
                 Field("OpenValue", FieldShape.Number), Field("Band", FieldShape.Text)]),

            Host<HostThresholdClearedData>("host.threshold.cleared", EventOutcome.Neutral,
                [Field("EpisodeId", FieldShape.Opaque), Field("RuleKey", FieldShape.Text),
                 Field("Metric", FieldShape.Text), Field("Scope", FieldShape.Text),
                 Field("Ref", FieldShape.Text), Field("ServerId", FieldShape.Text),
                 Field("Threshold", FieldShape.Number), Field("PeakValue", FieldShape.Number),
                 Field("PeakBand", FieldShape.Text), Field("OpenedTs", FieldShape.Number),
                 Field("ClosedTs", FieldShape.Number), Field("CloseValue", FieldShape.Number),
                 // Not always a recovery. A rule retuned, disabled or removed closes an episode
                 // without the value ever being observed to come down.
                 Field("CloseReason", FieldShape.Text)]),

            // -- players -----------------------------------------------------------------------
            Instance<InstancePlayerJoinedData>("player.joined", EventWeight.Fact, EventOutcome.Neutral,
                [PlayerId, PlayerName, PlayerAddr, SessionKey]),
            Instance<InstancePlayerLeftData>("player.left", EventWeight.Fact, EventOutcome.Neutral,
                [PlayerId, PlayerName, PlayerAddr, SessionKey, Field("Reason", FieldShape.Text)]),

            Instance<InstancePlayerKickedData>("player.kicked", EventWeight.Fact, EventOutcome.Neutral, [Target, Command], severity: EventSeverity.Warn),
            Instance<InstancePlayerBannedData>("player.banned", EventWeight.Fact, EventOutcome.Neutral, [Target, Command], severity: EventSeverity.Danger),
            Instance<InstancePlayerUnbannedData>("player.unbanned", EventWeight.Fact, EventOutcome.Neutral, [Target, Command]),

            // -- operator actions --------------------------------------------------------------
            // The key only: kgsm deliberately never puts the value on the event, because a config
            // value can be an rcon password.
            Instance<InstanceConfigChangedData>("config.changed", EventWeight.Fact, EventOutcome.Neutral,
                [Field("Key", FieldShape.Text)]),

            // Both labels, unlike the config event above: a display name is text a person chose to be
            // read, so there is no value to withhold, and a consumer re-labelling a row would
            // otherwise have to go back to the engine to learn what to re-label it to.
            Instance<InstanceDisplayNameChangedData>("server.renamed", EventWeight.Fact, EventOutcome.Neutral,
                [Field("OldDisplayName", FieldShape.Text), Field("NewDisplayName", FieldShape.Text)]),

            Instance<InstanceInputSentData>("console.input.sent", EventWeight.Fact, EventOutcome.Neutral, [Command]),

            // The message and the command it resolved to. Its own type rather than an
            // input-sent row because its subject is the players, so "what were people told
            // on this server" is a filter rather than a pattern-match over command text.
            Instance<InstanceAnnouncementSentData>("announcement.sent", EventWeight.Fact, EventOutcome.Neutral,
                [Field("Message", FieldShape.Text), Command]),

            // -- blueprints --------------------------------------------------------------------
            BlueprintEvent<BlueprintCreatedData>("blueprint.created", [Tier, OverridesSystem, Runtime]),
            BlueprintEvent<BlueprintUpdatedData>("blueprint.updated", [Tier, OverridesSystem, Runtime]),
            BlueprintEvent<BlueprintRemovedData>("blueprint.removed", [Tier, Field("RevertedToSystem", FieldShape.Text)]),

            // -- libraries ---------------------------------------------------------------------
            // Where instances can be placed, which is a property of the host rather than of anything
            // installed on it. Neutral: registering a disk and letting one go are both ordinary
            // administration, and an instance whose library was deregistered is still on disk.
            LibraryEvent<LibraryAddedData>("library.added"),
            LibraryEvent<LibraryRemovedData>("library.removed"),

            // -- accounts ----------------------------------------------------------------------
            // Signing in and accounts changing. No engine command runs for any of them: the auth anchor,
            // which holds the accounts, authors them. They are classified here because a payload field
            // nobody has classified renders nowhere, and these carry the values most worth care.
            Account<AuthSessionEventData>("auth.signed_in", EventOutcome.Success, SessionFields),
            Account<AuthSessionEventData>("auth.signed_out", EventOutcome.Neutral, SessionFields),

            Account<AuthSessionRevokedData>("auth.session.revoked", EventOutcome.Neutral,
                [UserId, Username, Field("Scope", FieldShape.Text), Sid, Field("Count", FieldShape.Number)]),

            // The one failed sign-in that is a fact rather than noise. It is emitted when the
            // lock begins and not on the attempts it goes on to refuse, so one run of guessing is one
            // row however long it runs.
            Account<AuthLockedOutData>("auth.locked_out", EventOutcome.Failure,
                [UserId, Username, Identity, Field("FailedCount", FieldShape.Number),
                 Field("Until", FieldShape.Timestamp)]),

            // A refused attempt at an administrator-only surface. The anchor coalesces a minute of the
            // same refusal into one line carrying a count. The address is where a scanner is read from,
            // so it is personal; the account is only named when the anchor knew who asked.
            new EventDescriptor("auth.admin.refused", EventSubject.Account, EventWeight.Fact,
                EventOutcome.Failure,
                [Field("Surface", FieldShape.Text), Field("Route", FieldShape.Text),
                 Field("Account", FieldShape.Identity),
                 Field("Address", FieldShape.Identity, FieldSensitivity.Personal),
                 Field("Count", FieldShape.Number)],
                typeof(AuthAdminRefusedData), Known: true),

            // An account's standing is only ever changed by whoever writes the account store, so these
            // are the whole record of an account arriving, being let in, switched off and removed.
            Account<UserAccountEventData>("user.provisioned", EventOutcome.Neutral, AccountChangeFields),
            Account<UserAccountEventData>("user.approved", EventOutcome.Success, AccountChangeFields),
            Account<UserAccountEventData>("user.disabled", EventOutcome.Neutral, AccountChangeFields),
            Account<UserAccountEventData>("user.deleted", EventOutcome.Neutral, AccountChangeFields),

            // Records that a credential was set and by whom. Never the credential.
            Account<UserAccountEventData>("user.password_changed", EventOutcome.Neutral, AccountChangeFields),

            Account<IdentityLinkEventData>("identity.linked", EventOutcome.Neutral, IdentityFields),
            Account<IdentityLinkEventData>("identity.unlinked", EventOutcome.Neutral, IdentityFields),

            // -- access ------------------------------------------------------------------------
            // Who may do what, written by the auth anchor alone. An assignment is about the account
            // holding it; a role, a permission, the catalog and a service's requirement are about the
            // model, because a change to one changes what everybody holding it may do.
            Account<AssignmentEventData>("auth.assignment.granted", EventOutcome.Neutral, AssignmentFields),
            Account<AssignmentEventData>("auth.assignment.revoked", EventOutcome.Neutral, AssignmentFields),
            AccessEvent<AuthorityRecordEventData>("auth.role.changed", AuthorityRecordFields),
            AccessEvent<AuthorityRecordEventData>("auth.role.removed", AuthorityRecordFields),
            AccessEvent<AuthorityRecordEventData>("auth.permission.changed", AuthorityRecordFields),
            AccessEvent<AuthorityRecordEventData>("auth.permission.removed", AuthorityRecordFields),
            AccessEvent<CatalogEventData>("auth.catalog.changed",
                [Field("Member", FieldShape.Text), Field("Added", FieldShape.Text), Field("Removed", FieldShape.Text), AuthorityVersion]),
            AccessEvent<ServiceRequirementEventData>("auth.service.requirement.approved", RequirementFields),
            AccessEvent<ServiceRequirementEventData>("auth.service.requirement.revoked", RequirementFields),

            // Applications are about the model too: a client registered, removed or given a new secret
            // changes who can be handed a token at all.
            AccessEvent<ApplicationEventData>("auth.application.changed", ApplicationFields),
            AccessEvent<ApplicationEventData>("auth.application.removed", ApplicationFields),
            AccessEvent<ApplicationEventData>("auth.application.client.removed", ApplicationFields),
            AccessEvent<ApplicationEventData>("auth.application.secret.rotated", ApplicationFields),

            // An application's client exchanging somebody's Discord credential for a token naming their
            // account. A refusal is a row per request: the client authenticated, so each one is a
            // decision about a real caller rather than noise at an open door.
            Account<TokenExchangeEventData>("auth.token.exchanged", EventOutcome.Success, TokenExchangeFields),
            Account<TokenExchangeEventData>("auth.token.exchange_refused", EventOutcome.Failure, TokenExchangeFields),

            // -- leaf services -----------------------------------------------------------------
            Service<ServiceProvisioningEventData>("service.connected", EventOutcome.Success, [Leaf, DisplayName]),
            Service<ServiceProvisioningEventData>("service.disconnected", EventOutcome.Neutral, [Leaf, DisplayName]),

            // Keys only. A leaf's configuration holds tokens and passwords, so the value a change set
            // is not part of the fact that it changed.
            Service<ServiceConfigChangedEventData>("service.config_changed", EventOutcome.Neutral,
                [Leaf, DisplayName, Field("Keys", FieldShape.Text), Field("Outcome", FieldShape.Text)]),

            Service<ServiceRestartedEventData>("service.restarted", EventOutcome.Neutral,
                [Leaf, DisplayName, Field("Unit", FieldShape.Text), Ok]),

            // -- what a leaf says about ITSELF --------------------------------------------------
            // Same subject as the four above and the opposite direction: those record what kgsm-api
            // did to a leaf on somebody's instruction, these are the leaf's own report. None of
            // them names a leaf, because the journal a line was read from already does — see
            // LeafLifecycleEventData.
            LeafEvent<LeafReadyEventData>(LeafLifecycleEvents.Ready, EventOutcome.Success,
                [StartupMs, Detail]),

            LeafEvent<LeafDegradedEventData>(LeafLifecycleEvents.Degraded, EventOutcome.Failure,
                [Component, Detail]),

            LeafEvent<LeafRecoveredEventData>(LeafLifecycleEvents.Recovered, EventOutcome.Success,
                [Component, DegradedForSec]),

            // Neutral, not Failure: a leaf saying it is going away on purpose is the fact that
            // separates a deploy from an outage, and reporting it as a failure would lose exactly the
            // distinction it exists to make.
            LeafEvent<LeafStoppingEventData>(LeafLifecycleEvents.Stopping, EventOutcome.Neutral,
                [Reason, UptimeSec]),

            // -- what the assistant says about its own conduct ----------------------------------
            // Service-subject: none of them is an event about a game server, because on every one of
            // them nothing happened to a server. Filing a refusal or an unapproved proposal under the
            // instance it names would say the opposite of what it records.
            AssistantEvent<AssistantClaimCorrectedEventData>(
                AssistantEvents.ClaimCorrected, EventOutcome.Failure,
                [Check, Resolution, Net, ConversationId]),

            AssistantEvent<AssistantActionDeclinedEventData>(
                AssistantEvents.ActionDeclined, EventOutcome.Failure,
                [Tool, DeclineReason, ActionInstance]),

            // Neutral: a proposal is the assistant doing exactly what it is meant to — stopping to ask.
            AssistantEvent<AssistantActionProposedEventData>(
                AssistantEvents.ActionProposed, EventOutcome.Neutral,
                [Kind, Tool, ActionInstance, ExpiresInSec]),

            // -- what the reactor judged -------------------------------------------------------
            // Service-subject for the same reason the assistant's are: a decision is something this
            // host NOTICED, not something that happened to the server it names. Its severity is the
            // RULE's, carried in the payload, so a consumer reads it from there rather than from the
            // descriptor — which classifies the family and cannot know which rule spoke.
            ReactorEvent<ReactorDecidedEventData>(
                ReactorEvents.Decided, EventOutcome.Neutral, EventSeverity.Info,
                [
                    RuleField, RuleAuthor, ReactorSubject, ReactorSubjectKind, SeverityField,
                    ReactorMode, ReactorOutcome, ReactorReason, ReactorAction, ReactorActionInstance,
                    DecisionId, OpenedAt, .. SourceFields,
                ]),

            // An offer, and nothing has been done. Neutral for that reason: a proposal that is never
            // answered is the ordinary case, not a failure, and an outcome here would make a surface
            // colour a question as though it were a result.
            ReactorEvent<ReactorProposedEventData>(
                ReactorEvents.Proposed, EventOutcome.Neutral, EventSeverity.Info,
                [
                    RuleField, RuleAuthor, ReactorSubject, ReactorSubjectKind, SeverityField,
                    ReactorReason, ReactorAction, ReactorActionInstance, DecisionId,
                    ReactorHandle, ExpiresAt, OpenedAt,
                ]),

            // Neutral as a family, because the four resolutions are not one outcome: a dismissal is a
            // person working as intended and a failed confirm is not, and the descriptor cannot know
            // which line it is classifying. A consumer reads `Resolution` and `Ok`.
            ReactorEvent<ReactorResolvedEventData>(
                ReactorEvents.Resolved, EventOutcome.Neutral, EventSeverity.Info,
                [RuleField, ReactorSubject, ReactorAction, ReactorActionInstance, DecisionId,
                 ReactorHandle, ReactorResolution, AnsweredBy, OkField, Artifact, ReactorDetail]),

            // The one reactor event that is an audit row with no person in it at all: something was
            // performed on nobody's request. Its outcome is the ACTION's, which is why it is not
            // neutral — `Ok` false is a complete fact and not an absence.
            ReactorEvent<ReactorActedEventData>(
                ReactorEvents.Acted, EventOutcome.Success, EventSeverity.Warn,
                [RuleField, ReactorSubject, ReactorAction, ReactorActionInstance, DecisionId,
                 OkField, Artifact, ReactorDetail]),

            // Blueprint-subject, and the only pair here that brackets rather than reports: the engine
            // records the probe install and uninstall in full, and these say the rows belong together.
            new(AssistantEvents.BlueprintAuthoringStarted, EventSubject.Blueprint, EventWeight.Phase,
                EventOutcome.Neutral, [Probe],
                typeof(AssistantBlueprintAuthoringStartedEventData), Known: true),

            new(AssistantEvents.BlueprintAuthored, EventSubject.Blueprint, EventWeight.Fact,
                EventOutcome.Neutral, [Probe, AuthoringOutcome, DurationSec],
                typeof(AssistantBlueprintAuthoredEventData), Known: true),

            // -- panel actions on an instance --------------------------------------------------
            // Instance-subject because that is what they are about, even though the Control Panel and
            // not the engine performed them. Both carry an identity of the bytes and never the bytes:
            // an instance config file holds rcon passwords, and a world is somebody's data.
            Instance<FileWrittenEventData>("file.written", EventWeight.Fact, EventOutcome.Neutral,
                [Path, SizeBytes, Sha256]),
            Instance<BackupDownloadedEventData>("backup.downloaded", EventWeight.Fact, EventOutcome.Neutral,
                [Field("BackupId", FieldShape.Text), SizeBytes, Sha256]),

            // A transfer between nodes, as the API journals it: only the API sees both nodes. Each side
            // writes its own half. The started line is named by the source's instance, because the server
            // has no id on the arriving node yet, and a target's failed line leaves InstanceName empty
            // for the same reason; neither payload may be read as naming a local server then.
            new EventDescriptor("server.transfer.started", EventSubject.Instance, EventWeight.Fact,
                EventOutcome.Neutral,
                [TransferId, Field("SourceMember", FieldShape.Text), Field("SourceInstance", FieldShape.Text)],
                typeof(TransferStartedEventData), Known: true),
            Instance<TransferCompletedEventData>("server.transfer.completed", EventWeight.Fact,
                EventOutcome.Success,
                [TransferId, Field("TargetMember", FieldShape.Text), Field("TargetInstance", FieldShape.Text)]),
            Instance<TransferFailedEventData>("server.transfer.failed", EventWeight.Fact,
                EventOutcome.Failure,
                [TransferId, Field("SourceInstance", FieldShape.Text), Field("Reason", FieldShape.Text),
                 Field("Phase", FieldShape.Text)]),
        };

        var byType = new Dictionary<string, EventDescriptor>(all.Count, StringComparer.Ordinal);
        foreach (EventDescriptor descriptor in all)
            byType.Add(descriptor.Type, descriptor);

        return byType;
    }

    /// <summary>
    /// One instance-subject event, named together with the class its payload deserializes into.
    /// </summary>
    /// <remarks>
    /// <b>The type parameter is what makes the classification and the dispatch one registry.</b>
    /// <see cref="Services.EventService"/> reads <see cref="EventDescriptor.PayloadType"/> to decide
    /// what to deserialize, so an event cannot be dispatched without being described — that is now
    /// true by construction rather than by a test comparing two lists. Its constraint carries a second
    /// guarantee for free: an instance-subject event must have an instance-shaped payload, so the
    /// subject and the payload's own base cannot disagree.
    /// </remarks>
    private static EventDescriptor Instance<TData>(
        string type,
        EventWeight weight,
        EventOutcome outcome = EventOutcome.Neutral,
        IReadOnlyList<EventField>? fields = null,
        EventSeverity severity = EventSeverity.Info)
        where TData : EventDataBase =>
        new(type, EventSubject.Instance, weight, outcome, fields ?? [], typeof(TData), Known: true,
            Severity: severity);

    /// <summary>The blueprint-subject counterpart, constrained to the sibling payload base.</summary>
    /// <summary>A host-scoped descriptor — a fact this machine's own monitoring established.</summary>
    private static EventDescriptor Host<TData>(
        string type,
        EventOutcome outcome,
        IReadOnlyList<EventField> fields)
        where TData : HostThresholdEventDataBase =>
        new(type, EventSubject.Host, EventWeight.Fact, outcome, fields, typeof(TData), Known: true);

    private static EventDescriptor BlueprintEvent<TData>(string type, IReadOnlyList<EventField> fields)
        where TData : BlueprintEventDataBase =>
        new(type, EventSubject.Blueprint, EventWeight.Fact, EventOutcome.Neutral, fields,
            typeof(TData), Known: true);

    /// <summary>
    /// A library-subject descriptor — something that happened to a placement root.
    /// </summary>
    /// <remarks>
    /// The path is on the payload rather than in the field list on purpose: it is the library's
    /// identity, not a detail rendered beside its name, and both events carry exactly the same two.
    /// </remarks>
    private static EventDescriptor LibraryEvent<TData>(string type)
        where TData : LibraryEventDataBase =>
        new(type, EventSubject.Library, EventWeight.Fact, EventOutcome.Neutral,
            [Field("Path", FieldShape.Text)], typeof(TData), Known: true);

    /// <summary>An account-subject descriptor — something that happened to somebody's access.</summary>
    private static EventDescriptor Account<TData>(
        string type, EventOutcome outcome, IReadOnlyList<EventField> fields)
        where TData : AccountEventDataBase =>
        new(type, EventSubject.Account, EventWeight.Fact, outcome, fields, typeof(TData), Known: true);

    /// <summary>A descriptor for a change to the access model itself.</summary>
    private static EventDescriptor AccessEvent<TData>(string type, IReadOnlyList<EventField> fields)
        where TData : KgsmEventDataBase =>
        new(type, EventSubject.Access, EventWeight.Fact, EventOutcome.Neutral, fields, typeof(TData), Known: true);

    /// <summary>A leaf-service descriptor.</summary>
    private static EventDescriptor Service<TData>(
        string type, EventOutcome outcome, IReadOnlyList<EventField> fields)
        where TData : ServiceEventData =>
        new(type, EventSubject.Service, EventWeight.Fact, outcome, fields, typeof(TData), Known: true);

    /// <summary>
    /// A descriptor for a leaf's report about itself.
    /// </summary>
    /// <remarks>
    /// The same subject as <see cref="Service{TData}"/> — it is still an event about a leaf service —
    /// with the payload constraint that keeps the two apart. A self-reported event must not carry a
    /// leaf id, and <see cref="ServiceEventData"/> requires one.
    /// </remarks>
    private static EventDescriptor LeafEvent<TData>(
        string type, EventOutcome outcome, IReadOnlyList<EventField> fields)
        where TData : LeafLifecycleEventData =>
        new(type, EventSubject.Service, EventWeight.Fact, outcome, fields, typeof(TData), Known: true);

    /// <summary>
    /// A descriptor for the assistant's report about its own conduct.
    /// </summary>
    /// <remarks>
    /// Service-subject, and constrained to a payload that cannot name a leaf, for the same reason
    /// <see cref="LeafEvent{TData}"/> is: the journal directory already says who produced it.
    /// </remarks>
    private static EventDescriptor AssistantEvent<TData>(
        string type, EventOutcome outcome, IReadOnlyList<EventField> fields)
        where TData : AssistantEventData =>
        new(type, EventSubject.Service, EventWeight.Fact, outcome, fields, typeof(TData), Known: true);

    /// <summary>
    /// A descriptor for the reactor's report about what it judged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Service-subject, and constrained to a payload that cannot name a leaf, for the same reason
    /// <see cref="AssistantEvent{TData}"/> is: the journal directory already says who produced it.
    /// </para>
    /// <para>
    /// <b>Not instance-subject, even though most decisions name a server.</b> A decision is
    /// something this host <em>noticed</em> about a server rather than something that happened to one,
    /// and the subject is not always a server at all — a threshold episode is about a sensor. A
    /// consumer that filed these under the instance would put a judgment in the same list as the
    /// events it was judging.
    /// </para>
    /// </remarks>
    private static EventDescriptor ReactorEvent<TData>(
        string type, EventOutcome outcome, EventSeverity severity, IReadOnlyList<EventField> fields)
        where TData : ReactorEventData =>
        new(type, EventSubject.Service, EventWeight.Fact, outcome, fields, typeof(TData), Known: true,
            Severity: severity);

    private static EventField Field(
        string name, FieldShape shape, FieldSensitivity sensitivity = FieldSensitivity.Public) =>
        new(name, sensitivity, shape);

    // The fields that appear on more than one event, declared once so two events cannot classify the
    // same field differently.
    private static readonly EventField Blueprint = Field("Blueprint", FieldShape.Text);

    /// <summary>
    /// The placement root a payload names, as its registry name. Not the same field as the library
    /// events' own <c>LibraryName</c>, which is their subject rather than a detail on one.
    /// </summary>
    private static readonly EventField LibraryName = Field("Library", FieldShape.Text);

    private static readonly EventField Source = Field("Source", FieldShape.Text);
    private static readonly EventField Version = Field("Version", FieldShape.Version);
    private static readonly EventField ExitCode = Field("ExitCode", FieldShape.Text);
    private static readonly EventField Restarts = Field("Restarts", FieldShape.Number);
    private static readonly EventField Ports = Field("Ports", FieldShape.Ports);
    private static readonly EventField Tier = Field("Tier", FieldShape.Text);
    private static readonly EventField Runtime = Field("Runtime", FieldShape.Text);
    private static readonly EventField OverridesSystem = Field("OverridesSystem", FieldShape.Text);

    /// <summary>
    /// The fields a leaf's own lifecycle events carry, named from the emitter's constants rather than
    /// from a string here. Three descriptions of one field — what the leaf writes, what the payload
    /// class binds to, and what this classifies — are only ever the same field if they are the same
    /// string.
    /// </summary>
    private static readonly EventField StartupMs = Field(LeafLifecycleFields.StartupMs, FieldShape.Number);
    private static readonly EventField Detail = Field(LeafLifecycleFields.Detail, FieldShape.Text);
    private static readonly EventField Component = Field(LeafLifecycleFields.Component, FieldShape.Text);
    private static readonly EventField DegradedForSec = Field(LeafLifecycleFields.DegradedForSec, FieldShape.Number);
    private static readonly EventField Reason = Field(LeafLifecycleFields.Reason, FieldShape.Text);
    private static readonly EventField UptimeSec = Field(LeafLifecycleFields.UptimeSec, FieldShape.Number);

    /// <summary>
    /// The fields the assistant's own events carry, named from the contract's constants for the same
    /// reason the lifecycle ones are — three descriptions of one field are only the same field if they
    /// are the same string.
    /// </summary>
    private static readonly EventField Check = Field(AssistantEventFields.Check, FieldShape.Text);
    private static readonly EventField Resolution = Field(AssistantEventFields.Resolution, FieldShape.Text);
    private static readonly EventField Net = Field(AssistantEventFields.Net, FieldShape.Text);
    private static readonly EventField Tool = Field(AssistantEventFields.Tool, FieldShape.Text);
    private static readonly EventField DeclineReason = Field(AssistantEventFields.DeclineReason, FieldShape.Text);
    private static readonly EventField ActionInstance = Field(AssistantEventFields.Instance, FieldShape.Text);
    private static readonly EventField Kind = Field(AssistantEventFields.Kind, FieldShape.Text);
    private static readonly EventField ExpiresInSec = Field(AssistantEventFields.ExpiresInSec, FieldShape.Number);
    private static readonly EventField Probe = Field(AssistantEventFields.Probe, FieldShape.Text);
    private static readonly EventField AuthoringOutcome =
        Field(AssistantEventFields.AuthoringOutcome, FieldShape.Text);
    private static readonly EventField DurationSec = Field(AssistantEventFields.DurationSec, FieldShape.Number);

    /// <summary>
    /// The conversation a claim was corrected in. <see cref="FieldShape.Opaque"/> because it means
    /// nothing to a reader, and <see cref="FieldSensitivity.Personal"/> because the key embeds the
    /// account it belongs to — it is a correlation token that happens to name somebody.
    /// </summary>
    private static readonly EventField ConversationId =
        Field(AssistantEventFields.ConversationId, FieldShape.Opaque, FieldSensitivity.Personal);

    /// <summary>
    /// The name and id a game shows other players in its own scoreboard. Public because that is what
    /// they already are — a roster that refused them would answer nobody's question.
    /// </summary>
    private static readonly EventField PlayerName = Field("PlayerName", FieldShape.Identity);
    private static readonly EventField PlayerId = Field("PlayerId", FieldShape.Identity);

    /// <summary>
    /// Where somebody connected from. It identifies a person rather than a player, and the game shows
    /// it to nobody.
    /// </summary>
    private static readonly EventField PlayerAddr =
        Field("PlayerAddr", FieldShape.Identity, FieldSensitivity.Personal);

    /// <summary>
    /// The supervisor's correlation token for one session. Public — it says nothing about anybody —
    /// but <see cref="FieldShape.Opaque"/>, so nothing renders it for want of meaning rather than for
    /// privacy.
    /// </summary>
    private static readonly EventField SessionKey = Field("SessionKey", FieldShape.Opaque);

    /// <summary>
    /// Who a moderation action named. <see cref="FieldSensitivity.Conditional"/> because the value may
    /// be an address, a name or an id and <em>the event does not say which</em> — the game's blueprint
    /// declares it. This library already refuses to classify that here, because re-deriving it would be
    /// a second answer that could disagree with the template; the catalog refuses for the same reason
    /// rather than guessing on its behalf.
    /// </summary>
    private static readonly EventField Target =
        Field("Target", FieldShape.Identity, FieldSensitivity.Conditional);

    /// <summary>
    /// The console command that was delivered. Powerful by nature — a command can create an
    /// operator, set a password or carry a token — so it is privileged rather than public, whatever
    /// the particular command turns out to be.
    /// </summary>
    private static readonly EventField Command =
        Field("Command", FieldShape.Text, FieldSensitivity.Privileged);

    // ---- the Control Panel's own fields --------------------------------------------------------

    /// <summary>
    /// The account's stable id. Public and <see cref="FieldShape.Opaque"/>: it is a generated key that
    /// says nothing about anybody on its own, and nothing should render it for meaning.
    /// </summary>
    private static readonly EventField UserId = Field("UserId", FieldShape.Opaque);

    /// <summary>
    /// What the account was called when this happened. Public — it is already the name every audit row
    /// carries as its actor, and a trail that hid it would name nobody.
    /// </summary>
    private static readonly EventField Username = Field("Username", FieldShape.Identity);

    /// <summary>
    /// The external handle an account signs in with, as <c>provider:name</c>.
    /// <see cref="FieldSensitivity.Personal"/>: unlike the username, it links this host's account to a
    /// person's identity somewhere else, and it is the account's own holder who chose to attach it —
    /// not something the panel gets to publish to everyone who can read the log.
    /// </summary>
    private static readonly EventField Identity =
        Field("Identity", FieldShape.Identity, FieldSensitivity.Personal);

    /// <summary>Which provider vouched. Public — naming the door is not naming who came through it.</summary>
    private static readonly EventField Provider = Field("Provider", FieldShape.Text);

    /// <summary>
    /// The session id, so a login and its logout pair up. Public and opaque, on the same reasoning as
    /// <see cref="SessionKey"/> — it correlates two rows and grants nothing.
    /// </summary>
    private static readonly EventField Sid = Field("Sid", FieldShape.Opaque);

    /// <summary>
    /// The calling device. <see cref="FieldSensitivity.Personal"/> for the same reason
    /// <see cref="PlayerAddr"/> is: a user-agent string describes somebody's machine, and it is on the
    /// row to answer "was that me?" for the account's holder, not to tell a reader what everyone else
    /// browses with.
    /// </summary>
    private static readonly EventField UserAgent =
        Field("UserAgent", FieldShape.Text, FieldSensitivity.Personal);

    private static readonly EventField Leaf = Field("Leaf", FieldShape.Text);
    private static readonly EventField DisplayName = Field("DisplayName", FieldShape.Text);
    private static readonly EventField Ok = Field("Ok", FieldShape.Text);
    private static readonly EventField Path = Field("Path", FieldShape.Text);
    private static readonly EventField SizeBytes = Field("SizeBytes", FieldShape.Number);

    /// <summary>The content hash. Identifies the bytes; is not the bytes.</summary>
    private static readonly EventField Sha256 = Field("Sha256", FieldShape.Opaque);
    private static readonly EventField TransferId = Field("TransferId", FieldShape.Opaque);

    /// <summary>The fields every <c>auth.*</c> session event carries.</summary>
    private static readonly EventField[] SessionFields =
        [UserId, Username, Identity, Provider, Sid, UserAgent];

    /// <summary>The fields every <c>user.*</c> account-change event carries.</summary>
    private static readonly EventField[] AccountChangeFields =
    [
        UserId, Username,
        Field("FromStatus", FieldShape.Text), Field("ToStatus", FieldShape.Text),
        Field("ByHolder", FieldShape.Text),
    ];

    private static readonly EventField AuthorityVersion = Field("AuthorityVersion", FieldShape.Number);

    /// <summary>The fields both <c>auth.assignment.*</c> events carry.</summary>
    private static readonly EventField[] AssignmentFields =
    [
        UserId, Username, Field("AssignmentId", FieldShape.Opaque), Field("RoleId", FieldShape.Opaque),
        Field("Role", FieldShape.Text), Field("Scope", FieldShape.Text), AuthorityVersion,
    ];

    /// <summary>The fields every role and permission event carries.</summary>
    private static readonly EventField[] AuthorityRecordFields =
        [Field("Id", FieldShape.Opaque), Field("Name", FieldShape.Text), AuthorityVersion];

    /// <summary>The fields both service requirement events carry.</summary>
    private static readonly EventField[] RequirementFields =
    [
        Field("AccountId", FieldShape.Opaque), Field("Service", FieldShape.Text), Field("Action", FieldShape.Text),
        Field("Scope", FieldShape.Text), Field("Automatic", FieldShape.Text), AuthorityVersion,
    ];

    /// <summary>The fields every <c>auth.application.*</c> event carries.</summary>
    private static readonly EventField[] ApplicationFields =
        [Field("Id", FieldShape.Opaque), Field("Name", FieldShape.Text), Field("Client", FieldShape.Text)];

    /// <summary>The fields both <c>auth.token.*</c> exchange events carry.</summary>
    private static readonly EventField[] TokenExchangeFields =
    [
        UserId, Username, Identity, Field("Client", FieldShape.Text), Field("Application", FieldShape.Text),
        Field("ActedBy", FieldShape.Text), Field("Reason", FieldShape.Text),
    ];

    /// <summary>The fields both <c>identity_*</c> events carry.</summary>
    private static readonly EventField[] IdentityFields =
        [UserId, Username, Provider, Field("Handle", FieldShape.Identity, FieldSensitivity.Personal)];

    // -- the reactor's judgments ------------------------------------------------------------------

    private static readonly EventField RuleField =
        Field(ReactorEventFields.Rule, FieldShape.Text);

    /// <summary>
    /// Who shaped the rule. A KGSM username, so it names a natural person rather than a player.
    /// </summary>
    /// <remarks>
    /// The one personal field the reactor writes, and the reason it is classified rather than left
    /// public: a surface listing decisions to a room of players would otherwise print the operator who
    /// wrote each rule beside it.
    /// </remarks>
    private static readonly EventField RuleAuthor =
        Field(ReactorEventFields.RuleAuthor, FieldShape.Identity, FieldSensitivity.Personal);

    private static readonly EventField ReactorSubject =
        Field(ReactorEventFields.Subject, FieldShape.Text);

    private static readonly EventField ReactorSubjectKind =
        Field(ReactorEventFields.SubjectKind, FieldShape.Text);

    private static readonly EventField ReactorMode =
        Field(ReactorEventFields.Mode, FieldShape.Text);

    private static readonly EventField ReactorOutcome =
        Field(ReactorEventFields.Outcome, FieldShape.Text);

    /// <summary>Why the rule concluded what it did, carrying the figures it rests on.</summary>
    private static readonly EventField ReactorReason =
        Field(ReactorEventFields.Reason, FieldShape.Text);

    private static readonly EventField ReactorAction =
        Field(ReactorEventFields.Action, FieldShape.Text);

    /// <summary>
    /// The server the action operates on. Its own field rather than the assistant's, because the two
    /// are spelled differently on the wire and a shared definition would classify a name nothing writes.
    /// </summary>
    private static readonly EventField ReactorActionInstance =
        Field(ReactorEventFields.ActionInstance, FieldShape.Text);

    /// <summary>How loudly the rule speaks. The RULE's, not the family's.</summary>
    private static readonly EventField SeverityField =
        Field(ReactorEventFields.Severity, FieldShape.Text);

    private static readonly EventField OkField = Field(ReactorEventFields.Ok, FieldShape.Text);

    /// <summary>What the action produced — a backup id. Identifies the artifact; is not the artifact.</summary>
    private static readonly EventField Artifact = Field(ReactorEventFields.Artifact, FieldShape.Opaque);

    /// <summary>What went wrong, or what else is worth reading.</summary>
    /// <remarks>
    /// Its own definition rather than the leaf-lifecycle one it shares a name with: the two are the
    /// same string today and are separate contracts, and one field standing for both would classify a
    /// name whichever producer moved first.
    /// </remarks>
    private static readonly EventField ReactorDetail =
        Field(ReactorEventFields.Detail, FieldShape.Text);

    /// <summary>
    /// The token a staged proposal is redeemed with.
    /// </summary>
    /// <remarks>
    /// <b>Opaque because presenting it is how a proposal gets confirmed.</b> It carries no meaning to
    /// render, and a surface that printed it into a channel a fleet reads would be publishing the one
    /// string that lets somebody ask for the action.
    /// </remarks>
    private static readonly EventField ReactorHandle =
        Field(ReactorEventFields.ProposalHandle, FieldShape.Opaque);

    /// <summary>When an unanswered proposal stops being redeemable.</summary>
    private static readonly EventField ExpiresAt =
        Field(ReactorEventFields.ExpiresAt, FieldShape.Timestamp);

    /// <summary>
    /// How a proposal ended. Its own definition rather than the assistant's identically-named field,
    /// for the reason <see cref="ReactorDetail"/> is: two contracts that agree on a spelling today are
    /// still two contracts.
    /// </summary>
    private static readonly EventField ReactorResolution =
        Field(ReactorEventFields.Resolution, FieldShape.Text);

    /// <summary>
    /// Who answered a proposal. A natural person, which is what separates a confirmed action from
    /// <c>reactor.acted</c>'s autonomous one.
    /// </summary>
    private static readonly EventField AnsweredBy =
        Field(ReactorEventFields.AnsweredBy, FieldShape.Identity, FieldSensitivity.Personal);

    private static readonly EventField DecisionId =
        Field(ReactorEventFields.DecisionId, FieldShape.Opaque);

    private static readonly EventField OpenedAt =
        Field(ReactorEventFields.OpenedAt, FieldShape.Timestamp);

    /// <summary>Where the line the decision was made from lives — a pointer, not a reading.</summary>
    private static readonly EventField[] SourceFields =
    [
        Field(ReactorEventFields.SourceProducer, FieldShape.Text),
        Field(ReactorEventFields.SourceSegment, FieldShape.Text),
        Field(ReactorEventFields.SourceOffset, FieldShape.Number),
        Field(ReactorEventFields.SourceEventId, FieldShape.Opaque),
    ];
}

/// <summary>
/// What is known about one engine event type.
/// </summary>
/// <param name="Type">The raw engine event type, e.g. <c>instance_started</c>.</param>
/// <param name="Subject">What the event is about.</param>
/// <param name="Weight">Whether it reports a fact or a step inside one.</param>
/// <param name="Outcome">Whether it reports something completing or failing.</param>
/// <param name="Fields">
/// The payload's own fields and what each holds. <b>Empty means "render nothing from the payload"</b>,
/// which is not the same as the payload being empty — an event whose fields nobody has classified may
/// carry anything.
/// </param>
/// <param name="PayloadType">
/// The class this event's <c>Data</c> deserializes into, and the reason the catalog is the <em>only</em>
/// registry of what the engine emits: <see cref="Services.EventService"/> dispatches off this, so an
/// event that can be deserialized is necessarily one that has been classified. Null exactly when
/// <paramref name="Known"/> is false — nothing can be deserialized for a type this build has never
/// heard of, which is why an unknown event reaches a handler only as a raw envelope.
/// </param>
/// <param name="Known">
/// Whether this build classifies the type at all. On <see langword="false"/> the only load-bearing
/// values are <paramref name="Type"/> and the empty <paramref name="Fields"/>; the subject is read off
/// the engine's naming convention and the rest are cautious defaults. An unknown type must still be
/// shown — dropping it is how a surface reports a busy night as a quiet one.
/// </param>
public sealed record EventDescriptor(
    string Type,
    EventSubject Subject,
    EventWeight Weight,
    EventOutcome Outcome,
    IReadOnlyList<EventField> Fields,
    System.Type? PayloadType,
    bool Known,
    EventSeverity Severity = EventSeverity.Info)
{
    /// <summary>The classification of <paramref name="name"/>, or null if the event has no such field.</summary>
    public EventField? Field(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
}

/// <summary>One payload field: what it is called, what kind of thing it holds, and how careful to be with it.</summary>
/// <param name="Name">The field's name on the event's data class and in its JSON payload.</param>
/// <param name="Sensitivity">What kind of data it is. Never who may see it — that is a surface's decision.</param>
/// <param name="Shape">What it looks like, so a renderer knows whether it can be printed as a scalar.</param>
public sealed record EventField(string Name, FieldSensitivity Sensitivity, FieldShape Shape);

/// <summary>What an event is about.</summary>
public enum EventSubject
{
    /// <summary>One game server instance; the payload names it.</summary>
    Instance,

    /// <summary>One blueprint — a template, not an installed server. Never read as being about an instance.</summary>
    Blueprint,

    /// <summary>
    /// The host itself — a fact this machine's monitoring established, which may name a server it is
    /// about without being an event that server produced.
    /// </summary>
    Host,

    /// <summary>
    /// One library — a named root instances are placed in. Never read as being about an instance: a
    /// library is registered before anything lives in it and survives every instance leaving it.
    /// </summary>
    Library,

    /// <summary>
    /// One KGSM account — who signed in, whose authority changed, which identity was attached. The
    /// subject is the account rather than the person, because an account is what authority resolves
    /// against and it outlives any name somebody is currently shown under.
    /// </summary>
    Account,

    /// <summary>
    /// One leaf service on this host — connected, disconnected, reconfigured, restarted. Never read as
    /// being about a game server: a leaf can be reconfigured while every instance keeps running.
    /// </summary>
    Service,

    /// <summary>
    /// The cluster's access model itself — a role, a permission, the catalog of what can be granted, a
    /// service's requirement. Never read as being about one account: a permission changing changes what
    /// everybody holding it may do.
    /// </summary>
    Access,
}

/// <summary>
/// Whether an event is the news or part of getting to it.
/// </summary>
public enum EventWeight
{
    /// <summary>Something happened that stands on its own.</summary>
    Fact,

    /// <summary>
    /// A step inside a multi-step operation that has its own <see cref="Fact"/> event — the brackets
    /// around an install, a stop, an update. A surface that lists everything reads as noise; a surface
    /// tracking progress needs exactly these. Which is why this classifies rather than prescribes.
    /// </summary>
    Phase,
}

/// <summary>
/// What kind of data a field holds. A statement about the data, never about who may see it.
/// </summary>
public enum FieldSensitivity
{
    /// <summary>Safe wherever events are read at all.</summary>
    Public,

    /// <summary>
    /// May identify a person, depending on something the event does not carry. A consumer that cannot
    /// resolve which it is must treat it as <see cref="Personal"/>.
    /// </summary>
    Conditional,

    /// <summary>Identifies a natural person rather than a player.</summary>
    Personal,

    /// <summary>Operator-level content that may carry a credential.</summary>
    Privileged,
}

/// <summary>What a field looks like, so a renderer knows what it can do with it.</summary>
public enum FieldShape
{
    /// <summary>A short string fit to print.</summary>
    Text,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A game or build version string.</summary>
    Version,

    /// <summary>Something that names a player.</summary>
    Identity,

    /// <summary>The canonical port list — structured, and never flattened by a generic renderer.</summary>
    Ports,

    /// <summary>A moment in time.</summary>
    Timestamp,

    /// <summary>A token with no meaning to a reader. Carried for correlation, never displayed.</summary>
    Opaque,
}

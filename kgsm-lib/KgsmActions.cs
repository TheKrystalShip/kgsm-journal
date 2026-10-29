namespace TheKrystalShip.KGSM;

/// <summary>
/// The engine's actions, by id: what a caller checks before it calls the engine through this library,
/// and what a component names in <c>[Requires]</c> when it calls the engine as its own service account.
/// </summary>
/// <remarks>
/// <para>
/// The engine performs no authorization of its own; whoever calls it checks the person or service on
/// whose behalf it does. These ids are the engine's manifest (<c>kgsm/deploy/kgsm.actions.json</c>),
/// mirrored so a caller names a constant rather than spelling a string, and <c>KgsmActionsTests</c>
/// fails when the two disagree in either direction.
/// </para>
/// <para>
/// An id is immutable in meaning. An operation that starts doing something else is a new action, in the
/// manifest and here in the same change.
/// </para>
/// </remarks>
public static class KgsmActions
{
    /// <summary>The engine's action namespace.</summary>
    public const string Component = "kgsm";

    // ── Servers ───────────────────────────────────────────────────────────────

    /// <summary>See a server: its info, status and versions. Instance scope.</summary>
    public const string ServerRead = "kgsm:server.read";

    /// <summary>Start a server. Instance scope.</summary>
    public const string ServerStart = "kgsm:server.start";

    /// <summary>Stop a server. Instance scope.</summary>
    public const string ServerStop = "kgsm:server.stop";

    /// <summary>Restart a server. Instance scope.</summary>
    public const string ServerRestart = "kgsm:server.restart";

    /// <summary>Update a server to the latest version. Instance scope.</summary>
    public const string ServerUpdate = "kgsm:server.update";

    /// <summary>Install a server on a node. Node scope.</summary>
    public const string ServerInstall = "kgsm:server.install";

    /// <summary>Uninstall a server. Instance scope.</summary>
    public const string ServerUninstall = "kgsm:server.uninstall";

    /// <summary>Move a server to another library. Instance scope.</summary>
    public const string ServerMove = "kgsm:server.move";

    /// <summary>Transfer a server out of this node to another: export, commit and unlock. Instance scope.</summary>
    public const string ServerExport = "kgsm:server.export";

    /// <summary>Transfer servers into this node: check, begin, receive parts, finish and abort. Node scope.</summary>
    public const string ServerImport = "kgsm:server.import";

    /// <summary>Read a server's console and logs. Instance scope.</summary>
    public const string ServerConsoleRead = "kgsm:server.console.read";

    /// <summary>Send a server console input, and ask it to save. Instance scope.</summary>
    public const string ServerConsoleWrite = "kgsm:server.console.write";

    /// <summary>Announce a message to a server's players. Instance scope.</summary>
    public const string ServerAnnounce = "kgsm:server.announce";

    /// <summary>Kick a player. Instance scope.</summary>
    public const string ServerPlayersKick = "kgsm:server.players.kick";

    /// <summary>Ban and unban players. Instance scope.</summary>
    public const string ServerPlayersBan = "kgsm:server.players.ban";

    /// <summary>Read a server's settings. Instance scope.</summary>
    public const string ServerConfigRead = "kgsm:server.config.read";

    /// <summary>Change a server's settings, display name and note. Instance scope.</summary>
    public const string ServerConfigWrite = "kgsm:server.config.write";

    /// <summary>Change a server's maintenance windows. Instance scope.</summary>
    public const string ServerWindowsWrite = "kgsm:server.windows.write";

    /// <summary>See a server's backups. Instance scope.</summary>
    public const string ServerBackupsRead = "kgsm:server.backups.read";

    /// <summary>Back a server up. Instance scope.</summary>
    public const string ServerBackupsCreate = "kgsm:server.backups.create";

    /// <summary>Restore a server from a backup. Instance scope.</summary>
    public const string ServerBackupsRestore = "kgsm:server.backups.restore";

    /// <summary>Pin, unpin, prune and delete a server's backups. Instance scope.</summary>
    public const string ServerBackupsManage = "kgsm:server.backups.manage";

    /// <summary>Read a server's files. Instance scope.</summary>
    public const string ServerFilesRead = "kgsm:server.files.read";

    /// <summary>Write, rename and delete a server's files. Instance scope.</summary>
    public const string ServerFilesWrite = "kgsm:server.files.write";

    // ── The node's engine ─────────────────────────────────────────────────────

    /// <summary>Create, write and remove custom blueprints. Node scope.</summary>
    public const string BlueprintsWrite = "kgsm:blueprints.write";

    /// <summary>Add, rename and remove libraries. Node scope.</summary>
    public const string LibrariesManage = "kgsm:libraries.manage";

    /// <summary>Read the engine's settings. Node scope.</summary>
    public const string EngineConfigRead = "kgsm:engine.config.read";

    /// <summary>Change the engine's settings. Node scope.</summary>
    public const string EngineConfigWrite = "kgsm:engine.config.write";

    // ── The cluster ───────────────────────────────────────────────────────────

    /// <summary>See the blueprint catalog. Cluster scope.</summary>
    public const string LibraryRead = "kgsm:library.read";
}

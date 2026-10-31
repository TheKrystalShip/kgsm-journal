namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Whether a journal can be reached by the readers that have to read it.
/// </summary>
/// <remarks>
/// <para>
/// A producer writes its journal under its own state directory, and every other component on the host
/// reads it from there. Those components may run as different service accounts — the ecosystem's
/// units name a shared <c>kgsm</c> group for exactly that reason — so a state directory that grants
/// the group no access makes the journal inside it unreachable however permissive the journal's own
/// mode is: a directory cannot be entered without execute on every directory above it.
/// </para>
/// <para>
/// <b>And the result is silence, not an error.</b> A reader that cannot traverse into the state
/// directory does not get a permission failure it can report — <c>Directory.Exists</c> answers false,
/// so discovery concludes the producer has no journal, which is exactly what a leaf that has recorded
/// nothing looks like. There is no reading of the host that distinguishes them. This is the check
/// that turns that into something said out loud, at the only moment anything is in a position to
/// notice.
/// </para>
/// </remarks>
public static class JournalAccess
{
    /// <summary>
    /// Why readers running as another account will not find this journal, or null when they will.
    /// </summary>
    /// <remarks>
    /// Only the group bit is examined, and deliberately: the ecosystem's answer to cross-account
    /// reads is a shared group, not world access. A state directory holds more than the journal — an
    /// API's session store and an assistant's conversation history sit beside it — so "make it
    /// world-readable" is not the remedy and is not suggested.
    /// </remarks>
    /// <param name="journalDirectory">The producer's journal directory.</param>
    /// <returns>A description of the problem, or null when the journal is reachable.</returns>
    public static string? DescribeUnreachable(string? journalDirectory)
    {
        if (string.IsNullOrWhiteSpace(journalDirectory))
            return null;

        string? stateDirectory = Path.GetDirectoryName(
            journalDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(stateDirectory))
            return null;

        try
        {
            // Group access is a Unix mode; a platform without one has nothing to report.
            if (OperatingSystem.IsWindows() || !Directory.Exists(stateDirectory))
                return null;

            UnixFileMode mode = File.GetUnixFileMode(stateDirectory);

            if ((mode & UnixFileMode.GroupExecute) != 0)
                return null;

            return $"The state directory '{stateDirectory}' grants its group no access, so a reader "
                + "running as another account cannot reach this journal — and reads that as the "
                + "producer having recorded nothing rather than as a failure. Give the directory a "
                + "group every KGSM service belongs to and group execute (0750).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // A mode this process cannot read is not evidence of a bad one. Staying quiet is the
            // honest answer; the alternative is warning about a host that is configured correctly.
            return null;
        }
    }
}

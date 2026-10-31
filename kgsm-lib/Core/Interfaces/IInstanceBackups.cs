using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Read-only access to the archive bytes of an instance's backups — the jailed filesystem authority for
/// the backups store, as <see cref="IInstanceFiles"/> is for the working directory.
/// </summary>
/// <remarks>
/// It is a separate service rather than a second root on <see cref="IInstanceFiles"/> because backups
/// deliberately live OUTSIDE the working directory (so uninstalling an instance, which removes that
/// directory wholesale, leaves the backups intact) — the two roots are different places with different
/// lifetimes, and folding them together would make "the instance's files" mean two things.
/// <para>
/// The jail rule is not duplicated: containment comes from the same <c>InstanceJail</c> both services
/// use, rooted here at <c>Instance.BackupsDir</c> and canonicalised fresh on every call.
/// </para>
/// <para>
/// Only a COMPRESSED backup has archive bytes to open. An uncompressed one is a <c>data/</c> directory
/// tree, not a single artifact — there is nothing to hand over as one stream and no digest to verify it
/// against, so it is refused with <see cref="FileOpOutcome.NotAFile"/> rather than silently tarred into
/// something the manifest never described.
/// </para>
/// </remarks>
public interface IInstanceBackups
{
    /// <summary>
    /// Opens a compressed backup's archive for reading, together with the facts its manifest records.
    /// </summary>
    /// <param name="instance">The instance whose backup to open.</param>
    /// <param name="backupId">The backup's opaque id, as <c>kgsm instances backups</c> lists it.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> with an open read stream the caller owns and must dispose, plus the
    /// size, mtime and the manifest's own sha256;
    /// <see cref="FileOpOutcome.InstanceUnavailable"/> when the instance or its backups directory cannot
    /// be resolved; <see cref="FileOpOutcome.OutOfJail"/> when <paramref name="backupId"/> escapes the
    /// backups store; <see cref="FileOpOutcome.NotFound"/> when there is no such backup, when it carries
    /// no readable manifest (an interrupted build is not a backup), or when its archive is missing;
    /// <see cref="FileOpOutcome.NotAFile"/> when the backup is uncompressed, or its archive is not a
    /// regular file; <see cref="FileOpOutcome.IoError"/> for any other filesystem failure.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when either argument is null or whitespace.</exception>
    FileOpResult<BackupArchive> OpenArchive(string instance, string backupId);
}

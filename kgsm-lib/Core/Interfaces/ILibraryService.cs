using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// The typed surface over KGSM's <c>libraries</c> module — the named roots instances
/// are placed in. This is the only way a C# consumer reaches library management; no
/// other project shells <c>kgsm libraries</c> itself.
/// </summary>
public interface ILibraryService
{
    /// <summary>
    /// Lists every registered library with its state, capacity and instance count.
    /// </summary>
    /// <returns>
    /// The registered libraries, or <c>null</c> when the read could not be made. A host
    /// with no libraries registered answers an empty list, which is a different fact
    /// from a failed read — a caller that collapses the two reports an unreadable host
    /// as an empty one.
    /// </returns>
    List<Library>? List();

    /// <summary>
    /// Registers a library at a path, creating the root when it does not exist and
    /// writing the <c>.kgsm-library</c> marker into it. A root that already carries a
    /// marker is adopted under the identity written in it.
    /// </summary>
    /// <param name="path">Absolute path of the library root.</param>
    /// <param name="name">
    /// Optional library name. Absent, the engine takes the directory's own name, or the
    /// name in an existing marker.
    /// </param>
    /// <param name="actor">Optional audit principal (who) propagated to KGSM as
    /// <c>KGSM_EVENT_ACTOR</c>, written <c>provider:name</c>; null/empty = no actor emitted.</param>
    /// <param name="origin">Optional driving surface (through-what) propagated as
    /// <c>KGSM_EVENT_ORIGIN</c>; null/empty = no surface emitted.</param>
    /// <returns>Result of the add operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when path is null.</exception>
    KgsmResult Add(string path, string? name = null, string? actor = null, string? origin = null);

    /// <summary>
    /// Deregisters a library. No file inside it is touched, including the instances
    /// placed there.
    /// </summary>
    /// <param name="name">Library name.</param>
    /// <param name="force">
    /// Deregister even while instances resolve to the library. Without it the engine
    /// refuses, naming what blocks the removal.
    /// </param>
    /// <param name="drainTo">
    /// Name of the library to move every resident instance into before deregistering — the way a
    /// disk is emptied before it is taken out. The engine moves them one at a time and deregisters
    /// once the last has landed; every resident has to be stopped first, and it lists the running
    /// ones and does nothing rather than stopping servers on the caller's behalf.
    /// <para>
    /// Minutes of copying per resident instance. Drive this as a job, and give the executor
    /// <see cref="KgsmTimeoutOptions.Move"/> rather than the default ceiling.
    /// </para>
    /// <para>
    /// Mutually exclusive with <paramref name="force"/> — one moves the instances and the other
    /// abandons them. Both together is refused by the engine, which owns that rule so there is one
    /// answer to it.
    /// </para>
    /// </param>
    /// <param name="actor">Optional audit principal — see <see cref="Add"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Add"/>.</param>
    /// <returns>Result of the remove operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when name is null.</exception>
    KgsmResult Remove(string name, bool force = false, string? drainTo = null, string? actor = null, string? origin = null);

    /// <summary>
    /// Renames a library in the registry and in its marker. Instances are unaffected:
    /// they record the library's path, and the name lives only in the registry.
    /// </summary>
    /// <param name="oldName">Current library name.</param>
    /// <param name="newName">New library name.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Add"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Add"/>.</param>
    /// <returns>Result of the rename operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either name is null.</exception>
    KgsmResult Rename(string oldName, string newName, string? actor = null, string? origin = null);
}

using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// The write-side authority for blueprint files: creates, overwrites, and removes
/// <c>&lt;name&gt;.bp.yaml</c> files in kgsm's writable USER blueprints directory, and reads them from
/// either directory (<see cref="ReadRaw"/>). Two write paths sit side by side —
/// <see cref="Create"/> templates a typed native draft, <see cref="WriteRaw"/> commits exact bytes for
/// any runtime — and only the latter can round-trip a container blueprint or one carrying comments. The SECOND kgsm-lib
/// service that does direct <c>System.IO</c> (the first is <see cref="IInstanceFiles"/>, whose shape this
/// mirrors: atomic write, a jail, and an outcome-not-exception failure channel). The complementary
/// write-side to the existing read-only <see cref="IBlueprintService"/>.
/// </summary>
/// <remarks>
/// Three non-negotiable guardrails:
/// <list type="number">
/// <item><b>No semantic validation here.</b> This authority only performs STRUCTURAL checks (a required
///   field present, a safe name) — never re-implements what kgsm considers a *valid* blueprint (a legal
///   <c>runtime</c>, port-format parsing, ...). Every write must be validated by reading it back through
///   the engine's existing read path (<see cref="IBlueprintService.GetInfo(string)"/>) — the engine stays
///   the schema authority.</item>
/// <item><b>The jail root is learned from the engine, never re-derived in C#.</b> The user blueprints
///   directory is resolved fresh on every call from <c>kgsm --paths --json</c>'s
///   <c>user.KGSM_USER_BLUEPRINTS_DIR</c> (deserialized to <see cref="KgsmPaths"/> — a stable,
///   machine-readable contract, no free-form text parsing). This mirrors how <see cref="IInstanceFiles"/>
///   learns its jail root from <see cref="IInstanceService.GetInstanceInfo(string)"/>'s <c>WorkingDir</c>
///   rather than re-deriving XDG paths in C#.</item>
/// <item><b>AOT-safe YAML.</b> No YamlDotNet or other reflection-based serializer — the native schema is
///   small and flat, so <see cref="Create"/> string-templates it directly (deterministic, diff-stable
///   field order matching <c>kgsm/templates/blueprint.tp</c>).</item>
/// </list>
/// <para>
/// Failure-channel convention: every method returns a <see cref="FileOpResult"/>/<see cref="FileOpResult{T}"/>
/// whose <see cref="FileOpResult.Outcome"/> reports what happened — including an invalid name, a
/// resolution failure for the user blueprints dir, or a filesystem error — and none of these methods
/// throw for an expected failure. Argument validation (a null/blank <c>name</c>) still throws, per the
/// rest of kgsm-lib's convention.
/// </para>
/// <para>
/// The jail: the root is the engine-reported user blueprints directory, canonicalised (POSIX
/// <c>realpath</c>) fresh on every call — never cached, mirroring <see cref="IInstanceFiles"/>. Unlike
/// <see cref="IInstanceFiles"/> (which resolves an arbitrary caller-supplied relative path), the only
/// caller input here is a single-segment <c>name</c>: it is rejected as
/// <see cref="FileOpOutcome.OutOfJail"/> before any disk access unless it is a safe lowercase slug
/// (<c>[a-z0-9_-]</c>, 1–64 chars, no leading/trailing separator) — no path separators, no <c>..</c>, no
/// absolute path can ever reach this far. The resulting <c>&lt;userDir&gt;/&lt;name&gt;.bp.yaml</c> path
/// is still canonicalised and containment-checked as defense in depth, exactly like
/// <see cref="IInstanceFiles"/>' jail check.
/// </para>
/// <para>
/// <see cref="ReadRaw"/> is the ONE method whose jail spans both blueprints directories, because a
/// shipped blueprint has to be readable in order to be edited into an override. Both roots come from
/// <c>kgsm --paths --json</c> like the write root does, and the engine's resolved path is checked against
/// them — a containment check on the engine's answer, never a path this library composed.
/// </para>
/// <para>
/// <see cref="Create"/>, <see cref="WriteRaw"/> and <see cref="Remove"/> operate ONLY inside the user
/// blueprints directory — they are structurally
/// incapable of touching the read-only SYSTEM blueprints directory (<c>KGSM_SYSTEM_BLUEPRINTS_DIR</c>,
/// e.g. shipped blueprints like <c>factorio.bp.yaml</c>): the path they write or delete is always
/// <c>&lt;userDir&gt;/&lt;name&gt;.bp.yaml</c>, never derived from or resolved against the system dir. If
/// a name has no file in the user dir (whether or not a same-named SYSTEM blueprint exists),
/// <see cref="Remove"/> reports <see cref="FileOpOutcome.NotFound"/> and touches nothing.
/// </para>
/// </remarks>
public interface IBlueprintFiles
{
    /// <summary>
    /// Templates <paramref name="draft"/> into a valid native <c>&lt;name&gt;.bp.yaml</c> string and
    /// atomically writes it into the user blueprints directory (temp file in the same directory →
    /// fsync → rename — a crash mid-write never corrupts an existing file of the same name).
    /// </summary>
    /// <param name="draft">The blueprint draft to template and write.</param>
    /// <param name="overwrite">When true, an existing same-named user blueprint is replaced; when false
    /// (default), an existing target is refused with <see cref="FileOpOutcome.AlreadyExists"/>. A
    /// self-repair loop that re-persists an adjusted draft under the same probe name passes
    /// <see langword="true"/>.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> with the written file's size/mtime/etag;
    /// <see cref="FileOpOutcome.InvalidDraft"/> if <see cref="NativeBlueprintDraft.Native"/>'s
    /// <c>ExecutableFile</c> is blank (the one structural requirement — see the interface remarks for
    /// what this authority does and does not validate);
    /// <see cref="FileOpOutcome.OutOfJail"/> if <see cref="NativeBlueprintDraft.Name"/> is not a safe
    /// slug, or the resolved target somehow escapes the user blueprints directory;
    /// <see cref="FileOpOutcome.AlreadyExists"/> if a same-named user blueprint exists and
    /// <paramref name="overwrite"/> is false;
    /// <see cref="FileOpOutcome.BlueprintsDirUnavailable"/> if the user blueprints directory could not be
    /// resolved from the engine;
    /// <see cref="FileOpOutcome.IoError"/> for any other filesystem failure.
    /// </returns>
    /// <param name="actor">The audit principal to stamp on the emitted event, written
    /// <c>provider:name</c>. Null emits no actor — never a fabricated one.</param>
    /// <param name="origin">The surface that drove the write. Null emits no origin — never a fabricated one.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft"/> is null.</exception>
    /// <remarks>Emits <c>blueprint_created</c> or <c>blueprint_updated</c> on success, exactly as
    /// <see cref="WriteRaw"/> does — a blueprint written through the typed path is no less a blueprint
    /// write, and a consumer that trusts these events must see both. A failed emit does not fail the write.</remarks>
    FileOpResult<FileStat> Create(NativeBlueprintDraft draft, bool overwrite = false, string? actor = null, string? origin = null);

    /// <summary>
    /// Renders <paramref name="draft"/> to the exact native <c>&lt;name&gt;.bp.yaml</c> string
    /// <see cref="Create"/> would write, WITHOUT touching the filesystem or the engine — the editable
    /// text an authoring surface shows a user for in-chat review. Pure and deterministic: identical field
    /// order and single-quoted scalar style to <see cref="Create"/>, so <see cref="TryParse"/> is its exact
    /// inverse. Performs no validation (a draft with a blank <c>executable_file</c> still renders).
    /// </summary>
    /// <param name="draft">The blueprint draft to template into YAML.</param>
    /// <returns>The rendered <c>&lt;name&gt;.bp.yaml</c> content.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft"/> is null.</exception>
    string Render(NativeBlueprintDraft draft);

    /// <summary>
    /// Parses a native blueprint YAML string — as produced by <see cref="Render"/>, tolerant of light
    /// hand-edits (unquoted or double-quoted scalars, extra blank lines, whole-line <c>#</c> comments,
    /// trailing spaces) — back into a <see cref="NativeBlueprintDraft"/>. The inverse of <see cref="Render"/>,
    /// for a review surface that let a user edit the rendered text before it is finalized.
    /// <para>
    /// STRUCTURAL only, exactly like <see cref="Create"/>: it requires a safe <c>name</c>, a
    /// <c>runtime</c> of <c>native</c> (a non-native runtime is refused — this authority only handles
    /// native blueprints), and a non-blank <c>native.executable_file</c>. It NEVER judges semantic validity
    /// (a legal port format, a real app id, …) — that stays the engine's authority, applied by reading the
    /// draft back via <see cref="IBlueprintService.GetInfo(string)"/> after it is written.
    /// </para>
    /// </summary>
    /// <param name="yaml">The native blueprint YAML to parse.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> with the parsed draft;
    /// <see cref="FileOpOutcome.InvalidDraft"/> (with a human-readable message) if a required field is
    /// missing/blank, the <c>runtime</c> is present but not <c>native</c>, or the <c>name</c> is not a safe
    /// slug.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="yaml"/> is null.</exception>
    FileOpResult<NativeBlueprintDraft> TryParse(string yaml);

    /// <summary>
    /// Reads a blueprint file's exact bytes as text — the engine-resolved file for <paramref name="name"/>,
    /// from EITHER blueprints directory. The one method here whose jail spans both: a shipped blueprint
    /// must be readable in order to be edited into an override, while the write jail stays user-dir-only.
    /// </summary>
    /// <param name="name">The blueprint name to read.</param>
    /// <param name="maxBytes">The byte-length ceiling — a larger file is refused rather than loaded.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> with the file's text, resolved path, tier, and etag;
    /// <see cref="FileOpOutcome.NotFound"/> if the name resolves to no file in either directory;
    /// <see cref="FileOpOutcome.OutOfJail"/> if <paramref name="name"/> is not a safe slug, or the engine's
    /// resolved path lies outside both engine-reported blueprints directories;
    /// <see cref="FileOpOutcome.NotAFile"/> if the resolved path is not a regular file;
    /// <see cref="FileOpOutcome.TooLarge"/> if the file exceeds <paramref name="maxBytes"/>;
    /// <see cref="FileOpOutcome.Binary"/> if its bytes are not valid UTF-8 text;
    /// <see cref="FileOpOutcome.BlueprintsDirUnavailable"/> if the blueprints directories could not be
    /// resolved from the engine;
    /// <see cref="FileOpOutcome.IoError"/> for any other filesystem failure.
    /// </returns>
    /// <remarks>
    /// Raw text, never a typed round-trip: <see cref="Create"/>/<see cref="Render"/> handle native
    /// blueprints only and drop every comment, so a container blueprint or a commented one could not
    /// survive being read and written back through them. Reading the bytes is what makes byte-level
    /// editing possible.
    /// <para>
    /// Resolution goes through <see cref="IBlueprintService.FindAll(string)"/> rather than
    /// <see cref="IBlueprintService.FindPath(string)"/>, so a MALFORMED blueprint is still readable —
    /// that is precisely the file an editor is opened to repair — and so the tier and override state come
    /// from the engine's own candidate list instead of being inferred.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    FileOpResult<BlueprintFileContent> ReadRaw(string name, long maxBytes);

    /// <summary>
    /// Atomically writes <paramref name="content"/> verbatim to <c>&lt;name&gt;.bp.yaml</c> in the user
    /// blueprints directory, after the ENGINE has validated it — and emits the corresponding
    /// <c>blueprint_created</c>/<c>blueprint_updated</c> event.
    /// </summary>
    /// <param name="name">The blueprint name to write.</param>
    /// <param name="content">The exact file text to write. Written byte-for-byte: comments, field order,
    /// and container blueprints all survive.</param>
    /// <param name="opts">Caps, the optimistic-concurrency guard, and the provenance to stamp on the event.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> with the written file's size/mtime/etag;
    /// <see cref="FileOpOutcome.OutOfJail"/> if <paramref name="name"/> is not a safe slug, or the resolved
    /// target somehow escapes the user blueprints directory;
    /// <see cref="FileOpOutcome.TooLarge"/> if the UTF-8 content exceeds <see cref="BlueprintWriteOptions.MaxBytes"/>;
    /// <see cref="FileOpOutcome.EtagMismatch"/> if <see cref="BlueprintWriteOptions.ExpectedEtag"/> no
    /// longer matches the currently resolved file;
    /// <see cref="FileOpOutcome.InvalidDraft"/> if the engine rejected the content, with its errors listed
    /// individually on <see cref="FileOpResult{T}.Errors"/> (and joined into
    /// <see cref="FileOpResult{T}.Message"/>) — nothing was written;
    /// <see cref="FileOpOutcome.BlueprintsDirUnavailable"/> if the user blueprints directory could not be
    /// resolved from the engine, or the engine returned no verdict at all;
    /// <see cref="FileOpOutcome.IoError"/> for any other filesystem failure.
    /// </returns>
    /// <remarks>
    /// A write ALWAYS lands in the user directory. Saving an edit to a shipped blueprint therefore creates
    /// an override that shadows it permanently, rather than modifying the shipped file — which is not
    /// merely policy here but structural: the system directory is the engine deploy's rsync target and
    /// would be erased on the next one.
    /// <para>
    /// Validation happens on a temp file under a name the engine's <c>*.bp.yaml</c> glob cannot see, so an
    /// invalid draft never occupies the real filename even momentarily. Only after the engine approves it
    /// is the temp renamed over the target.
    /// </para>
    /// <para>
    /// A failed EMIT does not fail the write: the bytes are committed and valid, so the failure is logged
    /// and the result stays <see cref="FileOpOutcome.Ok"/>. Reporting an error would tell the caller their
    /// save failed when it did not; losing the notification only degrades consumers to polling.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> or <paramref name="opts"/> is null.</exception>
    FileOpResult<FileStat> WriteRaw(string name, string content, BlueprintWriteOptions opts);

    /// <summary>
    /// Deletes <c>&lt;name&gt;.bp.yaml</c> from the user blueprints directory ONLY — see the interface
    /// remarks for why this can never reach a system blueprint — and emits <c>blueprint_removed</c>.
    /// </summary>
    /// <param name="name">The blueprint name (the same slug passed as <see cref="NativeBlueprintDraft.Name"/>
    /// to <see cref="Create"/>) to remove.</param>
    /// <param name="actor">The audit principal to stamp on the emitted event, written
    /// <c>provider:name</c>. Null emits no actor — never a fabricated one.</param>
    /// <param name="origin">The surface that drove the removal. Null emits no origin — never a
    /// fabricated one.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> on success;
    /// <see cref="FileOpOutcome.OutOfJail"/> if <paramref name="name"/> is not a safe slug;
    /// <see cref="FileOpOutcome.NotFound"/> if no such file exists in the user blueprints directory
    /// (including when only a same-named SYSTEM blueprint exists — that file is never touched);
    /// <see cref="FileOpOutcome.BlueprintsDirUnavailable"/> if the user blueprints directory could not be
    /// resolved from the engine;
    /// <see cref="FileOpOutcome.IoError"/> for any other filesystem failure.
    /// </returns>
    /// <remarks>
    /// This is the revert path: when a shipped original exists, deleting the user copy restores it; when
    /// it does not, the blueprint is gone entirely. The emitted event says which of the two happened, so
    /// a consumer never has to guess.
    /// <para>A failed emit does not fail the removal — see <see cref="WriteRaw"/>.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    FileOpResult Remove(string name, string? actor = null, string? origin = null);

    /// <summary>
    /// Reports whether <c>&lt;name&gt;.bp.yaml</c> currently exists in the user blueprints directory,
    /// without reading it.
    /// </summary>
    /// <param name="name">The blueprint name to check.</param>
    /// <returns>
    /// <see cref="FileOpOutcome.Ok"/> with <see langword="true"/>/<see langword="false"/>;
    /// <see cref="FileOpOutcome.OutOfJail"/> if <paramref name="name"/> is not a safe slug;
    /// <see cref="FileOpOutcome.BlueprintsDirUnavailable"/> if the user blueprints directory could not be
    /// resolved from the engine.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    FileOpResult<bool> Exists(string name);
}

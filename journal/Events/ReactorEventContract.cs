namespace TheKrystalShip.KGSM.Events;

/// <summary>
/// The event types the reactor leaf produces about what it judged.
/// </summary>
/// <remarks>
/// <para>
/// <b>A judgment, which exists nowhere else on the host.</b> The engine records what happened and the
/// supervisor records what it did about it; nobody records that a condition was read against a rule
/// and found to hold. That reading is the reactor's whole output — in observe mode it is its
/// <em>only</em> output — so an event is the sole way anything downstream learns a rule spoke.
/// </para>
/// <para>
/// <b>Four events, because each is a separate immutable fact.</b> A rule decided; an offer was put to
/// a person; that offer reached an end; the reactor performed something itself. Collapsing any pair
/// makes a real case unrepresentable — <em>"it decided and the action failed"</em>, or <em>"it offered
/// and nobody answered"</em>, which is the one an operator most needs to see when reviewing a week.
/// </para>
/// <para>
/// <b>Written on a transition, never on an evaluation.</b> A state rule re-reads its condition on
/// every sweep and the reactor's ledger folds those into one row that gets better informed. Emitting
/// per evaluation would write a line every thirty seconds for a condition that has not changed, and
/// the journal would record how often the reactor looked rather than what it concluded.
/// </para>
/// </remarks>
public static class ReactorEvents
{
    /// <summary>
    /// <c>reactor.decided</c> — a rule reached a verdict about a subject.
    /// </summary>
    /// <remarks>
    /// <b>Not an audit row.</b> A decision in observe mode is something this host <em>noticed</em>,
    /// not something it did, and an audit trail records what was performed. Only
    /// <see cref="Acted"/> has any business becoming one.
    /// </remarks>
    public const string Decided = "reactor.decided";

    /// <summary>
    /// <c>reactor.proposed</c> — a rule staged an action for a person to confirm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An offer, and nothing has happened yet.</b> The rule decided, the mode was propose, and the
    /// action is held under a handle until somebody redeems it or its lifetime runs out. A consumer
    /// that rendered this as work performed would be announcing something that has not been done.
    /// </para>
    /// <para>
    /// <b>Its expiry is not what makes it safe.</b> A proposal is addressed to whoever notices,
    /// possibly in the morning, so the window is measured in hours rather than the seconds a
    /// confirmation held in front of somebody who just asked gets. What makes the long window safe is
    /// that the condition is re-derived at redemption: a server that came back up on its own resolves
    /// the proposal instead of executing it.
    /// </para>
    /// </remarks>
    public const string Proposed = "reactor.proposed";

    /// <summary>
    /// <c>reactor.resolved</c> — a staged proposal reached its end, whichever end that was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every proposal gets exactly one of these, and the four ways out are the point:
    /// <see cref="ReactorResolutions"/> separates a person saying yes from a person saying no, from
    /// nobody answering at all, from the condition having gone away before anybody did. The third and
    /// fourth are what a review of a week is actually looking for — a rule whose offers all lapse is
    /// one nobody wants, and a rule whose offers all go stale is one whose settle window is too short.
    /// </para>
    /// <para>
    /// <b>The resolution says what the person did; <c>Ok</c> says what the action did.</b> They
    /// answer different questions and a confirmed proposal whose action then failed needs both.
    /// <c>Ok</c> is absent whenever nothing ran.
    /// </para>
    /// </remarks>
    public const string Resolved = "reactor.resolved";

    /// <summary>
    /// <c>reactor.acted</c> — the reactor carried an action out itself, however it went.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It repeats the rule, the subject and the action rather than making a reader join back to
    /// <see cref="Decided"/> on the decision id: a consumer has to be able to render this from the one
    /// event, and a join is a second read that can fail while the first succeeded.
    /// </para>
    /// <para>
    /// <b>Autonomous, with nobody behind it.</b> An action a person confirmed is a
    /// <see cref="Resolved"/> carrying their name; this is the one where the rule is the whole
    /// authority. Keeping them apart is what lets a surface answer "what did this host do on its own"
    /// without subtracting one set from another.
    /// </para>
    /// </remarks>
    public const string Acted = "reactor.acted";

    /// <summary>
    /// The prefix every event this leaf writes shares.
    /// </summary>
    /// <remarks>
    /// <b>The reactor tails every producer's journal, its own included</b>, so what it writes comes
    /// back to it. No rule may wake on one of these: a rule woken by a decision would decide about its
    /// own decision, write that, and be woken by it — at the sweep interval, forever, with a
    /// plausible-looking ledger. The leaf enforces it by construction, and this is the string anything
    /// else recognising the family reads.
    /// </remarks>
    public const string Prefix = "reactor.";
}

/// <summary>
/// The payload field names the reactor's events carry.
/// </summary>
/// <remarks>
/// Declared once, and read by all three descriptions of a field — what the leaf writes, what the
/// payload class binds to, and what the catalog classifies. Three descriptions are only ever the same
/// field if they are the same string.
/// </remarks>
public static class ReactorEventFields
{
    /// <summary>The rule that decided. Also the actor an audit row carries.</summary>
    public const string Rule = "Rule";

    /// <summary>Who had shaped that rule when it decided, as <c>provider:name</c>.</summary>
    public const string RuleAuthor = "RuleAuthor";

    /// <summary>What was judged — a server name, a sensor reference, a component.</summary>
    public const string Subject = "Subject";

    /// <summary>What sort of thing that is: <c>instance</c>, <c>host</c>, <c>leaf</c>, <c>unknown</c>.</summary>
    public const string SubjectKind = "SubjectKind";

    /// <summary>How loudly the rule speaks, in the ecosystem's severity spellings.</summary>
    public const string Severity = "Severity";

    /// <summary>The authority it ran under: <c>observe</c>, <c>propose</c>, <c>act</c>.</summary>
    public const string Mode = "Mode";

    /// <summary>What was decided — see <see cref="ReactorOutcomes"/>.</summary>
    public const string Outcome = "Outcome";

    /// <summary>Why, in one line. Always present.</summary>
    public const string Reason = "Reason";

    /// <summary>What the rule would do, as a stable name.</summary>
    public const string Action = "Action";

    /// <summary>The server the action operates on, or null when it operates on none.</summary>
    public const string ActionInstance = "ActionInstance";

    /// <summary>The decision's own identity, which <see cref="ReactorEvents.Acted"/> refers back to.</summary>
    public const string DecisionId = "DecisionId";

    /// <summary>When the condition opened. The envelope's timestamp is when it was decided.</summary>
    public const string OpenedAt = "OpenedAt";

    /// <summary>Whose journal the originating line is in.</summary>
    public const string SourceProducer = "SourceProducer";

    /// <summary>Which segment file of it.</summary>
    public const string SourceSegment = "SourceSegment";

    /// <summary>The byte offset in that segment.</summary>
    public const string SourceOffset = "SourceOffset";

    /// <summary>The id the originating line's producer minted for it, or null when it carries none.</summary>
    public const string SourceEventId = "SourceEventId";

    /// <summary>
    /// The opaque token a staged proposal is redeemed with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not guessable, and not a name.</b> Confirming is redeeming this handle, not re-issuing the
    /// command it describes — which is what keeps the re-derivation of the condition on the path.
    /// </para>
    /// <para>
    /// <b>Spelled in full on the wire because a bare <c>Handle</c> already means a person.</b> An
    /// account event carries one, and that is somebody's name; this is a capability. One field name
    /// standing for both would leave every consumer classifying whichever it met first.
    /// </para>
    /// </remarks>
    public const string ProposalHandle = "ProposalHandle";

    /// <summary>When an unanswered proposal stops being redeemable.</summary>
    public const string ExpiresAt = "ExpiresAt";

    /// <summary>How a proposal ended — see <see cref="ReactorResolutions"/>.</summary>
    public const string Resolution = "Resolution";

    /// <summary>Who answered, as <c>provider:name</c>, or null when nobody did.</summary>
    public const string AnsweredBy = "AnsweredBy";

    /// <summary>Whether the action succeeded.</summary>
    public const string Ok = "Ok";

    /// <summary>What the action produced — a backup id — or null.</summary>
    public const string Artifact = "Artifact";

    /// <summary>What went wrong, or what else is worth reading.</summary>
    public const string Detail = "Detail";
}

/// <summary>
/// What a rule concluded, and what the gate did with it.
/// </summary>
/// <remarks>
/// <para>
/// Lower case, which is the spelling every enumerated value in every event payload on this host uses
/// — a consumer compares against these without knowing anybody's casing convention.
/// </para>
/// <para>
/// <b>Three of these are not verdicts about the world.</b> Fired and settled report what the rule
/// decided; suppressed, ceilinged and superseded report that it decided <em>yes</em> and the gate held
/// it back. A consumer counting "how often was this condition true" must count those alongside
/// <see cref="Fired"/>, and one counting "how often did anything happen" must not.
/// </para>
/// </remarks>
public static class ReactorOutcomes
{
    /// <summary>The condition held and nothing held the decision back.</summary>
    public const string Fired = "fired";

    /// <summary>The condition did not hold — usually because it resolved itself.</summary>
    public const string Settled = "settled";

    /// <summary>It held, and this rule had already spoken about this subject too recently.</summary>
    public const string Suppressed = "suppressed";

    /// <summary>It held, and the host-wide hourly ceiling was already reached.</summary>
    public const string Ceilinged = "ceilinged";

    /// <summary>It held, and a louder rule had already spoken for the same episode.</summary>
    public const string Superseded = "superseded";

    /// <summary>
    /// No judgment could be formed.
    /// </summary>
    /// <remarks>
    /// <b>Never read as a no.</b> Either the world could not be read or what was read is not enough
    /// to decide on, and a surface that folded this into "the condition does not hold" would report
    /// silence as an all-clear.
    /// </remarks>
    public const string Unreadable = "unreadable";
}

/// <summary>
/// The four ways a staged proposal ends.
/// </summary>
/// <remarks>
/// <para>
/// Lower case, like every other enumerated value in every event payload on this host.
/// </para>
/// <para>
/// <b>Exhaustive by construction, and each one is a different fact about the rule that staged it.</b>
/// A rule whose offers are mostly confirmed is one that should be considered for acting on its own; a
/// rule whose offers are mostly dismissed is one whose condition is wrong; a rule whose offers mostly
/// lapse is one nobody wants; a rule whose offers mostly go stale is one that speaks too early. A
/// consumer folding any of the last three into "not confirmed" throws away the only signal that
/// separates them.
/// </para>
/// </remarks>
public static class ReactorResolutions
{
    /// <summary>A person said yes, and the action was attempted. <c>Ok</c> says how it went.</summary>
    public const string Confirmed = "confirmed";

    /// <summary>A person said no. Nothing was attempted.</summary>
    public const string Dismissed = "dismissed";

    /// <summary>Nobody answered before the proposal's lifetime ran out.</summary>
    public const string Lapsed = "lapsed";

    /// <summary>
    /// Somebody tried to confirm it, and by then the condition it rested on had gone.
    /// </summary>
    /// <remarks>
    /// <b>This is the safety property, observed working.</b> The rule is re-evaluated at redemption
    /// rather than at staging, so a server that came back up on its own turns a confirmed restore into
    /// this instead of overwriting a running world. A host where these are common is not a host with a
    /// broken reactor — it is one where the settle windows are shorter than the conditions.
    /// </remarks>
    public const string NoLongerApplicable = "no_longer_applicable";

    /// <summary>Every resolution the reactor writes.</summary>
    public static readonly IReadOnlyList<string> All =
        [Confirmed, Dismissed, Lapsed, NoLongerApplicable];
}

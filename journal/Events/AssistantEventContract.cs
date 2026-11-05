namespace TheKrystalShip.KGSM.Events;

/// <summary>
/// The event types the assistant leaf produces about its own conduct.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not a log of what the assistant did.</b> Every mutation it performs runs through
/// this library with provenance attached, so the engine's own journal already records it, attributed
/// to the person who asked. A second copy here would be an answer able to disagree with the one the
/// engine wrote.
/// </para>
/// <para>
/// What these record is the opposite: <b>the turn that did not act</b>. A refusal, a proposal nobody
/// approved, and a claim of an action that never happened leave the engine's record entirely empty —
/// from its side nothing occurred — and so exist nowhere else on the host.
/// </para>
/// <para>
/// The blueprint pair is the exception that proves the rule: it brackets a run whose side effects the
/// engine <em>does</em> record, in full, as an ordinary install and uninstall of a disposable probe
/// instance. What the engine cannot say is that those rows belong to one authoring run, or how it
/// concluded — and when authoring fails there is no engine event at all.
/// </para>
/// </remarks>
public static class AssistantEvents
{
    /// <summary>
    /// <c>assistant_claim_corrected</c> — a reply described an action the turn never took, or a
    /// lookup it never made, and was re-prompted or corrected.
    /// </summary>
    /// <remarks>
    /// The only measurement of the deployed model's fabrication rate on real prompts. The benchmark
    /// scores the same checks against a fixed corpus, which is a different question from what the
    /// shipped prompt does with what people actually ask.
    /// </remarks>
    public const string ClaimCorrected = "assistant.claim.corrected";

    /// <summary>
    /// <c>assistant_action_declined</c> — somebody reached for an action they do not hold.
    /// </summary>
    /// <remarks>
    /// Authorization only. The assistant also refuses for blast-radius reasons — too many staged
    /// commands, too many searches, a repeated lookup — and those are loop guards firing on ordinary
    /// model over-eagerness, not somebody reaching past their permissions.
    /// </remarks>
    public const string ActionDeclined = "assistant.action.declined";

    /// <summary>
    /// <c>assistant_action_proposed</c> — a mutation was staged and is waiting on a person.
    /// </summary>
    /// <remarks>
    /// Nothing has run: the engine has no record of a proposal until somebody confirms it, and one
    /// that expires unapproved is invisible everywhere else.
    /// </remarks>
    public const string ActionProposed = "assistant.action.proposed";

    /// <summary>
    /// <c>assistant_blueprint_authoring_started</c> — a blueprint-authoring run began.
    /// </summary>
    public const string BlueprintAuthoringStarted = "assistant.blueprint.authoring_started";

    /// <summary>
    /// <c>assistant_blueprint_authored</c> — a blueprint-authoring run concluded, however it ended.
    /// </summary>
    public const string BlueprintAuthored = "assistant.blueprint.authored";
}

/// <summary>
/// The payload field names the assistant's own events carry.
/// </summary>
/// <remarks>
/// Declared once, and read by all three descriptions of a field — what the leaf writes, what the
/// payload class binds to, and what the catalog classifies. Three descriptions are only ever the same
/// field if they are the same string.
/// </remarks>
public static class AssistantEventFields
{
    /// <summary>Which check found the claim — see <see cref="AssistantClaimChecks"/>.</summary>
    public const string Check = "Check";

    /// <summary>What was done about it — see <see cref="AssistantClaimResolutions"/>.</summary>
    public const string Resolution = "Resolution";

    /// <summary>Which net caught it — see <see cref="AssistantClaimNets"/>.</summary>
    public const string Net = "Net";

    /// <summary>The conversation it happened in.</summary>
    public const string ConversationId = "ConversationId";

    /// <summary>The tool that was refused, proposed, or run.</summary>
    public const string Tool = "Tool";

    /// <summary>Why an action was declined — see <see cref="AssistantDeclineReasons"/>.</summary>
    public const string DeclineReason = "DeclineReason";

    /// <summary>The instance an action would have touched, when it names one.</summary>
    public const string Instance = "Instance";

    /// <summary>What kind of action was staged, by name and never by ordinal.</summary>
    public const string Kind = "Kind";

    /// <summary>How long the staged action stays redeemable.</summary>
    public const string ExpiresInSec = "ExpiresInSec";

    /// <summary>The disposable instance an authoring run installs to test its draft.</summary>
    public const string Probe = "Probe";

    /// <summary>How an authoring run concluded — see <see cref="AssistantAuthoringOutcomes"/>.</summary>
    public const string AuthoringOutcome = "AuthoringOutcome";

    /// <summary>How long an authoring run took.</summary>
    public const string DurationSec = "DurationSec";
}

/// <summary>Which integrity check found a reply wanting.</summary>
public static class AssistantClaimChecks
{
    /// <summary>The reply claimed an action on a turn that staged and ran nothing.</summary>
    public const string UnbackedAction = "unbacked_action";

    /// <summary>The person asked for the web and the turn looked nothing up.</summary>
    public const string UnsearchedWeb = "unsearched_web";
}

/// <summary>What was done about a reply that failed its check.</summary>
public static class AssistantClaimResolutions
{
    /// <summary>The turn was given another attempt, told what it had and had not done.</summary>
    public const string RePrompted = "re_prompted";

    /// <summary>A correction was appended and the reply left standing.</summary>
    public const string Corrected = "corrected";
}

/// <summary>
/// Which of the two nets caught a claim.
/// </summary>
/// <remarks>
/// They sit at different depths and fire at very different rates: the review runs where the turn can
/// still be re-prompted, and the outer net runs over the reply the turn ends on. Knowing which caught
/// a given claim is the difference between a model that can be talked out of it and one that cannot.
/// </remarks>
public static class AssistantClaimNets
{
    /// <summary>The per-reply review, where a re-prompt is still possible.</summary>
    public const string Review = "review";

    /// <summary>The outer net over the finished reply.</summary>
    public const string Outer = "outer";
}

/// <summary>Why an action was refused.</summary>
public static class AssistantDeclineReasons
{
    /// <summary>The caller does not hold the action.</summary>
    public const string Authority = "authority";

    /// <summary>This host has actions turned off entirely.</summary>
    public const string ActionsDisabled = "actions_disabled";
}

/// <summary>How a blueprint-authoring run concluded.</summary>
public static class AssistantAuthoringOutcomes
{
    /// <summary>The draft installed, started, and was seen to be ready.</summary>
    public const string Verified = "verified";

    /// <summary>A draft came back for a person to review, unverified.</summary>
    public const string DraftReady = "draft_ready";

    /// <summary>The run did not produce a usable blueprint.</summary>
    public const string Failed = "failed";
}

using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.KGSM.Conformance;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;
using TheKrystalShip.KGSM.Extensions;
using TheKrystalShip.KGSM.Services;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Reads one line out of every journal on this host and asserts the producers agree.
/// </summary>
/// <remarks>
/// <para>
/// The regression net for the whole conformance layer, and the only test in the ecosystem that
/// compares two producers. Every drift the layer exists to prevent — a version stamped in the wrong
/// shape, a journal written where no reader scans, an absent value spelled as an empty string, a
/// coarser timestamp — is one this would have caught, and none of them is visible from inside the
/// repo that caused it.
/// </para>
/// <para>
/// <b>It discovers producers rather than listing them.</b> Nothing here names a leaf: it asks the same
/// scan every consumer uses and checks whatever that finds. A producer added to this ecosystem later
/// is covered the moment its journal exists, without anybody remembering to cover it — which is the
/// property that matters, because the whole class of defect here is the kind nobody thinks to look
/// for.
/// </para>
/// <para>
/// <b>It needs a host with journals on it, and says so loudly when there is none.</b> A machine that
/// has never run KGSM sets <see cref="SkipVariable"/>; anything else fails rather than passing over an
/// empty scan. A check that quietly succeeds when it measured nothing is the exact failure mode this
/// document is about, and it would be absurd for the check itself to have it.
/// </para>
/// </remarks>
public sealed class HostJournalConformanceTests
{
    /// <summary>Set on a machine that is not a KGSM host, where there is nothing to check.</summary>
    public const string SkipVariable = "KGSM_CONFORMANCE_SKIP_HOST";

    /// <summary>
    /// How many of each journal's newest lines to read.
    /// </summary>
    /// <remarks>
    /// A producer spells every line the same way, so one line per journal measures its spelling. The
    /// newest, because an old line records what an old build wrote and is allowed to look old — what
    /// is being checked is what the builds deployed here are writing now.
    /// </remarks>
    private const int LinesPerJournal = 1;

    [Fact]
    public void Every_producer_on_this_host_writes_the_same_envelope()
    {
        if (NotAKgsmHost())
            return;

        HostReport report = Check();

        Assert.True(report.Conforms, report.Describe());
    }

    [Fact]
    public void The_check_actually_read_something()
    {
        // Separate from the assertion above, because they fail for opposite reasons and a clean report
        // over nothing looks exactly like a clean report over a host. This is the half that fails when
        // discovery quietly finds no journal at all.
        if (NotAKgsmHost())
            return;

        HostReport report = Check();

        Assert.True(
            report.Journals.Count > 0,
            $"no journals were discovered under {JournalLayout.DefaultStateRoot}. If this machine is "
            + $"not a KGSM host, set {SkipVariable}=1; otherwise every producer here is invisible to "
            + "every consumer, which is the defect this suite exists to catch.");

        Assert.True(
            report.LinesChecked > 0,
            $"{report.Journals.Count} journal(s) were found and all of them are empty: {report.Describe()}");
    }

    [Fact]
    public void Every_journal_a_consumer_reads_is_one_a_producer_owns()
    {
        // Discovery derives a producer from the directory it found the journal in, and the writer
        // derives that directory from the producer. This is the two halves meeting on a real host —
        // the one place they can be observed to agree rather than asserted to.
        if (NotAKgsmHost())
            return;

        foreach (JournalSource source in Discover())
        {
            string? attributed = JournalLayout.ProducerOf(source.Directory);

            Assert.True(
                attributed is null || attributed == source.Producer,
                $"discovery calls {source.Directory} '{source.Producer}', the layout rule says "
                + $"'{attributed}'");
        }
    }

    private static HostReport Check() =>
        JournalConformance.CheckHost(
            Discover().Select(static s => new JournalTarget(s.Producer, s.Directory)),
            LinesPerJournal);

    /// <summary>
    /// Every journal on this host, found the way a consumer finds them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The real scan rather than a list, so the coverage of this check is the coverage of the
    /// ecosystem. The engine's journal is taken at its conventional location: its directory is
    /// configurable, and a host that has moved it is telling this check to look somewhere else.
    /// </para>
    /// <para>
    /// The state root is the same one a producer writes to, read from the same variable, so a run that
    /// redirected producers away from the host's real journals is checked where it actually wrote
    /// rather than against journals it never touched.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<JournalSource> Discover()
    {
        string root = Environment.GetEnvironmentVariable(
            JournalServiceCollectionExtensions.StateRootVariable) is { Length: > 0 } redirected
                ? redirected
                : JournalLayout.DefaultStateRoot;

        return new JournalDiscovery(
            JournalLayout.DirectoryFor(JournalProducer.Kgsm, root),
            root,
            NullLogger<JournalDiscovery>.Instance)
            .Discover();
    }

    private static bool NotAKgsmHost() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SkipVariable));
}

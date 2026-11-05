using System.Text.Json.Serialization;

namespace TheKrystalShip.KGSM.Core.Models;

/// <summary>
/// One contiguous port range for a single protocol — the canonical, range-preserving port
/// representation across the KGSM ecosystem. KGSM derives it from an instance's UFW-style port
/// spec (e.g. <c>27015:27020/udp|27016/tcp</c>) and emits it on the <c>instances info --json</c>
/// surface as <c>"ports": [ { "start": 27015, "end": 27020, "protocol": "udp" }, ... ]</c>.
/// <para>
/// A single port is <c>Start == End</c>; a UFW entry written with no protocol expands to two
/// mappings (one <c>tcp</c>, one <c>udp</c>). This is the shape kgsm-firewall's
/// <c>EnsureOpen(instance, ports[])</c> and the watchdog's port-forwarding path both consume — no consumer
/// re-parses an opaque port string. <see cref="Start"/>/<see cref="End"/> arrive as JSON numbers
/// (the global string→int coercion still accepts a stringly form, so either wire shape binds).
/// </para>
/// </summary>
public record class PortMapping
{
    /// <summary>First port of the inclusive range (a single port has <c>Start == End</c>).</summary>
    [JsonPropertyName("start")]
    public int Start { get; set; }

    /// <summary>Last port of the inclusive range (a single port has <c>Start == End</c>).</summary>
    [JsonPropertyName("end")]
    public int End { get; set; }

    /// <summary>Transport protocol — <c>"tcp"</c> or <c>"udp"</c>.</summary>
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;
}

/// <summary>
/// Helpers over a set of <see cref="PortMapping"/>s. They live in kgsm-lib (the C#↔engine
/// chokepoint) so no consumer re-derives port logic — the watchdog expands individual ports, and
/// anyone needing the legacy string form renders it here.
/// </summary>
public static class PortMappingExtensions
{
    /// <summary>
    /// Expand the range-preserving mappings into individual <c>(Port, Protocol)</c> pairs — what
    /// port-forwarding needs (one external port at a time) and what a per-port conflict
    /// scan iterates. Inverted ranges (<c>End &lt; Start</c>) are skipped defensively. Order
    /// follows the source mappings.
    /// </summary>
    public static IEnumerable<(int Port, string Protocol)> Expand(this IEnumerable<PortMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        foreach (PortMapping m in mappings)
        {
            if (m is null || m.End < m.Start)
                continue;
            for (int port = m.Start; port <= m.End; port++)
                yield return (port, m.Protocol);
        }
    }

    /// <summary>
    /// Render the mappings back to a UFW-style spec string — <c>start:end/proto</c> for a range,
    /// <c>port/proto</c> for a single port, <c>|</c>-joined. The inverse of KGSM's parse, for the
    /// rare consumer that still wants the legacy string (e.g. the watchdog's defensive
    /// <c>$instance_ports</c> env value for blueprint arg expansion).
    /// </summary>
    public static string ToUfwSpec(this IEnumerable<PortMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        return string.Join("|", mappings
            .Where(m => m is not null)
            .Select(m => m.Start == m.End
                ? $"{m.Start}/{m.Protocol}"
                : $"{m.Start}:{m.End}/{m.Protocol}"));
    }
}

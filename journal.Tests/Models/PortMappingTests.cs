namespace TheKrystalShip.KGSM.Tests.Models;

/// <summary>
/// The two pure helpers over the canonical structured port form: <see cref="PortMappingExtensions.Expand"/>
/// (range → individual (port, protocol) pairs, what the watchdog feeds and a per-port
/// conflict scan iterates) and <see cref="PortMappingExtensions.ToUfwSpec"/> (the inverse render to the
/// legacy UFW string).
/// </summary>
public class PortMappingTests
{
    [Fact]
    public void Expand_unrolls_a_range_into_individual_ports()
    {
        List<PortMapping> mappings = [new() { Start = 27015, End = 27017, Protocol = "udp" }];

        (int Port, string Protocol)[] expanded = [.. mappings.Expand()];

        Assert.Equal([(27015, "udp"), (27016, "udp"), (27017, "udp")], expanded);
    }

    [Fact]
    public void Expand_keeps_a_single_port_as_one_pair()
    {
        List<PortMapping> mappings =
        [
            new() { Start = 34197, End = 34197, Protocol = "tcp" },
            new() { Start = 34197, End = 34197, Protocol = "udp" },
        ];

        Assert.Equal([(34197, "tcp"), (34197, "udp")], mappings.Expand().ToArray());
    }

    [Fact]
    public void Expand_skips_inverted_ranges_defensively()
    {
        List<PortMapping> mappings = [new() { Start = 100, End = 99, Protocol = "tcp" }];

        Assert.Empty(mappings.Expand());
    }

    [Fact]
    public void Expand_of_empty_is_empty()
    {
        Assert.Empty(new List<PortMapping>().Expand());
    }

    [Fact]
    public void ToUfwSpec_renders_single_and_range_entries_pipe_joined()
    {
        List<PortMapping> mappings =
        [
            new() { Start = 27015, End = 27020, Protocol = "udp" },
            new() { Start = 27016, End = 27016, Protocol = "tcp" },
        ];

        // range -> start:end/proto ; single -> port/proto ; '|'-joined
        Assert.Equal("27015:27020/udp|27016/tcp", mappings.ToUfwSpec());
    }

    [Fact]
    public void ToUfwSpec_of_empty_is_empty_string()
    {
        Assert.Equal(string.Empty, new List<PortMapping>().ToUfwSpec());
    }
}

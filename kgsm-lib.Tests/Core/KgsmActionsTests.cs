using System.Reflection;
using System.Text.Json;

namespace TheKrystalShip.KGSM.Tests.Core;

/// <summary>
/// <see cref="KgsmActions"/> against the engine's own manifest: the same ids, both ways.
/// </summary>
/// <remarks>
/// <para>
/// The engine's manifest is written by hand in the <c>kgsm</c> repo, and these constants are what every
/// caller checks and requires by. An action added to one side only is either one nobody can check or one
/// the catalog never offers, so the test fails naming it.
/// </para>
/// <para>
/// The manifest is read from the engine checkout beside this one, or from <c>KGSM_ENGINE_CHECKOUT</c>.
/// Neither being there is a failure, not a pass: a comparison that could not be made has not shown
/// that the two agree.
/// </para>
/// </remarks>
public sealed class KgsmActionsTests
{
    private const string ManifestPath = "deploy/kgsm.actions.json";
    private const string CheckoutVariable = "KGSM_ENGINE_CHECKOUT";

    private static string Manifest()
    {
        string? configured = Environment.GetEnvironmentVariable(CheckoutVariable);
        if (!string.IsNullOrEmpty(configured))
            return Path.Combine(configured, ManifestPath);

        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "kgsm-lib.sln")))
            {
                string sibling = Path.Combine(dir.Parent!.FullName, "kgsm", ManifestPath);
                if (File.Exists(sibling))
                    return sibling;
            }
        }

        throw new InvalidOperationException(
            $"The engine's action manifest was not found beside this checkout (../kgsm/{ManifestPath}). " +
            $"Set {CheckoutVariable} to a kgsm checkout: without it, nothing says KgsmActions agrees with the engine.");
    }

    private static (string Component, string[] Actions) Engine()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Manifest()));
        JsonElement root = manifest.RootElement;

        string component = root.GetProperty("component").GetString()!;
        return (component, [.. root.GetProperty("actions").EnumerateArray()
            .Select(a => $"{component}:{a.GetProperty("id").GetString()}")
            .Order(StringComparer.Ordinal)]);
    }

    private static string[] Mirrored() =>
    [
        .. typeof(KgsmActions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name != nameof(KgsmActions.Component))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal),
    ];

    [Fact]
    public void EveryEngineActionHasAConstant()
    {
        string[] missing = [.. Engine().Actions.Except(Mirrored())];

        Assert.True(missing.Length == 0,
            $"The engine declares {string.Join(", ", missing)} and KgsmActions has no constant for it.");
    }

    [Fact]
    public void EveryConstantIsAnEngineAction()
    {
        string[] extra = [.. Mirrored().Except(Engine().Actions)];

        Assert.True(extra.Length == 0,
            $"KgsmActions names {string.Join(", ", extra)}, which the engine's manifest does not declare.");
    }

    [Fact]
    public void TheNamespaceIsTheEngines() =>
        Assert.Equal(KgsmActions.Component, Engine().Component);

    [Fact]
    public void NoTwoConstantsNameOneAction()
    {
        string[] mirrored = Mirrored();

        Assert.Equal(mirrored.Length, mirrored.Distinct(StringComparer.Ordinal).Count());
    }
}

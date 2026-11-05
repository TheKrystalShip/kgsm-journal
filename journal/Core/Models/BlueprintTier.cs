using System.Text.Json.Serialization;

namespace TheKrystalShip.KGSM.Core.Models;

/// <summary>
/// Which of the engine's two blueprint directories a file lives in. A <see cref="User"/> blueprint
/// shadows a same-named <see cref="System"/> one.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BlueprintTier>))]
public enum BlueprintTier
{
    /// <summary>The writable user blueprints directory.</summary>
    User,

    /// <summary>The read-only blueprints directory shipped with the engine.</summary>
    System,
}

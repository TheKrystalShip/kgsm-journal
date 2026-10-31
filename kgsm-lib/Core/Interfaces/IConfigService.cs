using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Interface for managing KGSM configuration.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Gets a configuration value by key.
    /// </summary>
    /// <param name="key">The configuration key to retrieve.</param>
    /// <returns>
    /// The configuration value for the specified key, or null if not found.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when key is null or whitespace.</exception>
    string? Get(string key);

    /// <summary>
    /// Sets a configuration value.
    /// </summary>
    /// <param name="key">The configuration key to set.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>
    /// Result of the set command execution.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when key or value is null or whitespace.</exception>
    KgsmResult Set(string key, string value);

    /// <summary>
    /// Lists all configuration values.
    /// </summary>
    /// <returns>
    /// A dictionary of all configuration key-value pairs, or empty if none found.
    /// </returns>
    Dictionary<string, string> List();

    /// <summary>
    /// Resets all configuration values to their defaults.
    /// </summary>
    /// <returns>
    /// Result of the reset command execution.
    /// </returns>
    KgsmResult Reset();

    /// <summary>
    /// Validates the current configuration.
    /// </summary>
    /// <returns>
    /// Result of the validate command execution.
    /// IsSuccess will be true if configuration is valid.
    /// </returns>
    KgsmResult Validate();

    /// <summary>
    /// Merges the current configuration with updated defaults.
    /// Preserves user customizations while adding new keys and commenting deprecated ones.
    /// Creates a numbered backup before merging.
    /// </summary>
    /// <returns>Result of the merge operation.</returns>
    KgsmResult Merge();

    /// <summary>
    /// Rolls back the configuration to a previous backup.
    /// Creates a safety backup of the current configuration before rolling back.
    /// </summary>
    /// <param name="generation">The backup generation to restore (0-9, where 0 is the most recent). Default is 0.</param>
    /// <returns>Result of the rollback operation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when generation is not between 0 and 9.</exception>
    KgsmResult Rollback(int generation = 0);

    /// <summary>
    /// Shows the differences between the current configuration and a backup.
    /// Returns unified diff format output.
    /// </summary>
    /// <param name="generation">The backup generation to compare with (0-9, where 0 is the most recent). Default is 0.</param>
    /// <returns>Result containing the diff output.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when generation is not between 0 and 9.</exception>
    KgsmResult Diff(int generation = 0);
}

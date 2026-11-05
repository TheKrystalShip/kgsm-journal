using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Keeps one journal position per producer in a single JSON file.
/// </summary>
/// <remarks>
/// <para>
/// One file rather than one per producer, so a consumer's whole reading position saves and loads as a
/// unit. Producers are added and removed by editing the map, and a producer with no entry is a cold
/// start for that journal alone — the others keep their positions.
/// </para>
/// <para>
/// The same write-then-rename as the single-cursor store: a crash mid-save leaves the previous map
/// intact rather than a half-written one, and the worst case is re-delivering events already handled.
/// The default store for a consumer with nowhere better; one that owns a database should keep these
/// beside what it derives from the events.
/// </para>
/// </remarks>
public sealed class FileFederatedEventCursorStore : IFederatedEventCursorStore
{
    private readonly string _path;
    private readonly ILogger<FileFederatedEventCursorStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a store backed by the file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Where the cursor map is kept.</param>
    /// <param name="logger">The logger to use.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is blank.</exception>
    public FileFederatedEventCursorStore(string path, ILogger<FileFederatedEventCursorStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));

        _path = path;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask<EventCursor?> LoadAsync(string producer, CancellationToken token = default)
    {
        JournalProducer.Validate(producer, nameof(producer));

        Dictionary<string, EventCursor> map = await ReadAsync(token).ConfigureAwait(false);

        return map.TryGetValue(producer, out EventCursor? cursor)
               && !string.IsNullOrEmpty(cursor.Segment)
            ? cursor
            : null;
    }

    /// <inheritdoc/>
    public async ValueTask SaveAsync(
        string producer, EventCursor cursor, CancellationToken token = default)
    {
        JournalProducer.Validate(producer, nameof(producer));
        ArgumentNullException.ThrowIfNull(cursor, nameof(cursor));

        // Read-modify-write on one file, so two producers advancing at once must not race: without
        // the gate the later save would drop the earlier one's position and silently replay it.
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Dictionary<string, EventCursor> map = await ReadAsync(token).ConfigureAwait(false);
            map[producer] = cursor;

            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(map, JournalJsonContext.Default.DictionaryStringEventCursor);

            string temporary = _path + ".tmp";
            await File.WriteAllTextAsync(temporary, json, token).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The stored map, or an empty one when there is nothing readable to load.
    /// </summary>
    private async ValueTask<Dictionary<string, EventCursor>> ReadAsync(CancellationToken token)
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            string json = await File.ReadAllTextAsync(_path, token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(json))
                return [];

            return JsonSerializer.Deserialize(json, JournalJsonContext.Default.DictionaryStringEventCursor)
                   ?? [];
        }
        catch (JsonException ex)
        {
            // A corrupt map is a cold start for every journal, reported rather than silently
            // ignored: the consumer is about to replay or jump to tail on all of them, and an
            // operator reading the log deserves to know which and why.
            _logger.LogError(ex,
                "Federated event cursor file {Path} is not readable JSON — every journal starts cold",
                _path);
            return [];
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Could not read the federated event cursor file {Path}", _path);
            return [];
        }
    }
}

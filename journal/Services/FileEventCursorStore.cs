using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Keeps a consumer's journal position in a small JSON file. The default store for a consumer
/// with nowhere better to put it; one that owns a database should store the cursor there
/// instead, next to whatever it derives from the events.
/// </summary>
public sealed class FileEventCursorStore : IEventCursorStore
{
    private readonly string _path;
    private readonly ILogger<FileEventCursorStore> _logger;

    /// <summary>
    /// Initializes a store backed by the file named in <paramref name="options"/>.
    /// </summary>
    /// <param name="options">KGSM options supplying <see cref="JournalReadOptions.EventCursorPath"/>.</param>
    /// <param name="logger">The logger to use.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the options carry no cursor path.</exception>
    public FileEventCursorStore(JournalReadOptions options, ILogger<FileEventCursorStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options, nameof(options));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        if (string.IsNullOrWhiteSpace(options.EventCursorPath))
            throw new ArgumentException("Event cursor path cannot be null, empty, or whitespace.", nameof(options));

        _path = options.EventCursorPath;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask<EventCursor?> LoadAsync(CancellationToken token = default)
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            string json = await File.ReadAllTextAsync(_path, token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(json))
                return null;

            EventCursor? cursor = JsonSerializer.Deserialize(json, JournalJsonContext.Default.EventCursor);

            return string.IsNullOrEmpty(cursor?.Segment) ? null : cursor;
        }
        catch (JsonException ex)
        {
            // A corrupt cursor file is a cold start, reported rather than silently ignored:
            // the consumer is about to see either a replay or a jump to tail, and an operator
            // reading the log deserves to know which and why.
            _logger.LogError(ex, "Event cursor file {Path} is not readable JSON — starting cold", _path);
            return null;
        }
    }

    /// <inheritdoc/>
    public async ValueTask SaveAsync(EventCursor cursor, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(cursor, nameof(cursor));

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(cursor, JournalJsonContext.Default.EventCursor);

        // Write-then-rename: a crash mid-save leaves the previous cursor intact instead of a
        // half-written one, and the worst case is re-delivering events already handled.
        string temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, json, token).ConfigureAwait(false);
        File.Move(temporary, _path, overwrite: true);
    }
}

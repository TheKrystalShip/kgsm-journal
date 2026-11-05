using System.Text.Json;

namespace TheKrystalShip.KGSM.Events;

/// <summary>Reads one field of a payload by its name, for a surface that is driven by the catalog's field list.</summary>
public static class EventPayloads
{
    /// <summary>
    /// The text of one top-level scalar field of <paramref name="data"/>: a string as written, a number in
    /// its written form. Null when the payload is null, declares no such field, or holds a non-scalar there.
    /// </summary>
    /// <param name="data">The payload, as the registered type its event names.</param>
    /// <param name="field">The field's JSON name.</param>
    public static string? ScalarText(KgsmEventDataBase? data, string field)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        if (data is null) return null;

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            data, JournalJsonContext.Default.GetTypeInfo(data.GetType())
                ?? throw new InvalidOperationException($"{data.GetType().Name} is not registered in {nameof(JournalJsonContext)}."));

        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool wanted = reader.ValueTextEquals(field);
            reader.Read();

            if (wanted)
            {
                return reader.TokenType switch
                {
                    JsonTokenType.String => reader.GetString(),
                    JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.ValueSpan),
                    _ => null,
                };
            }

            reader.Skip();
        }

        return null;
    }
}

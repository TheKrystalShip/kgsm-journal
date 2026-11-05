using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TheKrystalShip.KGSM.Events;

/// <summary>
/// Reads and writes the journal envelope with its payload as the registered type its event names.
/// </summary>
/// <remarks>
/// The payload's type is chosen by <c>EventType</c>, through the catalog's payload type for the event's
/// current name (a name an earlier build wrote is resolved first). A name the catalog does not classify
/// reads as <see cref="UnrecognizedEventData"/>. The payload may come before the type in a line, so the
/// reader's position at the payload is kept and the payload is read once the whole line has been seen.
/// Every payload type is registered in <see cref="JournalJsonContext"/>; nothing here is reflection.
/// Property names match without regard to case, as the rest of the journal's reading does.
/// </remarks>
public sealed class EventWrapperConverter : JsonConverter<EventWrapper>
{
    /// <inheritdoc/>
    public override EventWrapper? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A journal line is a JSON object.");

        var wrapper = new EventWrapper();
        Utf8JsonReader payload = default;
        bool hasPayload = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a property name.");

            string name = reader.GetString() ?? string.Empty;
            reader.Read();

            if (Is(name, "Id")) wrapper.Id = ReadString(ref reader);
            else if (Is(name, "EventType")) wrapper.EventType = ReadString(ref reader) ?? string.Empty;
            else if (Is(name, "Data"))
            {
                if (reader.TokenType == JsonTokenType.Null) continue;
                payload = reader;
                hasPayload = true;
                reader.Skip();
            }
            else if (Is(name, "Severity")) wrapper.Severity = ReadString(ref reader);
            else if (Is(name, "Outcome")) wrapper.Outcome = ReadString(ref reader);
            else if (Is(name, "Summary")) wrapper.Summary = ReadString(ref reader);
            else if (Is(name, "Timestamp"))
                wrapper.Timestamp = reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTimeOffset();
            else if (Is(name, "Actor")) wrapper.Actor = ReadString(ref reader);
            else if (Is(name, "Origin")) wrapper.Origin = ReadString(ref reader);
            else if (Is(name, "Hostname")) wrapper.Hostname = ReadString(ref reader);
            else if (Is(name, "KGSMVersion")) wrapper.KgsmVersion = ReadString(ref reader);
            else if (Is(name, "V"))
                wrapper.SchemaVersion = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt32();
            else if (Is(name, "ProducerVersion")) wrapper.ProducerVersion = ReadString(ref reader);
            else if (Is(name, "OpId")) wrapper.OpId = ReadString(ref reader);
            else if (Is(name, "RunId")) wrapper.RunId = ReadString(ref reader);
            else if (Is(name, "During")) wrapper.During = ReadStrings(ref reader);
            else reader.Skip();
        }

        if (hasPayload)
            wrapper.Data = (KgsmEventDataBase?)JsonSerializer.Deserialize(ref payload, PayloadTypeInfo(wrapper.EventType));

        return wrapper;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EventWrapper value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.SchemaVersion is { } version) writer.WriteNumber("V", version);
        if (value.Id is not null) writer.WriteString("Id", value.Id);
        writer.WriteString("EventType", value.EventType);

        if (value.Data is { } data)
        {
            writer.WritePropertyName("Data");
            JsonSerializer.Serialize(writer, data, TypeInfoOf(data.GetType()));
        }

        if (value.Severity is not null) writer.WriteString("Severity", value.Severity);
        if (value.Outcome is not null) writer.WriteString("Outcome", value.Outcome);
        if (value.Summary is not null) writer.WriteString("Summary", value.Summary);
        if (value.Timestamp is { } timestamp) writer.WriteString("Timestamp", timestamp);
        if (value.Actor is not null) writer.WriteString("Actor", value.Actor);
        if (value.Origin is not null) writer.WriteString("Origin", value.Origin);
        if (value.Hostname is not null) writer.WriteString("Hostname", value.Hostname);
        if (value.KgsmVersion is not null) writer.WriteString("KGSMVersion", value.KgsmVersion);
        if (value.ProducerVersion is not null) writer.WriteString("ProducerVersion", value.ProducerVersion);
        if (value.OpId is not null) writer.WriteString("OpId", value.OpId);
        if (value.RunId is not null) writer.WriteString("RunId", value.RunId);

        if (value.During is { } during)
        {
            writer.WriteStartArray("During");
            foreach (string operation in during) writer.WriteStringValue(operation);
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static JsonTypeInfo PayloadTypeInfo(string eventType)
    {
        Type payload = KgsmEventCatalog.Describe(LegacyEventNames.Canonical(eventType)).PayloadType
            ?? typeof(UnrecognizedEventData);
        return TypeInfoOf(payload);
    }

    private static JsonTypeInfo TypeInfoOf(Type type) =>
        JournalJsonContext.Default.GetTypeInfo(type)
            ?? throw new JsonException($"The payload type {type.Name} is not registered in {nameof(JournalJsonContext)}.");

    private static bool Is(string name, string expected) =>
        string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Null => null,
        JsonTokenType.String => reader.GetString(),
        _ => throw new JsonException($"Expected a string, found {reader.TokenType}."),
    };

    private static string[]? ReadStrings(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an array of strings.");

        var items = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected a string.");
            items.Add(reader.GetString() ?? string.Empty);
        }

        return [.. items];
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace backend.main.features.events.series.contracts.requests;

/// <summary>
/// Reads a weekday list written either as names (<c>["Tuesday","Thursday"]</c>) or as numbers
/// (<c>[2,4]</c>), and always writes names.
/// <para>
/// This application registers no global <see cref="JsonStringEnumConverter"/>, and a property-level
/// one cannot be applied to a <see cref="List{T}"/> — the factory only handles the enum type itself.
/// Without this, the surrounding rule would accept <c>"frequency": "Weekly"</c> but reject
/// <c>"byWeekdays": ["Tuesday"]</c>, which is a confusing asymmetry for anyone calling the API by
/// hand.
/// </para>
/// </summary>
public sealed class WeekdayListJsonConverter : JsonConverter<List<DayOfWeek>>
{
    public override List<DayOfWeek> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("byWeekdays must be an array of weekday names or numbers.");

        var days = new List<DayOfWeek>();

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndArray:
                    return days;

                case JsonTokenType.Number:
                    var numeric = reader.GetInt32();

                    if (numeric is < 0 or > 6)
                        throw new JsonException($"'{numeric}' is not a valid weekday (0-6).");

                    days.Add((DayOfWeek)numeric);
                    break;

                case JsonTokenType.String:
                    var name = reader.GetString();

                    if (!Enum.TryParse<DayOfWeek>(name, ignoreCase: true, out var parsed))
                        throw new JsonException($"'{name}' is not a valid weekday.");

                    days.Add(parsed);
                    break;

                default:
                    throw new JsonException("byWeekdays entries must be weekday names or numbers.");
            }
        }

        throw new JsonException("byWeekdays array was not closed.");
    }

    public override void Write(Utf8JsonWriter writer, List<DayOfWeek> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var day in value)
            writer.WriteStringValue(day.ToString());

        writer.WriteEndArray();
    }
}

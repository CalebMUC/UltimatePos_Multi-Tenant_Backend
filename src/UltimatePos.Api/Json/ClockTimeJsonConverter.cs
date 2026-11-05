using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltimatePos.Api.Json;

/// <summary>
/// A time of day as a clock shows it: reads "17:00" (what an HTML time input sends) or "17:00:00", writes "17:00". The default
/// converter only takes "17:00:00". Used by the selling hours of age-restricted products.
/// </summary>
public sealed class ClockTimeJsonConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] Formats = { "HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss", "HH:mm:ss.FFFFFFF" };

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is not null && TimeOnly.TryParseExact(text.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;
        throw new JsonException("Give the time as HH:mm, e.g. 17:00.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
}

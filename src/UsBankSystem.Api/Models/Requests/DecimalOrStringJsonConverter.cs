using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsBankSystem.Api.Models.Requests;

/// <summary>
/// Reads a decimal from either a JSON number or a JSON string (e.g. KLIK sends amounts
/// as quoted strings like "25.00"). Always writes as a JSON number.
/// </summary>
public class DecimalOrStringJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return decimal.Parse(value!, CultureInfo.InvariantCulture);
        }

        return reader.GetDecimal();
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

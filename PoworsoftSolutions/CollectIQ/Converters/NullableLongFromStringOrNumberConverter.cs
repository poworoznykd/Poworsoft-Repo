using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class NullableLongFromStringOrNumberConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt64(out long n))
                {
                    return n;
                }

                double d = reader.GetDouble();
                return (long)Math.Round(d);
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                string s = reader.GetString();

                if (string.IsNullOrWhiteSpace(s))
                {
                    return null;
                }

                s = s.Trim();

                if (s.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("na", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("-", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                s = s.Replace(",", "");

                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                {
                    return parsed;
                }

                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDouble))
                {
                    return (long)Math.Round(parsedDouble);
                }

                return null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
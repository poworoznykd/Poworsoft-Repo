using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class NullableIntFromStringOrNumberConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt32(out int n))
                {
                    return n;
                }

                // If it's a number but not an int (rare), try double and cast safely.
                double d = reader.GetDouble();
                return (int)Math.Round(d);
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                string s = reader.GetString();

                if (string.IsNullOrWhiteSpace(s))
                {
                    return null;
                }

                s = s.Trim();

                // handle "N/A", etc.
                if (s.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("na", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("-", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                // remove commas: "1,234"
                s = s.Replace(",", "");

                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    return parsed;
                }

                // try decimal-ish string
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDouble))
                {
                    return (int)Math.Round(parsedDouble);
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

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
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
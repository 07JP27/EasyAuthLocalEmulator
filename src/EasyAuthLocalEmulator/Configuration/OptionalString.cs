using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyAuthLocalEmulator.Configuration;

[JsonConverter(typeof(OptionalStringJsonConverter))]
public readonly record struct OptionalString(bool IsSpecified, string? Value)
{
    public static OptionalString Specified(string? value)
    {
        return new OptionalString(IsSpecified: true, value);
    }
}

public sealed class OptionalStringJsonConverter : JsonConverter<OptionalString>
{
    public override bool HandleNull => true;

    public override OptionalString Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => OptionalString.Specified(null),
            JsonTokenType.String => OptionalString.Specified(reader.GetString()),
            _ => throw new JsonException("Claim mappings must be strings or null.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        OptionalString value,
        JsonSerializerOptions options)
    {
        if (value.Value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value);
        }
    }
}

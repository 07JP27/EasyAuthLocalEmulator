using System.Text.Json.Serialization;

namespace EasyAuthLocalEmulator.Auth;

public sealed record EasyAuthClaim(
    [property: JsonPropertyName("typ")] string Type,
    [property: JsonPropertyName("val")] string Value);

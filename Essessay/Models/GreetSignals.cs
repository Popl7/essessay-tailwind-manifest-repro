using System.Text.Json.Serialization;

namespace Essessay.Models;

// Mirrors the client-side Datastar signals POSTed to /api/greet.
// JsonPropertyName keeps it robust regardless of the serializer's casing config.
public class GreetSignals
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("style")] public string? Style { get; set; }
}

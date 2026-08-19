using System.Text.Json.Serialization;

namespace Essessay.Models;

// The `draft` sub-object of the client's signal tree. Bound with
// [FromSignals(Path = "draft")] instead of ReadSignalsAsync<T>().
public class DraftSignals
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
}

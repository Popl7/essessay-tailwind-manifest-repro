using System.Text.Json.Serialization;

namespace Essessay.Models;

public class TodoSignals
{
    [JsonPropertyName("newTodo")] public string? NewTodo { get; set; }
}

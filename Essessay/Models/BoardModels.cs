using System.Text.Json.Serialization;

namespace Essessay.Models;

public enum BoardColumn
{
    Todo,
    Doing,
    Done
}

public record BoardCard(int Id, string Text, BoardColumn Column, string Author, DateTimeOffset UpdatedAt);

public record BoardColumnViewModel(BoardColumn Column, IReadOnlyList<BoardCard> Cards);

/// <summary>One connected client, as shown in the presence bar.</summary>
public record BoardViewer(string ConnectionId, string Name, bool IsTyping);

public record BoardPresenceViewModel(IReadOnlyList<BoardViewer> Viewers);

// The client's signal tree. `me` is assigned by the server when the stream opens,
// so a mutation can say who made it without trusting a form field.
public class BoardSignals
{
    [JsonPropertyName("newCard")] public string? NewCard { get; set; }
    [JsonPropertyName("rename")] public string? Rename { get; set; }
    [JsonPropertyName("me")] public BoardMeSignals? Me { get; set; }
}

public class BoardMeSignals
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

using Essessay.Models;

namespace Essessay.Services;

public interface IBoardStore
{
    Task<IReadOnlyList<BoardCard>> AllAsync();
    Task<BoardCard?> GetAsync(int id);
    Task<BoardCard> AddAsync(string text, string author);

    /// <summary>Moves a card one column left (-1) or right (+1). Null if it can't move.</summary>
    Task<BoardCard?> MoveAsync(int id, int direction, string movedBy);

    Task<bool> RemoveAsync(int id);
}

/// <summary>The cards every client shares. Seeded once, per store.</summary>
public static class BoardSeed
{
    public const string Author = "seed";

    /// <summary>Text, and how many columns to the right of Todo it starts.</summary>
    public static readonly (string Text, int Column)[] Cards =
    [
        ("Read the Datastar docs", 0),
        ("Open this page in a second tab", 0),
        ("Ship the demo pages", 1),
        ("Learn signals", 2)
    ];
}

// Single-instance board (singleton). Resets on restart.
public class InMemoryBoardStore : IBoardStore
{
    private readonly object _lock = new();
    private readonly List<BoardCard> _cards = [];
    private int _nextId;

    public InMemoryBoardStore()
    {
        foreach (var (text, column) in BoardSeed.Cards)
        {
            var card = AddAsync(text, BoardSeed.Author).GetAwaiter().GetResult();
            for (var step = 0; step < column; step++) MoveAsync(card.Id, 1, BoardSeed.Author).GetAwaiter().GetResult();
        }
    }

    public Task<IReadOnlyList<BoardCard>> AllAsync()
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<BoardCard>>(_cards.ToList());
    }

    public Task<BoardCard?> GetAsync(int id)
    {
        lock (_lock) return Task.FromResult(_cards.FirstOrDefault(card => card.Id == id));
    }

    public Task<BoardCard> AddAsync(string text, string author)
    {
        lock (_lock)
        {
            var card = new BoardCard(++_nextId, text, BoardColumn.Todo, author, DateTimeOffset.Now);
            _cards.Add(card);
            return Task.FromResult(card);
        }
    }

    public Task<BoardCard?> MoveAsync(int id, int direction, string movedBy)
    {
        lock (_lock)
        {
            var index = _cards.FindIndex(card => card.Id == id);
            if (index < 0) return Task.FromResult<BoardCard?>(null);

            var target = (int)_cards[index].Column + Math.Sign(direction);
            if (target < 0 || target > (int)BoardColumn.Done) return Task.FromResult<BoardCard?>(null);

            _cards[index] = _cards[index] with
            {
                Column = (BoardColumn)target,
                Author = movedBy,
                UpdatedAt = DateTimeOffset.Now
            };
            return Task.FromResult<BoardCard?>(_cards[index]);
        }
    }

    public Task<bool> RemoveAsync(int id)
    {
        lock (_lock) return Task.FromResult(_cards.RemoveAll(card => card.Id == id) > 0);
    }
}

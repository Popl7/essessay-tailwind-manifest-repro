using System.Text.Json;
using Essessay.Models;
using StackExchange.Redis;

namespace Essessay.Services;

/// <summary>
/// The cards, shared by every instance. A hash keyed by card id, with one counter
/// handing out ids. Reads are last-write-wins — fine for a demo board, but a real
/// one would want a WATCH/MULTI or a Lua script around the move.
/// </summary>
public class RedisBoardStore : IBoardStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisKey _cards;
    private readonly RedisKey _sequence;
    private readonly RedisKey _seeded;

    public RedisBoardStore(IConnectionMultiplexer redis, BoardRedisOptions options)
    {
        _redis = redis;
        _cards = options.Prefix + "board:cards";
        _sequence = options.Prefix + "board:cards:seq";
        _seeded = options.Prefix + "board:seeded";

        SeedAsync().GetAwaiter().GetResult();
    }

    private IDatabase Db => _redis.GetDatabase();

    public async Task<IReadOnlyList<BoardCard>> AllAsync()
    {
        var entries = await Db.HashGetAllAsync(_cards);

        return entries
            .Select(entry => JsonSerializer.Deserialize<BoardCard>(entry.Value.ToString()))
            .Where(card => card is not null)
            .Select(card => card!)
            .OrderBy(card => card.Id)
            .ToList();
    }

    public async Task<BoardCard?> GetAsync(int id)
    {
        var value = await Db.HashGetAsync(_cards, id);
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<BoardCard>(value.ToString());
    }

    public async Task<BoardCard> AddAsync(string text, string author)
    {
        var id = (int)await Db.StringIncrementAsync(_sequence);
        var card = new BoardCard(id, text, BoardColumn.Todo, author, DateTimeOffset.Now);

        await Db.HashSetAsync(_cards, id, JsonSerializer.Serialize(card));
        return card;
    }

    public async Task<BoardCard?> MoveAsync(int id, int direction, string movedBy)
    {
        var card = await GetAsync(id);
        if (card is null) return null;

        var target = (int)card.Column + Math.Sign(direction);
        if (target < 0 || target > (int)BoardColumn.Done) return null;

        var moved = card with
        {
            Column = (BoardColumn)target,
            Author = movedBy,
            UpdatedAt = DateTimeOffset.Now
        };

        await Db.HashSetAsync(_cards, id, JsonSerializer.Serialize(moved));
        return moved;
    }

    public async Task<bool> RemoveAsync(int id) => await Db.HashDeleteAsync(_cards, id);

    /// <summary>Whichever instance starts first wins the seed; the others skip it.</summary>
    private async Task SeedAsync()
    {
        if (!await Db.StringSetAsync(_seeded, "1", when: When.NotExists)) return;

        foreach (var (text, column) in BoardSeed.Cards)
        {
            var card = await AddAsync(text, BoardSeed.Author);
            for (var step = 0; step < column; step++) await MoveAsync(card.Id, 1, BoardSeed.Author);
        }
    }
}

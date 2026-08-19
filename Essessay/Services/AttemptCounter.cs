using System.Collections.Concurrent;

namespace Essessay.Services;

public interface IAttemptCounter
{
    /// <summary>Returns a monotonically increasing count for <paramref name="key"/>, starting at 1.</summary>
    int Next(string key);
}

// Process-wide demo counters (singleton). Used to make the "flaky" endpoint fail
// predictably and to give the polling demo a number that changes on every request.
public class AttemptCounter : IAttemptCounter
{
    private readonly ConcurrentDictionary<string, int> _counts = new();

    public int Next(string key) => _counts.AddOrUpdate(key, 1, (_, current) => current + 1);
}

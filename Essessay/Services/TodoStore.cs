using System.Collections.Concurrent;

namespace Essessay.Services;

public record Todo(int Id, string Text, bool Done);

public interface ITodoStore
{
    IReadOnlyList<Todo> All();
    Todo Add(string text);
    Todo? Toggle(int id);
    bool Remove(int id);
}

// In-memory demo store (singleton). Not persisted — resets on restart.
public class TodoStore : ITodoStore
{
    private readonly object _lock = new();
    private readonly List<Todo> _todos = [];
    private int _nextId;

    public TodoStore()
    {
        Add("Learn Datastar");
        Add("Try view transitions");
    }

    public IReadOnlyList<Todo> All()
    {
        lock (_lock) return _todos.ToList();
    }

    public Todo Add(string text)
    {
        lock (_lock)
        {
            var todo = new Todo(++_nextId, text, false);
            _todos.Add(todo);
            return todo;
        }
    }

    public Todo? Toggle(int id)
    {
        lock (_lock)
        {
            var i = _todos.FindIndex(t => t.Id == id);
            if (i < 0) return null;
            _todos[i] = _todos[i] with { Done = !_todos[i].Done };
            return _todos[i];
        }
    }

    public bool Remove(int id)
    {
        lock (_lock) return _todos.RemoveAll(t => t.Id == id) > 0;
    }
}

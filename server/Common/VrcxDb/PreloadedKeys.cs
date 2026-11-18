namespace VRCX.Common.VrcxDb;

/// <summary>
/// The keys a unit of work has already looked up in one query (a sync loading every friend's user
/// row before its loop, say). The rows that exist are tracked; a key listed here and not tracked is
/// known not to exist, so the per-item lookup that follows needs no query of its own.
/// </summary>
public sealed class PreloadedKeys
{
    private readonly HashSet<(Type, string)> _keys = [];

    public void Add<T>(IEnumerable<string> keys)
    {
        foreach (var key in keys) _keys.Add((typeof(T), key));
    }

    public bool Contains<T>(string key) => _keys.Contains((typeof(T), key));

    public void Clear() => _keys.Clear();
}

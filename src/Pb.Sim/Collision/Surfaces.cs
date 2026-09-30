namespace Pb.Sim.Collision;

/// <summary>Index of a surface material (inflatable, turf, player gear, …) defined in break_model.jsonc.</summary>
public readonly record struct SurfaceId(byte Value)
{
    public override string ToString() => Value.ToString();
}

/// <summary>Maps surface names from data files to compact ids.</summary>
public sealed class SurfaceRegistry
{
    private readonly List<string> _names = new();
    private readonly Dictionary<string, SurfaceId> _ids = new(StringComparer.Ordinal);

    public SurfaceRegistry(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            if (_ids.ContainsKey(name))
            {
                throw new ArgumentException($"Duplicate surface '{name}'.");
            }

            if (_names.Count == byte.MaxValue)
            {
                throw new ArgumentException("Too many surfaces (max 255).");
            }

            _ids[name] = new SurfaceId((byte)_names.Count);
            _names.Add(name);
        }
    }

    public int Count => _names.Count;

    public IReadOnlyList<string> Names => _names;

    public bool TryGet(string name, out SurfaceId id) => _ids.TryGetValue(name, out id);

    public SurfaceId Get(string name) =>
        _ids.TryGetValue(name, out SurfaceId id)
            ? id
            : throw new KeyNotFoundException($"unknown surface '{name}' (known: {string.Join(", ", _names)})");

    public string NameOf(SurfaceId id) => id.Value < _names.Count ? _names[id.Value] : $"#{id.Value}";
}

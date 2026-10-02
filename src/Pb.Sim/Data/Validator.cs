using System.Globalization;
using System.Numerics;

namespace Pb.Sim.Data;

/// <summary>
/// Collects value errors for one data file. Keys are passed as C# property names and reported as
/// the JSON keys the designer actually typed, with a path for nested objects and arrays.
/// </summary>
public sealed class Validator
{
    private readonly List<string> _errors;
    private readonly string _prefix;

    public Validator(string file)
        : this(file, new List<string>(), string.Empty)
    {
    }

    private Validator(string file, List<string> errors, string prefix)
    {
        File = file;
        _errors = errors;
        _prefix = prefix;
    }

    public string File { get; }

    public IReadOnlyList<string> Errors => _errors;

    /// <summary>Validator for a nested object, e.g. <c>Scope(nameof(Ramping))</c> → "ramping.afterShots".</summary>
    public Validator Scope(string property) => new(File, _errors, _prefix + Jsonc.KeyOf(property) + ".");

    /// <summary>Validator for an array element, e.g. "props[2].surface".</summary>
    public Validator Item(string property, int index) =>
        new(File, _errors, _prefix + Jsonc.KeyOf(property) + "[" + index.ToString(CultureInfo.InvariantCulture) + "].");

    public void Error(string property, string message) => _errors.Add(Key(property) + ": " + message);

    public void Positive(string property, double value)
    {
        if (!(value > 0) || double.IsInfinity(value))
        {
            Error(property, $"must be > 0 (got {Format(value)})");
        }
    }

    public void NonNegative(string property, double value)
    {
        if (!(value >= 0) || double.IsInfinity(value))
        {
            Error(property, $"must be ≥ 0 (got {Format(value)})");
        }
    }

    public void InRange(string property, double value, double min, double max)
    {
        if (!(value >= min && value <= max))
        {
            Error(property, $"must be in [{Format(min)}, {Format(max)}] (got {Format(value)})");
        }
    }

    public void NotEmpty(string property, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Error(property, "must not be empty");
        }
    }

    public void Vector(string property, float[]? value, int length = 3)
    {
        if (value is null || value.Length != length)
        {
            Error(property, $"must be an array of {length} numbers");
            return;
        }

        foreach (float f in value)
        {
            if (!float.IsFinite(f))
            {
                Error(property, "must contain only finite numbers");
                return;
            }
        }
    }

    public void ThrowIfErrors()
    {
        if (_errors.Count > 0)
        {
            throw new DataException(File, _errors);
        }
    }

    public static Vector3 ToVector3(float[]? value) =>
        value is { Length: 3 } ? new Vector3(value[0], value[1], value[2]) : Vector3.Zero;

    private string Key(string property) => _prefix + Jsonc.KeyOf(property);

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

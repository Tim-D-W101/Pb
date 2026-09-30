using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pb.Sim.Data;

/// <summary>Marks a data-file key that may be omitted. Every other key is required.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionalAttribute : Attribute
{
}

/// <summary>A data-file definition that can check its own values.</summary>
public interface IValidatable
{
    void Validate(Validator v);
}

/// <summary>
/// Loads JSON-with-comments data files. Keys are camelCase with a unit suffix
/// (<c>muzzleVelocity_mps</c>), unknown keys are rejected (typos fail loudly instead of being
/// silently ignored), and every key is required unless its property is marked [Optional].
/// </summary>
public static class Jsonc
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static T Load<T>(IDataSource source, string path)
        where T : IValidatable
    {
        string text = source.ReadAllText(path);
        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(text, Options);
        }
        catch (JsonException ex)
        {
            string where = ex.LineNumber is long line ? $" (line {line + 1})" : string.Empty;
            throw new DataException(path, Clean(ex.Message) + where);
        }

        if (value is null)
        {
            throw new DataException(path, "file is empty");
        }

        var validator = new Validator(path);
        value.Validate(validator);
        validator.ThrowIfErrors();
        return value;
    }

    public static string KeyOf(string propertyName) => JsonNamingPolicy.CamelCase.ConvertName(propertyName);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { RequireAllKeys },
            },
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly();
        return options;
    }

    private static void RequireAllKeys(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (JsonPropertyInfo property in info.Properties)
        {
            bool optional = property.AttributeProvider?.IsDefined(typeof(OptionalAttribute), inherit: true) == true;
            if (!optional && property.Set is not null)
            {
                property.IsRequired = true;
            }
        }
    }

    private static string Clean(string message)
    {
        // System.Text.Json appends "Path: $.x | LineNumber: n | BytePositionInLine: m." — keep the useful part.
        int cut = message.IndexOf(" Path:", StringComparison.Ordinal);
        string main = cut > 0 ? message[..cut] : message;
        int pathStart = message.IndexOf("Path: ", StringComparison.Ordinal);
        if (pathStart >= 0)
        {
            int pathEnd = message.IndexOf(" |", pathStart, StringComparison.Ordinal);
            string jsonPath = pathEnd > pathStart ? message[(pathStart + 6)..pathEnd] : message[(pathStart + 6)..];
            main += $" at {jsonPath}";
        }

        return main.Replace("Pb.Sim.Data.", string.Empty, StringComparison.Ordinal);
    }
}

namespace Pb.Sim.Data;

/// <summary>Where data files come from: the file system (tests, tools) or Godot's res:// (game).</summary>
public interface IDataSource
{
    /// <summary>Reads a file by its path relative to the data root, e.g. "markers/standard.jsonc".</summary>
    string ReadAllText(string path);
}

public sealed class FileSystemDataSource : IDataSource
{
    public FileSystemDataSource(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string ReadAllText(string path)
    {
        string full = Path.GetFullPath(Path.Combine(Root, path));
        if (!File.Exists(full))
        {
            throw new DataException(path, "file not found");
        }

        return File.ReadAllText(full);
    }

    /// <summary>Finds game/data by walking up from <paramref name="start"/> to the folder holding Pb.sln.</summary>
    public static FileSystemDataSource FindRepoData(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pb.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new DirectoryNotFoundException("Could not find Pb.sln above " + (start ?? AppContext.BaseDirectory));
        }

        return new FileSystemDataSource(Path.Combine(dir.FullName, "game", "data"));
    }
}

/// <summary>A data file is missing, malformed or has out-of-range values. The message names file and key.</summary>
public sealed class DataException : Exception
{
    public DataException(string file, string message)
        : base($"{file}: {message}")
    {
        File = file;
    }

    public DataException(string file, IReadOnlyList<string> errors)
        : base($"{file}: " + string.Join(Environment.NewLine + $"{file}: ", errors))
    {
        File = file;
    }

    public string File { get; }
}

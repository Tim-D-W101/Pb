using Godot;
using Pb.Sim.Data;

namespace Pb.Game.Core;

/// <summary>Reads data files from res://data (inside the exported .pck in builds).</summary>
public sealed class GodotDataSource : IDataSource
{
    public const string Root = "res://data/";

    public string ReadAllText(string path)
    {
        string full = Root + path;
        if (!FileAccess.FileExists(full))
        {
            throw new DataException(path, "file not found");
        }

        using FileAccess? file = FileAccess.Open(full, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            throw new DataException(path, $"cannot open ({FileAccess.GetOpenError()})");
        }

        return file.GetAsText();
    }
}

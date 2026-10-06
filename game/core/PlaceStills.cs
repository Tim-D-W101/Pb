using Godot;
using Pb.Game.Ui;
using Pb.Sim.Level;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --place-stills=DIR</c> (with <c>--level=ID</c>): takes the picture of each of the level's places that the menu
/// shows, from the viewpoint its <c>"still"</c> names (<see cref="PlaceSpec.Still"/>), and saves it as
/// <c>DIR/LEVEL_PLACE.jpg</c>, then quits. Run it under a real renderer (lavapipe in a container, see CLAUDE.md) into
/// <c>res://ui/places</c>, where the menu finds them; a missing picture just leaves the space empty. Nobody's in them.
/// </summary>
public partial class PlaceStills : Node
{
    /// <summary>Where the menu looks for the pictures.</summary>
    public const string Folder = "res://ui/places";

    /// <summary>The picture's size (px): wide and short, for the place column of the area card (twice its size on screen).</summary>
    public const int Width = 720, Height = 320;

    /// <summary>Frames each view is held before it's taken (shadows, probes and the exposure settle).</summary>
    private const int Settle = 30;

    private Camera3D _camera = null!;
    private LevelLayout _level = null!;
    private string _dir = "";
    private int _frame;

    /// <summary>The menu's picture of a place, if one's been taken.</summary>
    public static Texture2D? For(string levelId, string placeId)
    {
        string path = $"{Folder}/{levelId}_{placeId}.jpg";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    /// <param name="people">Everyone on the field: left out of the pictures, which are of the places.</param>
    public void Start(LevelLayout level, string dir, Hud hud, Node3D viewModel, System.Collections.Generic.IEnumerable<Node3D> people, float farClip)
    {
        foreach (Node3D person in people)
        {
            person.Visible = false;
        }

        _level = level;
        _dir = dir.StartsWith("res://", System.StringComparison.Ordinal) || dir.StartsWith("user://", System.StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(dir)
            : dir;
        DirAccess.MakeDirRecursiveAbsolute(_dir);
        hud.Visible = false;
        viewModel.Visible = false;
        _camera = new Camera3D { Name = "StillCamera", Fov = 62f, Far = farClip, Near = 0.05f };
        AddChild(_camera);
        _camera.MakeCurrent();
        GD.Print($"STILLS {level.Places.Count} places of {level.DisplayName} into {_dir}");
    }

    public override void _Process(double delta)
    {
        if (_level is null)
        {
            return;
        }

        int index = _frame / (Settle + 1);
        if (index >= _level.Places.Count)
        {
            GetTree().Quit();
            return;
        }

        PlaceSpec place = _level.Places[index];
        int step = _frame % (Settle + 1);
        if (step == 0)
        {
            Viewpoint view = place.Still;
            _camera.GlobalPosition = view.Position.ToGodot();
            _camera.Rotation = new Vector3(view.Pitch, view.Yaw, 0f);
        }
        else if (step == Settle)
        {
            Save(place);
        }

        _frame++;
    }

    /// <summary>The frame as drawn, cut to the picture's shape across its middle and scaled down to its size.</summary>
    private void Save(PlaceSpec place)
    {
        Image frame = GetViewport().GetTexture().GetImage();
        int w = frame.GetWidth(), h = frame.GetHeight();
        float aspect = Width / (float)Height;
        int cutH = Mathf.Min(h, Mathf.RoundToInt(w / aspect));
        int cutW = Mathf.Min(w, Mathf.RoundToInt(cutH * aspect));
        Image cut = frame.GetRegion(new Rect2I((w - cutW) / 2, (h - cutH) / 2, cutW, cutH));
        cut.Resize(Width, Height, Image.Interpolation.Lanczos);
        string path = $"{_dir}/{_level.Id}_{place.Id}.jpg";
        Error error = cut.SaveJpg(path, 0.85f);
        GD.Print(error == Error.Ok ? $"STILLS {place.Id}: {place.Still.Name} → {path}" : $"STILLS {place.Id}: couldn't save {path} ({error})");
    }
}

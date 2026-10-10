using Godot;

namespace Pb.Game.Platform;

/// <summary>Lets the platform do its work once a frame (searching for games now; a platform's callbacks later).</summary>
public partial class PlatformPump : Node
{
    private double _now;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _now += delta;
        Platforms.Current.Poll(_now);
    }
}

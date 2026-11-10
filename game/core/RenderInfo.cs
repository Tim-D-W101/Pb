using System.Globalization;
using Godot;

namespace Pb.Game.Core;

/// <summary>
/// One log line saying how the game is drawn (Phase 6): the renderer and its driver, the GPU, the 3D view's scale and how
/// it's scaled up, MSAA, the window and the interface's scale. Printed as the menu and each level start, so a phone's log
/// (<c>adb logcat -s godot</c>) says what it ran on.
/// </summary>
public static class RenderInfo
{
    public static string Line(Viewport viewport)
    {
        if (DisplayServer.GetName() == "headless")
        {
            // Nothing's drawn (CI, a dedicated server): Godot still names the project's renderer, but it isn't running.
            return string.Create(CultureInfo.InvariantCulture, $"RENDER: none, headless; {OS.GetName()} {OS.GetVersion()}, {OS.GetModelName()}");
        }

        string scaling = viewport.Scaling3DMode switch
        {
            Viewport.Scaling3DModeEnum.Fsr => "FSR 1.0",
            Viewport.Scaling3DModeEnum.Fsr2 => "FSR 2",
            Viewport.Scaling3DModeEnum.Bilinear => "bilinear",
            _ => viewport.Scaling3DMode.ToString(),
        };
        string msaa = viewport.Msaa3D switch
        {
            Viewport.Msaa.Disabled => "off",
            Viewport.Msaa.Msaa2X => "2x",
            Viewport.Msaa.Msaa4X => "4x",
            _ => viewport.Msaa3D.ToString(),
        };
        Vector2I window = DisplayServer.WindowGetSize();
        float contentScale = viewport is Window w ? w.ContentScaleFactor : 1f;
        return string.Create(CultureInfo.InvariantCulture,
            $"RENDER: {RenderingServer.GetCurrentRenderingMethod()} renderer ({RenderingServer.GetCurrentRenderingDriverName()}) on " +
            $"{RenderingServer.GetVideoAdapterName()} by {RenderingServer.GetVideoAdapterVendor()} ({RenderingServer.GetVideoAdapterApiVersion()}); " +
            $"3D at {viewport.Scaling3DScale * 100f:0} % ({scaling}), MSAA {msaa}; window {window.X} × {window.Y}, interface × {contentScale:0.00}; " +
            $"{OS.GetName()} {OS.GetVersion()}, {OS.GetModelName()}");
    }
}

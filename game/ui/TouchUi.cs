using System;
using System.Globalization;
using System.Linq;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ui;

/// <summary>
/// The touch screen (Phase 6): whether this is a phone or tablet, how many of the interface's pixels make a millimetre
/// on the screen, the screen's safe area, and the interface drawn bigger on a phone. <c>--touch</c> makes a computer act
/// as a phone (the touch controls up from the start, the interface scaled, the desktop's own settings left out), with
/// <c>--dpi=N</c> for the screen's density and <c>--safe-area=LEFT,TOP,RIGHT,BOTTOM</c> (screen pixels) for a notch.
/// </summary>
public static class TouchUi
{
    /// <summary>The menus' margins from the screen's edges on a phone (interface pixels), where the desktop's are wider.</summary>
    public const float PhoneMargin = 28f;

    /// <summary>A phone or tablet, or a computer told to act as one (<c>--touch</c>, and the touch demos).</summary>
    public static bool Phone => OS.HasFeature("mobile") || Args.Has("--touch") || Args.Has("--touch-demo") || Args.Has("--menu-touch-demo");

    /// <summary>The screen's density (dots per inch): <c>--dpi</c>, else the screen's own, else 96.</summary>
    public static float Dpi
    {
        get
        {
            if (float.TryParse(Args.Value("--dpi"), NumberStyles.Float, CultureInfo.InvariantCulture, out float given) && given >= 20f)
            {
                return given;
            }

            int screen = DisplayServer.GetName() == "headless" ? 0 : DisplayServer.ScreenGetDpi();
            return screen >= 20 ? screen : 96f;
        }
    }

    /// <summary>How many of the window's own pixels one of the interface's covers (its stretch, times the content scale).</summary>
    public static float CanvasScale(Window window)
    {
        Vector2 basis = window.ContentScaleSize;
        Vector2I size = window.Size;
        float stretch = basis.X > 0f && basis.Y > 0f ? Math.Min(size.X / basis.X, size.Y / basis.Y) : 1f;
        return Math.Max(0.01f, stretch * window.ContentScaleFactor);
    }

    /// <summary>The interface's pixels in a millimetre of the screen.</summary>
    public static float PxPerMm(Window window) => Dpi / 25.4f / CanvasScale(window);

    /// <summary>
    /// The safe area (clear of a notch, a camera hole, rounded corners and the system's bars) in the interface's pixels:
    /// <c>--safe-area</c>, else the phone's own, else (a computer) the whole window.
    /// </summary>
    public static Rect2 SafeArea(Window window)
    {
        Rect2 visible = window.GetVisibleRect();
        float scale = CanvasScale(window);
        float[] inset = Insets(window);
        return new Rect2(visible.Position + new Vector2(inset[0], inset[1]) / scale,
            visible.Size - new Vector2(inset[0] + inset[2], inset[1] + inset[3]) / scale);
    }

    /// <summary>The safe area's insets from the window's edges, in the window's pixels: left, top, right, bottom.</summary>
    private static float[] Insets(Window window)
    {
        if (Args.Value("--safe-area") is { } given)
        {
            float[] parts = given.Split(',').Select(p => float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? Math.Max(0f, v) : 0f)
                .ToArray();
            if (parts.Length == 4)
            {
                return parts;
            }
        }

        if (!OS.HasFeature("mobile"))
        {
            return new float[4];
        }

        Rect2I safe = DisplayServer.GetDisplaySafeArea();
        Vector2I at = window.Position;
        Vector2I size = window.Size;
        if (safe.Size.X <= 0 || safe.Size.Y <= 0)
        {
            return new float[4];
        }

        return new float[]
        {
            Math.Max(0, safe.Position.X - at.X), Math.Max(0, safe.Position.Y - at.Y),
            Math.Max(0, at.X + size.X - safe.End.X), Math.Max(0, at.Y + size.Y - safe.End.Y),
        };
    }

    /// <summary>
    /// On a phone, the interface drawn bigger by the screen's density: one of its pixels <see cref="TouchDef.UiUnit_mm"/>
    /// on the screen, but at least <see cref="TouchDef.MinUiHeight"/> of them in its height, and never smaller than on a
    /// computer. Elsewhere it's left as it is. Kept on the window from scene to scene; each scene asks again (the window
    /// may have turned or changed size).
    /// </summary>
    public static void ScaleInterface(Window window, TouchDef def)
    {
        if (!Phone)
        {
            return;
        }

        // Headless (CI) there's no screen, and the window's a small square: acting as a phone it gets a phone's screen,
        // 2400 × 1080 (a 6.5-inch one at --dpi=400) unless --screen=WIDTHxHEIGHT says otherwise.
        if (DisplayServer.GetName() == "headless")
        {
            window.Size = ScreenArg() ?? new Vector2I(2400, 1080);
        }

        window.ContentScaleFactor = 1f;
        float stretch = CanvasScale(window);
        float wanted = def.UiUnit_mm * Dpi / 25.4f / stretch;
        float most = window.Size.Y / stretch / def.MinUiHeight;
        window.ContentScaleFactor = Math.Max(1f, Math.Min(wanted, most));
    }

    /// <summary><c>--screen=WIDTHxHEIGHT</c>: the phone's screen in its own pixels, for headless runs.</summary>
    private static Vector2I? ScreenArg()
    {
        string[] parts = (Args.Value("--screen") ?? "").Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w >= 320 && h >= 240
            ? new Vector2I(w, h)
            : null;
    }

    /// <summary>A panel's padding either side (UiKit's theme): what's inside a panel is this much narrower.</summary>
    public const float PanelPadding = 68f;

    /// <summary>
    /// How wide a menu's panel (or what's in one) may be: <paramref name="wanted"/> on a computer; on a phone no wider than
    /// the screen less the menus' margins, and <paramref name="less"/> (a panel's padding, for what's inside one).
    /// </summary>
    public static float Room(float wanted, float less = 0f) =>
        Phone ? Math.Max(240f, Math.Min(wanted, Screen.X - 2f * PhoneMargin - less)) : wanted;

    /// <summary>The screen's size in the interface's pixels (the game's window, scaled).</summary>
    public static Vector2 Screen => Engine.GetMainLoop() is SceneTree tree ? tree.Root.GetVisibleRect().Size : new Vector2(1600f, 900f);
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ui;

/// <summary>
/// <c>-- --menu-touch-demo</c> on the main scene (Phase 6): works the menus by simulated taps and drags, as a phone sends
/// them, at a phone's size (<c>--touch</c>'s scaling; headless, <c>--screen=WxH</c> and <c>--dpi</c>). It checks each
/// screen fits across the screen, taps Play, picks a place, drags the page until Start comes into reach, goes back with
/// the phone's back button, opens the settings and their Video tab (finding the touch screen's settings, and none of the
/// desktop's), opens Play with others and taps the name box (which brings up a phone's keyboard), and ends on the
/// title, where the back button leaves the game. SMOKE PASS or SMOKE FAIL with what went wrong.
/// </summary>
public partial class MenuTouchDemo : Node
{
    private readonly List<(string Name, Action Do, Func<string?> Check, int Frames)> _steps = new();
    private readonly List<string> _failed = new();
    private Control _title = null!, _settings = null!, _others = null!;
    private Func<Control> _levelSelect = null!;
    private int _at;
    private int _wait;
    private bool _done;

    /// <param name="levels">The level select as it is now (it's built afresh each time it's shown).</param>
    public void Start(Control title, Func<Control> levels, Control settings, Control others)
    {
        _title = title;
        _levelSelect = levels;
        _settings = settings;
        _others = others;
        // As on a phone, the screen takes touches (headless and on a computer it says it has none), so pages scroll by drag.
        Input.EmulateTouchFromMouse = true;
        Script();
        GD.Print($"MENU TOUCH DEMO: {_steps.Count} steps on a screen of {TouchUi.Screen.X:0} × {TouchUi.Screen.Y:0} px");
    }

    public override void _Process(double delta)
    {
        if (_done || --_wait > 0)
        {
            return;
        }

        if (_at > 0)
        {
            (string name, _, Func<string?> check, _) = _steps[_at - 1];
            string? problem = check();
            GD.Print(problem is null ? $"MENU TOUCH DEMO {name}: ok" : $"MENU TOUCH DEMO {name}: FAILED, {problem}");
            if (problem is not null)
            {
                _failed.Add($"{name} ({problem})");
            }
        }

        if (_at >= _steps.Count)
        {
            _done = true;
            Finish();
            return;
        }

        _steps[_at].Do();
        _wait = _steps[_at].Frames; // for the tap to land and the screen to change
        _at++;
    }

    private void Finish()
    {
        Input.EmulateTouchFromMouse = false;
        if (_failed.Count > 0)
        {
            GD.Print($"SMOKE FAIL menu touch demo: {string.Join("; ", _failed)}");
            GetTree().QuitCleanly(1);
            return;
        }

        GD.Print($"SMOKE PASS menu touch demo: {_steps.Count} steps by touch");
        // The last step of all: the phone's back button on the title leaves the game.
        Back();
    }

    private void Script()
    {
        Step("the title fits", () => { }, () => _title.Visible ? OffScreen(_title, strict: true) ?? TooWide(_title) : "no title screen");
        Step("tap Play", () => TapButton(_title, "Play"), () => _levels.Visible ? null : "no level select");
        Step("the level select fits across", () => { }, () => TooWide(_levels));
        Step("tap a place", () => TapInView(Named(_levels, "Place_*_1")), () => Named(_levels, "Place_*_1") is Button { ButtonPressed: true } ? null : "the place isn't picked",
            frames: 6 * DragFrames + 20);
        Step("drag the page to Start", () => DragInto(Named(_levels, "Start_*")), () => Named(_levels, "Start_*") is { } start && OnScreen(start) ? null : "Start is out of reach",
            frames: 6 * DragFrames + 10);
        Step("back button to the title", Back, () => _title.Visible ? null : "not back on the title");
        Step("tap Settings", () => TapButton(_title, "Settings"), () => _settings.Visible ? TooWide(_settings) : "no settings");
        Step("the touch screen's settings", () => { },
            () => !HasLabel(_settings, "TOUCH SCREEN") ? "no touch settings" : HasLabel(_settings, "Mouse sensitivity") ? "the mouse's settings are there" : null);
        Step("tap the Video tab", () => TapTab(1), () => Tabs is { CurrentTab: 1 } ? HasLabel(_settings, "Window") ? "a window mode on a phone" : null : "still on the first tab");
        Step("tap Back", () => TapButton(_settings, "Back"), () => _title.Visible ? null : "not back on the title");
        Step("tap Play with others", () => TapButton(_title, "Play with others"), () => _others.Visible ? TooWide(_others) : "no Play with others");
        Step("tap your name", () => Tap(Named(_others, "Name")), () => Named(_others, "Name") is LineEdit { } name && name.HasFocus() ? null : "the name box didn't take the tap");
        Step("back button again", Back, () => _title.Visible ? null : "not back on the title");
    }

    private void Step(string name, Action act, Func<string?> check, int frames = 20) => _steps.Add((name, act, check, frames));

    /// <summary>One drag of the page: the thumb's moves, then a moment for it to settle.</summary>
    private const int DragMoves = 12, DragFrames = DragMoves + 8;

    private Control _levels => _levelSelect();

    private TabContainer? Tabs => _settings.FindChild("Tabs", recursive: true, owned: false) as TabContainer;

    /// <summary>Taps <paramref name="target"/>, first dragging its page until it's on the screen (as a thumb would).</summary>
    private async void TapInView(Control? target)
    {
        await DragIntoView(target);
        Tap(target);
    }

    private async void DragInto(Control? target) => await DragIntoView(target);

    /// <summary>
    /// Drags the page <paramref name="target"/> is on (a thumb from low to high, or high to low) until it's on the
    /// screen, a few times at most.
    /// </summary>
    private async System.Threading.Tasks.Task DragIntoView(Control? target)
    {
        for (int i = 0; i < 6 && target is not null && !OnScreen(target); i++)
        {
            if (ScrollOf(target) is not { } scroll)
            {
                return;
            }

            // At the page's left edge, on the page itself: a drag that starts on a button presses it instead.
            Rect2 area = scroll.GetGlobalRect();
            float x = area.Position.X + 6f;
            bool below = target.GetGlobalRect().End.Y > area.End.Y;
            Vector2 low = new(x, area.Position.Y + area.Size.Y * 0.8f), high = new(x, area.Position.Y + area.Size.Y * 0.25f);
            (Vector2 from, Vector2 to) = below ? (low, high) : (high, low);
            Touch(true, from);
            for (int k = 1; k <= DragMoves; k++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Drag(from.Lerp(to, (k - 1) / (float)DragMoves), from.Lerp(to, k / (float)DragMoves));
            }

            Touch(false, to);
            for (int k = DragMoves; k < DragFrames; k++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

        }
    }

    private void TapTab(int index)
    {
        if (Tabs?.GetTabBar() is { } bar)
        {
            Tap(bar.GetGlobalTransform() * bar.GetTabRect(index).GetCenter());
        }
    }

    private void TapButton(Control under, string text)
    {
        Button? button = under.FindChildren("*", nameof(Button), owned: false).OfType<Button>().FirstOrDefault(b => b.Text == text && b.IsVisibleInTree());
        Tap(button);
    }

    private void Tap(Control? control)
    {
        if (control is null)
        {
            GD.Print("MENU TOUCH DEMO: nothing to tap");
            return;
        }

        Tap(control.GetGlobalRect().GetCenter());
    }

    private void Tap(Vector2 at)
    {
        Touch(true, at);
        GetTree().CreateTimer(0.08).Timeout += () => Touch(false, at);
    }

    private void Touch(bool down, Vector2 at) =>
        Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = GetTree().Root.GetFinalTransform() * at, Pressed = down });

    private void Drag(Vector2 from, Vector2 to)
    {
        Transform2D t = GetTree().Root.GetFinalTransform();
        Input.ParseInputEvent(new InputEventScreenDrag { Index = 0, Position = t * to, Relative = t.BasisXform(to - from), ScreenRelative = t.BasisXform(to - from) });
    }

    private void Back() => GetTree().Root.PropagateNotification((int)NotificationWMGoBackRequest);

    private static Control? Named(Control under, string pattern) => under.FindChild(pattern, recursive: true, owned: false) as Control;

    private static bool HasLabel(Control under, string text) =>
        under.FindChildren("*", nameof(Label), owned: false).OfType<Label>().Any(l => l.IsVisibleInTree() && l.Text.Trim() == text);

    private static bool OnScreen(Control control)
    {
        Rect2 r = control.GetGlobalRect();
        Vector2 screen = TouchUi.Screen;
        return r.Position.X >= -1f && r.Position.Y >= -1f && r.End.X <= screen.X + 1f && r.End.Y <= screen.Y + 1f;
    }

    /// <summary>
    /// The screen's buttons off the screen: those not in a page that scrolls to them, or (<paramref name="strict"/>) any.
    /// </summary>
    private static string? OffScreen(Control screen, bool strict = false)
    {
        string[] off = screen.FindChildren("*", nameof(Button), owned: false).OfType<Button>()
            .Where(b => b.IsVisibleInTree() && !OnScreen(b) && (strict || !InScroll(b))).Select(b => b.Text).ToArray();
        return off.Length > 0 ? $"off the screen: {string.Join(", ", off)}" : null;
    }

    private static bool InScroll(Control control) => ScrollOf(control) is not null;

    private static ScrollContainer? ScrollOf(Control control)
    {
        for (Node? n = control.GetParent(); n is not null; n = n.GetParent())
        {
            if (n is ScrollContainer scroll)
            {
                return scroll;
            }
        }

        return null;
    }

    /// <summary>What's wider than the screen: a page wider than its scroller (it'd be cut off), or anything past the right edge.</summary>
    private static string? TooWide(Control screen)
    {
        var wide = new List<string>();
        foreach (ScrollContainer scroll in screen.FindChildren("*", nameof(ScrollContainer), owned: false).OfType<ScrollContainer>().Where(s => s.IsVisibleInTree()))
        {
            if (scroll.GetChildCount() > 0 && scroll.GetChild(0) is Control content && content.GetCombinedMinimumSize().X > scroll.Size.X + 1f &&
                scroll.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled)
            {
                wide.Add($"{scroll.Name} needs {content.GetCombinedMinimumSize().X:0} px of {scroll.Size.X:0}");
            }
        }

        // The pages scroll up and down only: anything past the right edge can't be reached.
        float right = TouchUi.Screen.X + 1f;
        foreach (Control c in screen.FindChildren("*", nameof(Control), owned: false).OfType<Control>().Where(c => c.IsVisibleInTree() && c is Button or LineEdit or Label))
        {
            if (c.GetGlobalRect().End.X > right)
            {
                wide.Add($"{(c is Button b ? b.Text : c.Name)} past the right edge ({c.GetGlobalRect().End.X:0} of {TouchUi.Screen.X:0})");
            }
        }

        return wide.Count > 0 ? string.Join(", ", wide.Take(6)) : null;
    }
}

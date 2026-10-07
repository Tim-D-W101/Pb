using System;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ui;

/// <summary>
/// Esc (or Start on a pad): pauses the game tree and offers Resume, Settings, Restart and Quit to the
/// menu. It keeps processing while the tree is paused.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private Control _root = null!;
    private Control _main = null!;
    private Control _settings = null!;
    private SettingsMenu _menu = null!;
    private Action? _restart;

    public bool Open => _root.Visible;

    /// <param name="graphicsChanged">Applies a change to the graphics (preset, its parts, render scale).</param>
    /// <param name="hudChanged">Applies a change to the HUD (crosshair, its size).</param>
    public void Build(GameSettings settings, PresentationDef view, Action<GameSettings> graphicsChanged, Action? restart, Action<GameSettings>? hudChanged = null)
    {
        Layer = 10;
        ProcessMode = ProcessModeEnum.Always;
        _restart = restart;

        VBoxContainer main = UiKit.Column(12);
        main.AddChild(UiKit.Title("Paused", 44));
        main.AddChild(UiKit.Button("Resume", Close));
        main.AddChild(UiKit.Button("Settings", () => ShowSettings(true)));
        if (restart is not null)
        {
            main.AddChild(UiKit.Button("Restart", () =>
            {
                Close();
                _restart?.Invoke();
            }));
        }

        main.AddChild(UiKit.Button("Quit to menu", () =>
        {
            GetTree().Paused = false;
            GetTree().ChangeSceneToFile(GameSession.MainScene);
        }));
        _main = main;

        VBoxContainer settingsColumn = UiKit.Column(14);
        _menu = new SettingsMenu { Name = "SettingsMenu" };
        _menu.Build(settings, view, graphicsChanged, hudChanged);
        settingsColumn.AddChild(_menu);
        settingsColumn.AddChild(UiKit.Button("Back", () => ShowSettings(false), 200));
        _settings = settingsColumn;

        VBoxContainer holder = UiKit.Column(0);
        holder.AddChild(_main);
        holder.AddChild(_settings);
        _root = UiKit.Overlay(UiKit.Panel(holder, 560f), dim: 0.5f);
        AddChild(_root);
        _root.Visible = false;
        ShowSettings(false);
    }

    public void Toggle()
    {
        if (Open)
        {
            Close();
        }
        else
        {
            _root.Visible = true;
            ShowSettings(false);
            GetTree().Paused = true;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _main.GetChild<Button>(1).GrabFocus();
        }
    }

    public void Close()
    {
        _root.Visible = false;
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Open && e.IsActionPressed("pause") && !_menu.Capturing)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void ShowSettings(bool show)
    {
        _main.Visible = !show;
        _settings.Visible = show;
    }
}

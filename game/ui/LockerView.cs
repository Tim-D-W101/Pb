using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Game.Player;
using Pb.Sim.Data;
using Pb.Sim.Gear;
using Pb.Sim.Match;

namespace Pb.Game.Ui;

/// <summary>
/// The gear locker: you in the works' changing room on a turntable under a lamp (<see cref="LockerStage"/>), with what
/// you can change round you. Pick your character, then a slot (the camera comes round to it) and its item by brand,
/// then its three colours from the palette or any colour you like. Drag to turn round yourself and scroll to come
/// closer. Done saves the loadout to your profile and your character to your settings; Back leaves both as they were.
/// It opens over the main menu or the lobby, and tells the one that opened it when it closes (<see cref="Closed"/>).
/// </summary>
public partial class LockerView : Control
{
    private readonly Dictionary<GearSlot, Button> _slotButtons = new();
    private readonly Dictionary<int, GearColours> _coloursGiven = new();
    private readonly List<Button> _chips = new();
    private PresentationDef _view = null!;
    private GameSettings _settings = null!;
    private RecordBook _records = null!;
    private GearCatalog _gear = null!;
    private Loadout _loadout = null!;
    private Color _side;
    private bool _showSide;
    private GearSlot? _slot;
    private int _colour;
    private SubViewport _viewport = null!;
    private LockerStage _stage = null!;
    private Action[] _steps = Array.Empty<Action>();
    private int _step = -1;
    private PanelContainer _left = null!;
    private PanelContainer _right = null!;
    private VBoxContainer _detail = null!;
    private HBoxContainer _looks = null!;
    private Label _status = null!;
    private Label _loading = null!;
    private ColorPickerButton? _picker;
    private bool _dragging;
    private bool _closed;

    /// <summary>Called as the locker closes: true when Done saved what you picked.</summary>
    public Action<bool>? Closed { get; set; }

    /// <summary>What's happening outside (the lobby's countdown), shown at the top; null shows nothing.</summary>
    public Func<string?>? Status { get; set; }

    /// <summary>Whether you're dressed and the room is built (the smoke test waits for it).</summary>
    public bool Built => _step >= _steps.Length && _stage.Visual is not null;

    /// <summary>Where the camera is (the tour's log).</summary>
    public string CameraLine => _step >= _steps.Length ? _stage.Camera.Describe() : "";

    /// <summary>What you've picked so far (the smoke test checks it's what was saved).</summary>
    public Loadout Picked => _loadout;

    /// <summary>
    /// Opens the locker on the loadout in <paramref name="records"/> (the field's own kit before you've been in) on your
    /// character from <paramref name="settings"/>. <paramref name="paint"/> is your paint colour; <paramref name="side"/>
    /// the colour of the side whose colour you can see your jersey and pants in.
    /// </summary>
    public void Open(GameData data, PresentationDef view, GameSettings settings, RecordBook records, Color paint, Color side)
    {
        _view = view;
        _settings = settings;
        _records = records;
        _gear = data.Gear;
        _side = side;
        int characters = Math.Max(1, view.Characters.Models.Length);
        int character = ((settings.PlayerLook % characters) + characters) % characters;
        _loadout = _gear.Read(records.Data.Loadout, character);
        _loadout.Character = character;
        Name = "Locker";
        Theme = UiKit.Theme;
        // Already in the tree: the offsets too, or it keeps the size it had (none).
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        // The room, behind everything, in a view with a world of its own.
        var container = new SubViewportContainer { Name = "View", Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        container.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(container);
        _viewport = new SubViewport { Name = "Room", OwnWorld3D = true, HandleInputLocally = false, GuiDisableInput = true };
        container.AddChild(_viewport);
        _stage = new LockerStage { Name = "Stage" };
        _viewport.AddChild(_stage);
        GraphicsPresetDef preset = view.Graphics.Effective(Args.Value("--preset") ?? settings.GraphicsPreset, settings.Graphics);
        ViewQuality(preset);
        _steps = _stage.Build(data, view, preset, _viewport, paint);

        // Dragging anywhere the panels aren't turns round you.
        var drag = new Control { Name = "Drag", MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Drag };
        drag.SetAnchorsPreset(LayoutPreset.FullRect);
        drag.GuiInput += OnDrag;
        AddChild(drag);

        var margin = new MarginContainer { Name = "Margin", MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string edge in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride("margin_" + edge, 36);
        }

        AddChild(margin);
        VBoxContainer page = UiKit.Column(14);
        page.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddChild(page);

        // The heading, and what's happening outside (the lobby).
        HBoxContainer header = UiKit.Row(16);
        header.MouseFilter = MouseFilterEnum.Ignore;
        VBoxContainer heading = UiKit.Column(2);
        heading.MouseFilter = MouseFilterEnum.Ignore;
        heading.AddChild(UiKit.Body("GEAR LOCKER", 18, UiKit.Accent));
        heading.AddChild(UiKit.Title("Your kit", 40));
        header.AddChild(heading);
        _status = UiKit.Body("", 20, UiKit.Accent);
        _status.Name = "Status";
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        _status.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        header.AddChild(_status);
        page.AddChild(header);

        HBoxContainer body = UiKit.Row(0);
        body.MouseFilter = MouseFilterEnum.Ignore;
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        page.AddChild(body);
        _left = UiKit.Panel(LeftColumn(), 360f);
        _left.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        body.AddChild(_left);
        var middle = new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(middle);
        _loading = UiKit.Body("Setting out your kit…", 22, UiKit.Dim);
        _loading.SetAnchorsPreset(LayoutPreset.Center);
        _loading.GrowHorizontal = GrowDirection.Both;
        _loading.GrowVertical = GrowDirection.Both;
        middle.AddChild(_loading);
        _detail = UiKit.Column(8);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(500f, 0f) };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _detail.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_detail);
        _right = UiKit.Panel(scroll, 560f);
        _right.SizeFlagsVertical = SizeFlags.ExpandFill;
        body.AddChild(_right);

        // How to look round, and the way out.
        HBoxContainer footer = UiKit.Row(16);
        footer.MouseFilter = MouseFilterEnum.Ignore;
        Label hint = UiKit.Body("Drag to turn round yourself · scroll to come closer", 18, UiKit.Dim);
        hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hint.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        footer.AddChild(hint);
        Button back = UiKit.Button("Back", () => Close(false), 200);
        back.Name = "Back";
        footer.AddChild(back);
        Button done = UiKit.Button("Done", () => Close(true), 240);
        done.Name = "Done";
        footer.AddChild(done);
        page.AddChild(footer);

        ShowSlot(null);
        (FindChild("Slot_All", recursive: true, owned: false) as Control)?.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Picks a slot (null: the whole kit), as its button would (the menu tour and the smoke test).</summary>
    public void Pick(GearSlot? slot)
    {
        if (slot is { } s && _slotButtons.TryGetValue(s, out Button? button))
        {
            button.ButtonPressed = true;
        }
        else if (FindChild("Slot_All", recursive: true, owned: false) is Button whole)
        {
            whole.ButtonPressed = true;
        }
    }

    /// <summary>Puts <paramref name="item"/> in its slot, as its button would.</summary>
    public void Wear(int item)
    {
        GearItem picked = _gear.Items[item];
        _loadout[picked.Slot] = new GearChoice(item, _coloursGiven.TryGetValue(item, out GearColours given) ? given : picked.Colours);
        Redress();
    }

    /// <summary>Sets colour <paramref name="index"/> (0 main, 1 second, 2 accent) of the slot shown, as the palette would.</summary>
    public void SetColour(int index, Color colour)
    {
        if (_slot is not { } slot)
        {
            return;
        }

        GearChoice choice = _loadout[slot];
        GearColours colours = choice.Colours.With(index, Kit.ToUint(colour));
        _loadout[slot] = choice with { Colours = colours };
        _coloursGiven[choice.Item] = colours;
        _stage.Recolour(Dressed());
        ShowChips();
    }

    /// <summary>Done (saving) or Back.</summary>
    public void Close(bool save)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        if (save)
        {
            _records.Data.Loadout = _gear.Write(_loadout);
            Profile.Save(_records);
            _settings.PlayerLook = _loadout.Character;
            _settings.Save();
            GD.Print($"Locker: saved character {_loadout.Character + 1} in " +
                     string.Join(", ", Enum.GetValues<GearSlot>().Select(s => $"{_gear.Items[_loadout[s].Item].Id} {GearColours.Format(_loadout[s].Colours.Main)}")));
        }

        Closed?.Invoke(save);
        QueueFree();
    }

    public override void _Process(double delta)
    {
        // The room a piece a frame once the panels have shown, then you in it.
        if (_step < _steps.Length)
        {
            int step = _step++;
            if (step >= 0)
            {
                _steps[step]();
                if (_step == _steps.Length)
                {
                    _stage.Dress(Dressed());
                    _loading.Visible = false;
                    ShowSlot(_slot);
                }
            }

            return;
        }

        _status.Text = Status?.Invoke() ?? "";

        // You in the middle of the room the panels leave.
        float width = GetViewportRect().Size.X;
        float from = _left.GetGlobalRect().End.X, to = _right.GetGlobalRect().Position.X;
        _stage.Camera.Shift = width > 0f ? ((from + to) * 0.5f - width * 0.5f) / (width * 0.5f) : 0f;

        // A pad's right stick turns round you too.
        Vector2 stick = new(Input.GetJoyAxis(0, JoyAxis.RightX), Input.GetJoyAxis(0, JoyAxis.RightY));
        if (stick.Length() > 0.2f)
        {
            _stage.Camera.Drag(stick * ((float)delta * _view.Locker.PadTurn_degPerS / _view.Locker.DragDegPerPx));
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Pb.Game.Audio.UiSounds.Back();
            Close(false);
        }
    }

    /// <summary>The room drawn at your graphics preset's antialiasing and your render scale.</summary>
    private void ViewQuality(GraphicsPresetDef preset)
    {
        _viewport.Msaa3D = preset.Msaa switch
        {
            1 => Viewport.Msaa.Msaa2X,
            2 => Viewport.Msaa.Msaa4X,
            _ => Viewport.Msaa.Disabled,
        };
        Pb.Game.World.Atmosphere.ApplyRenderScale(_viewport, _settings.RenderScale, _view.Graphics);
    }

    private void OnDrag(InputEvent e)
    {
        if (_step < _steps.Length)
        {
            return;
        }

        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _stage.Camera.Zoom(-1f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _stage.Camera.Zoom(1f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } button:
                _dragging = button.Pressed;
                break;
            case InputEventMouseMotion motion when _dragging:
                _stage.Camera.Drag(motion.Relative);
                break;
        }
    }

    /// <summary>What you've picked, as drawn: in a side's colour while you're looking at that.</summary>
    private Kit Dressed() => new(_gear, _loadout, _showSide ? _side : null);

    /// <summary>Your character, and the slots you can change (the whole kit first).</summary>
    private VBoxContainer LeftColumn()
    {
        VBoxContainer column = UiKit.Column(10);
        column.AddChild(UiKit.Body("CHARACTER", 16, UiKit.Dim));
        _looks = UiKit.Row(8);
        column.AddChild(_looks);
        ShowLooks();
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 6f) });
        column.AddChild(UiKit.Body("WHAT YOU WEAR", 16, UiKit.Dim));
        var group = new ButtonGroup();
        Button whole = SlotButton("Whole kit", group, null);
        whole.Name = "Slot_All";
        whole.SetPressedNoSignal(true);
        column.AddChild(whole);
        foreach (GearSlot slot in Enum.GetValues<GearSlot>())
        {
            Button button = SlotButton("", group, slot);
            button.Name = $"Slot_{slot}";
            _slotButtons[slot] = button;
            column.AddChild(button);
        }

        ShowSlotNames();
        return column;
    }

    private Button SlotButton(string text, ButtonGroup group, GearSlot? slot)
    {
        var button = new Button
        {
            Text = text, ToggleMode = true, ButtonGroup = group, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(292f, slot is null ? 46f : 62f),
        };
        button.AddThemeFontSizeOverride("font_size", 19);
        button.Toggled += on =>
        {
            if (on)
            {
                Pb.Game.Audio.UiSounds.Click();
                ShowSlot(slot);
            }
        };
        button.MouseEntered += Pb.Game.Audio.UiSounds.Hover;
        return button;
    }

    private void ShowLooks()
    {
        Clear(_looks);
        int count = Math.Max(1, _view.Characters.Models.Length);
        var group = new ButtonGroup();
        for (int i = 0; i < count; i++)
        {
            int index = i;
            var button = new Button
            {
                Name = $"Look_{i}", Text = $"{i + 1}", ToggleMode = true, ButtonGroup = group, ButtonPressed = i == _loadout.Character,
                CustomMinimumSize = new Vector2(64f, 44f), FocusMode = FocusModeEnum.All,
            };
            button.Toggled += on =>
            {
                if (on && _loadout.Character != index)
                {
                    Pb.Game.Audio.UiSounds.Click();
                    _loadout.Character = index;
                    Redress();
                }
            };
            button.MouseEntered += Pb.Game.Audio.UiSounds.Hover;
            _looks.AddChild(button);
        }
    }

    /// <summary>Each slot's button with what's in it now.</summary>
    private void ShowSlotNames()
    {
        foreach ((GearSlot slot, Button button) in _slotButtons)
        {
            button.Text = $"{SlotName(slot)}\n{What(slot)}";
        }
    }

    /// <summary>What's worn in <paramref name="slot"/>, in words.</summary>
    private string What(GearSlot slot)
    {
        if (Included(slot))
        {
            return $"Built into the {_gear.Items[_loadout[GearSlot.Marker].Item].DisplayName}";
        }

        GearItem item = _gear.Items[_loadout[slot].Item];
        return $"{item.Brand.DisplayName} {item.DisplayName}";
    }

    private static string SlotName(GearSlot slot) => slot switch
    {
        GearSlot.Marker => "Marker",
        GearSlot.Loader => "Loader",
        GearSlot.Tank => "Tank",
        GearSlot.Mask => "Mask",
        GearSlot.Jersey => "Jersey",
        _ => "Pants",
    };

    /// <summary>Whether <paramref name="slot"/> comes with the marker as drawn (the generated one has its loader and tank on it).</summary>
    private bool Included(GearSlot slot) => _stage.Visual?.WearsMarkerModel == true && _gear.Included(_loadout, slot);

    /// <summary>The right-hand panel for <paramref name="slot"/>: its items by brand and their colours; or the brands, for the whole kit.</summary>
    private void ShowSlot(GearSlot? slot)
    {
        _slot = slot;
        _picker = null;
        _chips.Clear();
        Clear(_detail);
        if (_step >= _steps.Length)
        {
            LockerFramingDef framing = _view.Locker.Framing(slot);
            _stage.Camera.Frame(framing, _stage.Target(framing));
        }

        if (slot is not { } s)
        {
            ShowBrands();
            return;
        }

        _detail.AddChild(UiKit.Title(SlotName(s), 32));
        bool included = Included(s);
        if (included)
        {
            Label why = UiKit.Body($"Your {_gear.Items[_loadout[GearSlot.Marker].Item].DisplayName} marker comes with its own: pick another " +
                                   "marker to choose this.", 17, UiKit.Dim, wrap: true);
            _detail.AddChild(why);
        }

        var group = new ButtonGroup();
        foreach (int index in _gear.ItemsIn(s))
        {
            GearItem item = _gear.Items[index];
            int pick = index;
            var button = new Button
            {
                Name = $"Item_{item.Id}", Text = $"{item.Brand.DisplayName} {item.DisplayName}", ToggleMode = true, ButtonGroup = group,
                ButtonPressed = _loadout[s].Item == index, Disabled = included, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.All,
                CustomMinimumSize = new Vector2(440f, 40f),
            };
            button.Toggled += on =>
            {
                if (on && _loadout[s].Item != pick)
                {
                    Pb.Game.Audio.UiSounds.Click();
                    Wear(pick);
                }
            };
            button.MouseEntered += Pb.Game.Audio.UiSounds.Hover;
            _detail.AddChild(button);
        }

        GearItem worn = _gear.Items[_loadout[s].Item];
        Label line = UiKit.Body($"{worn.Brand.DisplayName}: {worn.Brand.Line}", 16, UiKit.Dim, wrap: true);
        line.CustomMinimumSize = new Vector2(440f, 0f);
        _detail.AddChild(line);
        ShowColours(s);
    }

    /// <summary>The four brands, with a button to wear each one's range, and the field's own kit.</summary>
    private void ShowBrands()
    {
        _detail.AddChild(UiKit.Title("Four brands", 32));
        Label lead = UiKit.Body("Pick a slot on the left to choose its item and colours, or start from a brand's whole range.", 17, UiKit.Dim, wrap: true);
        lead.CustomMinimumSize = new Vector2(440f, 0f);
        _detail.AddChild(lead);
        foreach (GearBrand brand in _gear.Brands)
        {
            string id = brand.Id;
            HBoxContainer row = UiKit.Row(12);
            Label name = UiKit.Body(brand.DisplayName, 22, UiKit.Accent);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(name);
            Button wear = UiKit.Button("Wear the range", () =>
            {
                if (Kit.Brand(_gear, id, _loadout.Character) is { } range)
                {
                    _loadout = range;
                    Redress();
                }
            }, 220);
            wear.Name = $"Range_{id}";
            row.AddChild(wear);
            _detail.AddChild(row);
            Label line = UiKit.Body(brand.Line, 16, UiKit.Dim, wrap: true);
            line.CustomMinimumSize = new Vector2(440f, 0f);
            _detail.AddChild(line);
        }

        _detail.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 6f) });
        Button field = UiKit.Button("Wear the field's own kit", () =>
        {
            _loadout = _gear.Default(_loadout.Character);
            Redress();
        }, 300);
        field.Name = "FieldKit";
        field.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _detail.AddChild(field);
    }

    /// <summary>
    /// The slot's three colours (the one the palette sets lit), the palette, any colour, and the item's own colours; and
    /// for the clothes, how they look in a side's colour.
    /// </summary>
    private void ShowColours(GearSlot slot)
    {
        GearItem item = _gear.Items[_loadout[slot].Item];
        bool[] used = Used(slot, item);
        _detail.AddChild(UiKit.Body("COLOURS", 16, UiKit.Dim));
        if (Why(slot, item) is { } why)
        {
            Label note = UiKit.Body(why, 16, UiKit.Dim, wrap: true);
            note.CustomMinimumSize = new Vector2(440f, 0f);
            _detail.AddChild(note);
        }

        if (!used[_colour])
        {
            _colour = Array.IndexOf(used, true) is >= 0 and var first ? first : 0;
        }

        HBoxContainer chips = UiKit.Row(8);
        var group = new ButtonGroup();
        string[] names = { "Main", "Second", "Accent" };
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            var chip = new Button
            {
                Name = $"Colour_{i}", Text = names[i], ToggleMode = true, ButtonGroup = group, ButtonPressed = i == _colour && used[i],
                Disabled = !used[i], CustomMinimumSize = new Vector2(140f, 46f), FocusMode = FocusModeEnum.All,
            };
            chip.Toggled += on =>
            {
                if (on)
                {
                    Pb.Game.Audio.UiSounds.Click();
                    _colour = index;
                    ShowChips();
                }
            };
            _chips.Add(chip);
            chips.AddChild(chip);
        }

        _detail.AddChild(chips);
        bool any = used.Any(u => u);
        var palette = new GridContainer { Name = "Palette", Columns = (_view.Locker.Palette.Length + 1) / 2 };
        palette.AddThemeConstantOverride("h_separation", 5);
        palette.AddThemeConstantOverride("v_separation", 5);
        for (int i = 0; i < _view.Locker.Palette.Length; i++)
        {
            Color colour = Color.FromHtml(_view.Locker.Palette[i]);
            var swatch = new Button { Name = $"Swatch_{i}", CustomMinimumSize = new Vector2(31f, 31f), Disabled = !any, FocusMode = FocusModeEnum.All };
            Paint(swatch, colour, 1);
            swatch.Pressed += () =>
            {
                Pb.Game.Audio.UiSounds.Click();
                SetColour(_colour, colour);
            };
            palette.AddChild(swatch);
        }

        _detail.AddChild(palette);
        HBoxContainer row = UiKit.Row(10);
        _picker = new ColorPickerButton
        {
            Name = "AnyColour", Text = "Any colour…", EditAlpha = false, Disabled = !any, CustomMinimumSize = new Vector2(220f, 46f),
            FocusMode = FocusModeEnum.All,
        };
        ColorPicker picker = _picker.GetPicker();
        picker.EditAlpha = false;
        picker.SamplerVisible = false;
        picker.CanAddSwatches = false;
        Color[] presets = picker.GetPresets();
        foreach (string hex in _view.Locker.Palette)
        {
            // Every picker shares one list of presets, so each colour goes in once.
            Color colour = Color.FromHtml(hex);
            if (!presets.Contains(colour))
            {
                picker.AddPreset(colour);
            }
        }

        _picker.ColorChanged += colour => SetColour(_colour, colour);
        row.AddChild(_picker);
        Button own = UiKit.Button("Its own colours", () =>
        {
            int index = _loadout[slot].Item;
            _loadout[slot] = new GearChoice(index, _gear.Items[index].Colours);
            _coloursGiven.Remove(index);
            _stage.Recolour(Dressed());
            ShowChips();
        }, 220, any);
        own.Name = "OwnColours";
        row.AddChild(own);
        _detail.AddChild(row);

        if (slot is GearSlot.Jersey or GearSlot.Pants)
        {
            HBoxContainer toggle = UiKit.CheckRow("In a side's colour", _showSide, on =>
            {
                _showSide = on;
                _stage.Recolour(Dressed());
            });
            toggle.Name = "SideColour";
            toggle.TooltipText = "In a round with sides your jersey and pants take your side's colour as their main colour; the rest of " +
                                 "your design stays yours.";
            _detail.AddChild(toggle);
            Label sides = UiKit.Body("With sides, the main colour is your side's.", 16, UiKit.Dim);
            _detail.AddChild(sides);
        }

        ShowChips();
    }

    /// <summary>Each colour chip in its colour (the one the palette sets lit), and the picker on it.</summary>
    private void ShowChips()
    {
        if (_slot is not { } slot)
        {
            return;
        }

        GearColours colours = _loadout[slot].Colours;
        for (int i = 0; i < _chips.Count; i++)
        {
            Paint(_chips[i], GearModels.ToColor(colours[i]), i == _colour ? 3 : 1);
        }

        if (_picker is not null)
        {
            _picker.Color = GearModels.ToColor(colours[_colour]);
        }
    }

    /// <summary>Which of the item's three colours show on it as worn.</summary>
    private bool[] Used(GearSlot slot, GearItem item)
    {
        bool ownFinish = slot == GearSlot.Marker && _stage.Visual?.WearsMarkerModel == true;
        if (Included(slot) || ownFinish || slot == GearSlot.Mask && MaskRecipes.IsOwn(item.Shape))
        {
            return new[] { false, false, false };
        }

        return slot is GearSlot.Jersey or GearSlot.Pants && ClothesPatterns.Index(item.Shape) == 0 ? new[] { true, false, false } : new[] { true, true, true };
    }

    /// <summary>Why some colours can't be changed, or null.</summary>
    private string? Why(GearSlot slot, GearItem item)
    {
        if (Included(slot))
        {
            return "It comes in the marker's finish.";
        }

        if (slot == GearSlot.Marker && _stage.Visual?.WearsMarkerModel == true)
        {
            return "It comes in its own finish, its loader and tank with it.";
        }

        if (slot == GearSlot.Mask && MaskRecipes.IsOwn(item.Shape))
        {
            return "The mask your character came with: a brand's mask takes your colours.";
        }

        return slot is GearSlot.Jersey or GearSlot.Pants && ClothesPatterns.Index(item.Shape) == 0
            ? "The clothes your character came with, dyed the main colour (white leaves them as they are)."
            : null;
    }

    /// <summary>You built again in what you've picked, and the panels brought up to date (what's worn decides what can change).</summary>
    private void Redress()
    {
        // The panel is built again: keyboard and pad focus stays on the button of the same name.
        string? focused = GetViewport().GuiGetFocusOwner() is { } owner && _detail.IsAncestorOf(owner) ? owner.Name : null;
        _stage.Dress(Dressed());
        ShowSlotNames();
        ShowSlot(_slot);
        if (focused is not null && _detail.FindChild(focused, recursive: true, owned: false) is Control again)
        {
            again.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    /// <summary>A button filled with <paramref name="colour"/>, its edge <paramref name="edge"/> px (lit when chosen).</summary>
    private static void Paint(Button button, Color colour, int edge)
    {
        Color border = edge > 1 ? UiKit.Accent : new Color(1f, 1f, 1f, 0.25f);
        StyleBoxFlat Box(Color background, Color line, int width) => new()
        {
            BgColor = background, BorderColor = line, BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        button.AddThemeStyleboxOverride("normal", Box(colour, border, edge));
        button.AddThemeStyleboxOverride("hover", Box(colour.Lightened(0.08f), UiKit.Accent, Math.Max(2, edge)));
        button.AddThemeStyleboxOverride("pressed", Box(colour, UiKit.Accent, 3));
        button.AddThemeStyleboxOverride("hover_pressed", Box(colour.Lightened(0.08f), UiKit.Accent, 3));
        button.AddThemeStyleboxOverride("focus", Box(new Color(0f, 0f, 0f, 0f), UiKit.Accent, 2));
        button.AddThemeStyleboxOverride("disabled", Box(colour.Darkened(0.55f), new Color(0.2f, 0.2f, 0.2f), 1));
        // Words on the chip in black or white, whichever reads.
        Color ink = colour.Luminance > 0.55f ? new Color(0.05f, 0.05f, 0.06f) : Colors.White;
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
        {
            button.AddThemeColorOverride(state, ink);
        }
    }

    private static void Clear(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}

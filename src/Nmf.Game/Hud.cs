using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Units;
using Side = Nmf.Sim.Units.Side;
using Nmf.Sim.Vision;

namespace Nmf.Game;

/// <summary>Top status bar, JA2-style portrait cards along the bottom and an F1 help panel.</summary>
public partial class Hud : CanvasLayer
{
    private const string HelpText =
        "Left click / drag      select soldier / box select (Shift adds)\n" +
        "Right click            walk there   (Shift: run, Alt/Option: crawl)\n" +
        "1 / 2 / 3              stand / crouch / go prone\n" +
        "H                      halt\n" +
        "Space                  pause (orders still work)\n" +
        "+ / -                  game speed\n" +
        "WASD, arrows, middle drag, two-finger pan   move camera\n" +
        "Wheel, pinch           zoom\n" +
        "Tab / Esc              select all / clear selection\n" +
        "Cards                  click selects, double click centres camera\n" +
        "Right click on enemy   fire at that enemy\n" +
        "P                      fire policy: fire at will / return fire / hold fire\n" +
        "F11                    fullscreen\n" +
        "F                      debug: reveal all units\n" +
        "F1                     close this help";

    private static readonly Color PanelColor = new(0.11f, 0.12f, 0.09f, 0.9f);
    private static readonly Color BorderColor = new(0.45f, 0.43f, 0.3f);
    private static readonly Color SelectedBorder = new(1f, 0.85f, 0.3f);

    private readonly List<Card> _cards = [];

    private sealed record Card(UnitId Id, PanelContainer Panel, Label Status, Label Condition, Label Policy, ProgressBar Morale, ProgressBar Suppression, StyleBoxFlat Style);
    private Label _status = null!;

    /// <summary>Approximate height of the card bar in base pixels; the camera may scroll this far past the map's south edge.</summary>
    public const float BottomBarHeight = 190f;
    private PanelContainer _help = null!;

    public GameSession Session { get; set; } = null!;
    public ArtLibrary Art { get; set; } = null!;
    public Action<UnitId, bool>? CardClicked { get; set; }
    public Action<UnitId>? CardDoubleClicked { get; set; }

    public override void _Ready()
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = BuildTheme() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var top = new PanelContainer();
        top.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        _status = new Label();
        top.AddChild(_status);
        root.AddChild(top);

        // Only as wide as its cards, so the rest of the south edge of the map stays visible.
        var bottom = new PanelContainer { GrowVertical = Control.GrowDirection.Begin, GrowHorizontal = Control.GrowDirection.End };
        bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 8);
        bottom.AddChild(cards);
        root.AddChild(bottom);

        int index = 0;
        foreach (var unit in Session.OwnUnits)
            cards.AddChild(BuildCard(unit, index++));

        _help = new PanelContainer { Visible = false };
        _help.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _help.GrowHorizontal = Control.GrowDirection.Both;
        _help.GrowVertical = Control.GrowDirection.Both;
        var helpLabel = new Label { Text = HelpText };
        helpLabel.AddThemeFontOverride("font", MonospaceFont());
        _help.AddChild(helpLabel);
        root.AddChild(_help);

        Refresh();
    }

    public void ToggleHelp() => _help.Visible = !_help.Visible;

    public void Refresh()
    {
        var contacts = Session.Knowledge.Contacts.ToList();
        int seen = contacts.Count(c => c.Level == ContactLevel.Visible && Session.Sim.FindUnit(c.Target) is { IsOutOfAction: false });
        int heard = contacts.Count(c => c.Level == ContactLevel.Suspected);
        int lastKnown = contacts.Count(c => c.Level == ContactLevel.LastKnown);
        var t = Session.GameTime;
        string paused = Session.Clock.Paused ? "   ▌▌ PAUSED" : "";
        _status.Text = string.Create(CultureInfo.InvariantCulture,
            $"  {(int)t.TotalMinutes:00}:{t.Seconds:00}   speed x{Session.Clock.TimeScale:0.##}{paused}      Enemy: {seen} seen · {heard} heard · {lastKnown} last known      F1 help");

        int dead = Session.OwnUnits.Count(u => u.Wound == WoundLevel.Dead);
        int wounded = Session.OwnUnits.Count(u => u.Wound is > WoundLevel.None and < WoundLevel.Dead);
        int enemyDown = Session.Sim.Units.Count(u => u.Side != Session.PlayerSide && u.IsOutOfAction && Session.Knowledge.LevelOf(u.Id) == ContactLevel.Visible);
        _status.Text += string.Create(CultureInfo.InvariantCulture, $"      Losses: {dead} KIA · {wounded} wounded   Enemy down (seen): {enemyDown}");

        foreach (var card in _cards)
        {
            if (Session.Sim.FindUnit(card.Id) is not { } unit)
                continue;
            card.Status.Text = UnitStatus.Describe(unit);
            card.Condition.Text = UnitStatus.Condition(unit);
            card.Policy.Text = UnitStatus.PolicyName(unit.FirePolicy);
            card.Morale.Value = unit.IsOutOfAction ? 0 : unit.Morale;
            card.Suppression.Value = unit.Suppression;
            card.Style.BorderColor = Session.Selection.Contains(card.Id) ? SelectedBorder : BorderColor;
            card.Panel.Modulate = unit.Wound == WoundLevel.Dead ? new Color(0.55f, 0.55f, 0.55f) : Colors.White;
        }
    }

    private PanelContainer BuildCard(Unit unit, int index)
    {
        var style = PanelStyle();
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer();
        panel.AddChild(column);

        column.AddChild(new TextureRect
        {
            Texture = new AtlasTexture { Atlas = Art.Portraits(unit.Side), Region = new Rect2(UnitNames.PortraitIndex(index) * 64, 0, 64, 64) },
            CustomMinimumSize = new Vector2(96, 96),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var name = new Label { Text = UnitNames.For(unit.Side, index), MouseFilter = Control.MouseFilterEnum.Ignore };
        name.AddThemeFontSizeOverride("font_size", 15);
        column.AddChild(name);
        var status = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        status.AddThemeFontSizeOverride("font_size", 14);
        status.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.65f));
        column.AddChild(status);
        var condition = SmallLabel(column, new Color(0.9f, 0.75f, 0.7f));
        var policy = SmallLabel(column, new Color(0.7f, 0.8f, 0.9f));
        var morale = Bar(column, new Color(0.35f, 0.7f, 0.3f));
        var suppression = Bar(column, new Color(0.95f, 0.55f, 0.15f));

        var id = unit.Id;
        panel.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
            {
                if (click.DoubleClick)
                    CardDoubleClicked?.Invoke(id);
                else
                    CardClicked?.Invoke(id, click.ShiftPressed);
                panel.AcceptEvent();
            }
        };
        _cards.Add(new Card(id, panel, status, condition, policy, morale, suppression, style));
        return panel;
    }

    private static Label SmallLabel(Container parent, Color colour)
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", colour);
        parent.AddChild(label);
        return label;
    }

    private static ProgressBar Bar(Container parent, Color fill)
    {
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1000,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(96, 6),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.2f, 0.2f, 0.18f) });
        parent.AddChild(bar);
        return bar;
    }

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = 18 };
        theme.SetStylebox("panel", "PanelContainer", PanelStyle());
        theme.SetColor("font_color", "Label", new Color(0.93f, 0.92f, 0.84f));
        theme.SetColor("font_outline_color", "Label", Colors.Black);
        theme.SetConstant("outline_size", "Label", 3);
        return theme;
    }

    private static StyleBoxFlat PanelStyle()
    {
        var style = new StyleBoxFlat { BgColor = PanelColor, BorderColor = BorderColor };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(4);
        style.SetContentMarginAll(8);
        return style;
    }

    private static Font MonospaceFont() => new SystemFont { FontNames = ["Menlo", "Consolas", "DejaVu Sans Mono", "monospace"] };
}

using System;
using Godot;
using Nmf.Client;

namespace Nmf.Game;

/// <summary>
/// The settings window (spec 2026-10-10-settings-hints-design §3): volumes, mute, sounds as marks, language, full screen,
/// UI scale, card size, follow. Every change is applied at once (<see cref="Changed"/>); closing saves (<see cref="Closed"/>).
/// </summary>
public partial class SettingsView : CanvasLayer
{
    public GameSettings Settings { get; set; } = new();
    public string Language { get; set; } = "en";
    public Action<GameSettings>? Changed { get; set; }
    public Action<GameSettings>? Closed { get; set; }

    public override void _Ready()
    {
        Layer = 20;
        bool fi = Language == "fi";
        var root = new Control { Theme = Hud.BuildTheme(), MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.45f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(shade);

        var paper = new PanelContainer();
        paper.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.93f, 0.9f, 0.8f), BorderColor = new Color(0.35f, 0.3f, 0.2f),
            BorderWidthLeft = 3, BorderWidthTop = 3, BorderWidthRight = 3, BorderWidthBottom = 3,
            ContentMarginLeft = 32, ContentMarginRight = 32, ContentMarginTop = 24, ContentMarginBottom = 24,
        });
        paper.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        paper.GrowHorizontal = Control.GrowDirection.Both;
        paper.GrowVertical = Control.GrowDirection.Both;
        paper.CustomMinimumSize = new Vector2(560, 0);
        root.AddChild(paper);

        // Title and Done stay put; everything between scrolls when the window is smaller than the list.
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        paper.AddChild(outer);
        Ink(outer, fi ? "Asetukset" : "Settings", 30);
        outer.AddChild(new HSeparator());
        var body = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(600, Mathf.Max(240, GetViewport().GetVisibleRect().Size.Y - 220)),
        };
        outer.AddChild(body);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        body.AddChild(column);

        Ink(column, fi ? "Äänet" : "Sound", 20);
        Slider(column, fi ? "Päävoimakkuus" : "Master", Settings.Master, v => Settings = Settings with { Master = v });
        Slider(column, fi ? "Tehosteet" : "Effects", Settings.Sfx, v => Settings = Settings with { Sfx = v });
        Slider(column, fi ? "Puhe" : "Voices", Settings.Voice, v => Settings = Settings with { Voice = v });
        Slider(column, fi ? "Ympäristö" : "Ambience", Settings.Ambience, v => Settings = Settings with { Ambience = v });
        Slider(column, fi ? "Musiikki" : "Music", Settings.Music, v => Settings = Settings with { Music = v });
        Check(column, fi ? "Mykistä kaikki" : "Mute all", Settings.Mute, v => Settings = Settings with { Mute = v });
        Check(column, fi ? "Näytä äänet kuvina (V)" : "Show sounds as marks (V)", Settings.SoundMarks, v => Settings = Settings with { SoundMarks = v });
        column.AddChild(new HSeparator());

        Ink(column, fi ? "Näyttö ja peli" : "Display and game", 20);
        Choice(column, fi ? "Kieli" : "Language", ["Suomi", "English"], Settings.Language is null ? (fi ? 0 : 1) : Settings.Language == "fi" ? 0 : 1,
            i => Settings = Settings with { Language = i == 0 ? "fi" : "en" });
        Check(column, fi ? "Koko näyttö" : "Full screen", Settings.Fullscreen, v => Settings = Settings with { Fullscreen = v });
        Choice(column, fi ? "Käyttöliittymän koko" : "Interface size", ["100 %", "125 %", "150 %"], Array.IndexOf(GameSettings.UiScales, Settings.UiScalePct),
            i => Settings = Settings with { UiScalePct = GameSettings.UiScales[i] });
        Choice(column, fi ? "Korttien koko (K)" : "Card size (K)", fi ? ["Suuri", "Pieni", "Kuvake"] : ["Large", "Small", "Icon"],
            Array.IndexOf(GameSettings.CardSizes, Settings.CardSize), i => Settings = Settings with { CardSize = GameSettings.CardSizes[i] });
        Check(column, fi ? "Seuraa miehiä alussa (L)" : "Follow the men at start (L)", Settings.FollowAtStart, v => Settings = Settings with { FollowAtStart = v });
        Check(column, fi ? "Näytä kursorin ohje (mitä klikkaus tekee)" : "Show the cursor hint (what a click does)", Settings.CursorHints, v => Settings = Settings with { CursorHints = v });
        Check(column, fi ? "Näytä hyökkäyssuunnitelma ensin (tuplaklikkaus viholliseen)" : "Show the attack plan first (double click on an enemy)",
            Settings.PreviewAttacks, v => Settings = Settings with { PreviewAttacks = v });
        Ink(column, fi ? "Kieli vaihtuu kokonaan seuraavassa tehtävässä." : "The language changes fully from the next mission.", 14);
        column.AddChild(new HSeparator());

        // Every key and click, as in the F1 help, so they are found from here too.
        Ink(column, fi ? "Näppäimet ja hiiri" : "Keys and mouse", 20);
        var keys = new GridContainer { Columns = 2 };
        keys.AddThemeConstantOverride("h_separation", 14);
        foreach (var (keyText, what) in Nmf.Client.ControlsHelp.Lines(Language))
        {
            var k = new Label { Text = keyText, CustomMinimumSize = new Vector2(230, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
            k.AddThemeColorOverride("font_color", InkColour);
            k.AddThemeFontSizeOverride("font_size", 14);
            var w = new Label { Text = what, CustomMinimumSize = new Vector2(300, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
            w.AddThemeColorOverride("font_color", InkColour);
            w.AddThemeFontSizeOverride("font_size", 14);
            keys.AddChild(k);
            keys.AddChild(w);
        }
        column.AddChild(keys);

        var done = new Button { Text = fi ? "Valmis" : "Done", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(160, 38) };
        done.Pressed += Close;
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(done);
        outer.AddChild(new HSeparator());
        outer.AddChild(row);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Close()
    {
        Closed?.Invoke(Settings);
        QueueFree();
    }

    private void Slider(Container parent, string label, int value, Action<int> set)
    {
        var row = Row(parent, label);
        var slider = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = value, CustomMinimumSize = new Vector2(220, 24), FocusMode = Control.FocusModeEnum.None };
        var shown = new Label { Text = $"{value} %", CustomMinimumSize = new Vector2(60, 0) };
        shown.AddThemeColorOverride("font_color", InkColour);
        slider.ValueChanged += v =>
        {
            set((int)v);
            shown.Text = $"{(int)v} %";
            Changed?.Invoke(Settings);
        };
        row.AddChild(slider);
        row.AddChild(shown);
    }

    private void Check(Container parent, string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { Text = label, ButtonPressed = value, FocusMode = Control.FocusModeEnum.None };
        box.AddThemeColorOverride("font_color", InkColour);
        box.AddThemeColorOverride("font_pressed_color", InkColour);
        box.AddThemeColorOverride("font_hover_color", InkColour);
        box.Toggled += v =>
        {
            set(v);
            Changed?.Invoke(Settings);
        };
        parent.AddChild(box);
    }

    private void Choice(Container parent, string label, string[] options, int selected, Action<int> set)
    {
        var row = Row(parent, label);
        var choice = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(180, 0) };
        foreach (var option in options)
            choice.AddItem(option);
        choice.Selected = Math.Max(0, selected);
        choice.ItemSelected += i =>
        {
            set((int)i);
            Changed?.Invoke(Settings);
        };
        row.AddChild(choice);
    }

    private static readonly Color InkColour = new(0.15f, 0.12f, 0.08f);

    private static HBoxContainer Row(Container parent, string label)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var name = new Label { Text = label, CustomMinimumSize = new Vector2(230, 0) };
        name.AddThemeColorOverride("font_color", InkColour);
        row.AddChild(name);
        parent.AddChild(row);
        return row;
    }

    private static void Ink(Container parent, string text, int size)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", InkColour);
        parent.AddChild(label);
    }
}

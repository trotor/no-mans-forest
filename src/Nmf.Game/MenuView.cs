using System;
using System.Collections.Generic;
using Godot;
using Nmf.Client.Mission;
using Nmf.Content.Missions;

namespace Nmf.Game;

/// <summary>The mission menu (spec 2026-09-26-mission-menu-design §3): the missions with what has been done in each, the language, quit.</summary>
public partial class MenuView : CanvasLayer
{
    public IReadOnlyList<MissionLoader.Entry> Missions { get; set; } = [];
    public MissionProgress Progress { get; set; } = new();
    public string Language { get; set; } = "en";
    public Action<string, string>? MissionChosen { get; set; }

    private VBoxContainer _list = null!;
    private Label _title = null!, _subtitle = null!, _missionsHeading = null!;
    private Button _quit = null!, _fi = null!, _en = null!;

    public override void _Ready()
    {
        var root = new Control { Theme = Hud.BuildTheme() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var background = new ColorRect { Color = new Color(0.09f, 0.1f, 0.07f) };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(background);

        var paper = new PanelContainer();
        paper.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.93f, 0.9f, 0.8f), BorderColor = new Color(0.35f, 0.3f, 0.2f),
            BorderWidthLeft = 3, BorderWidthTop = 3, BorderWidthRight = 3, BorderWidthBottom = 3,
            ContentMarginLeft = 36, ContentMarginRight = 36, ContentMarginTop = 28, ContentMarginBottom = 28,
        });
        paper.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        paper.GrowHorizontal = Control.GrowDirection.Both;
        paper.GrowVertical = Control.GrowDirection.Both;
        paper.CustomMinimumSize = new Vector2(720, 0);
        root.AddChild(paper);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        paper.AddChild(column);
        _title = Ink(column, 44);
        _subtitle = Ink(column, 18);
        column.AddChild(new HSeparator());
        _missionsHeading = Ink(column, 22);
        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 8);
        column.AddChild(_list);
        column.AddChild(new HSeparator());

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _fi = new Button { Text = "Suomi", ToggleMode = true, FocusMode = Control.FocusModeEnum.None };
        _en = new Button { Text = "English", ToggleMode = true, FocusMode = Control.FocusModeEnum.None };
        _fi.Pressed += () => SetLanguage("fi");
        _en.Pressed += () => SetLanguage("en");
        row.AddChild(_fi);
        row.AddChild(_en);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _quit = new Button { FocusMode = Control.FocusModeEnum.None };
        _quit.Pressed += () => GetTree().Quit();
        row.AddChild(_quit);
        column.AddChild(row);
        Fill();
    }

    private void SetLanguage(string language)
    {
        Language = language;
        Fill();
    }

    private void Fill()
    {
        bool fi = Language == "fi";
        _title.Text = "No Man's Forest";
        _subtitle.Text = fi ? "Jatkosota 1942 — ryhmätaktiikkaa Karjalan metsissä" : "Continuation War, 1942 — squad tactics in the Karelian forest";
        _missionsHeading.Text = fi ? "Tehtävät" : "Missions";
        _quit.Text = fi ? "Lopeta" : "Quit";
        _fi.SetPressedNoSignal(fi);
        _en.SetPressedNoSignal(!fi);
        foreach (var child in _list.GetChildren())
            child.QueueFree();
        foreach (var entry in Missions)
        {
            string id = entry.Id;
            var button = new Button { FocusMode = Control.FocusModeEnum.None, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 70) };
            if (entry.Spec is { } spec)
            {
                button.Text = $"{spec.Title.In(Language)}\n{spec.Date.In(Language)}\n{Progress.StatusText(id, Language)}";
                button.Pressed += () => MissionChosen?.Invoke(id, Language);
            }
            else
            {
                button.Text = $"{id}\n{(fi ? "Tehtävää ei voi ladata: " : "Cannot load: ")}{entry.Error}";
                button.Disabled = true;
            }
            _list.AddChild(button);
        }
        if (Missions.Count == 0)
            Ink(_list, 18).Text = fi ? "Tehtäviä ei löytynyt (content/core/missions)." : "No missions found (content/core/missions).";
    }

    private static Label Ink(Container parent, int size)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color(0.15f, 0.12f, 0.08f));
        label.AddThemeConstantOverride("outline_size", 0);
        parent.AddChild(label);
        return label;
    }
}

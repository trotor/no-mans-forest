using System;
using System.Collections.Generic;
using Godot;
using Nmf.Client.Campaign;
using Nmf.Content.Campaigns;
using Nmf.Sim.Mission;

namespace Nmf.Game;

/// <summary>
/// The campaign between missions (spec 2026-10-11-campaign-design §9a, "Pelaajalle"): the story so far, who goes on the
/// next mission with which weapon, who stays behind, and the buttons to go, to start over or to go back to the menu.
/// </summary>
public partial class CampaignView : CanvasLayer
{
    public CampaignSpec Spec { get; set; } = null!;
    public CampaignState State { get; set; } = null!;
    /// <summary>The next chapter's mission as written (its slots and weapons); null when it cannot be loaded.</summary>
    public MissionSpec? Mission { get; set; }
    public Func<string, Localized?> MissionTitle { get; set; } = _ => null;
    public Func<string, string> WeaponName { get; set; } = id => id;
    public string Language { get; set; } = "en";
    public string? Error { get; set; }
    public Action? StartPressed { get; set; }
    public Action? RestartPressed { get; set; }
    public Action? BackPressed { get; set; }

    public override void _Ready()
    {
        bool fi = Language == "fi";
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
            ContentMarginLeft = 36, ContentMarginRight = 36, ContentMarginTop = 24, ContentMarginBottom = 24,
        });
        paper.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        paper.GrowHorizontal = Control.GrowDirection.Both;
        paper.GrowVertical = Control.GrowDirection.Both;
        paper.CustomMinimumSize = new Vector2(760, 0);
        root.AddChild(paper);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        paper.AddChild(column);
        Ink(column, 36).Text = Spec.Title.In(Language);
        Ink(column, 20).Text = CampaignText.Chapter(Spec, State, MissionTitle, Language);
        column.AddChild(new HSeparator());
        if (Error is { } error)
            Wrapped(column, 16, new Color(0.6f, 0.12f, 0.08f)).Text = error;

        // The story and the men scroll when the window is small.
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(700, 470), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        var inside = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        inside.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(inside);
        Wrapped(inside, 17).Text = CampaignText.Story(Spec, State, Language);
        bool canPlay = !State.Completed && CampaignMission.CanPlay(State) && Mission is not null;
        if (!State.Completed)
        {
            inside.AddChild(new HSeparator());
            if (Mission is not null && CampaignMission.CanPlay(State))
            {
                Ink(inside, 19).Text = fi ? "Tehtävään lähtevät" : "Going on the mission";
                foreach (var line in CampaignText.Lineup(Mission, State, WeaponName, Language))
                    Ink(inside, 16).Text = line;
                foreach (var line in CampaignText.Others(Mission, State, Language))
                    Wrapped(inside, 15).Text = line;
            }
            else if (!CampaignMission.CanPlay(State))
                Wrapped(inside, 17, new Color(0.6f, 0.12f, 0.08f)).Text = CampaignText.NobodyLeft(Language);
        }
        column.AddChild(new HSeparator());

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var start = new Button { Text = fi ? "Aloita tehtävä" : "Start the mission", FocusMode = Control.FocusModeEnum.None, Disabled = !canPlay };
        start.Pressed += () => StartPressed?.Invoke();
        row.AddChild(start);
        // Starting over throws the platoon's story away: the first press asks, the second does it.
        var restart = new Button { Text = fi ? "Aloita kampanja alusta" : "Start the campaign over", FocusMode = Control.FocusModeEnum.None };
        restart.Pressed += () =>
        {
            if (State.History.Count == 0 || restart.Text.StartsWith(fi ? "Vahvista" : "Confirm", StringComparison.Ordinal))
                RestartPressed?.Invoke();
            else
                restart.Text = fi ? "Vahvista: kaikki tähänastinen häviää" : "Confirm: all so far is lost";
        };
        row.AddChild(restart);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var back = new Button { Text = fi ? "Takaisin" : "Back", FocusMode = Control.FocusModeEnum.None };
        back.Pressed += () => BackPressed?.Invoke();
        row.AddChild(back);
        column.AddChild(row);
    }

    private static Label Ink(Container parent, int size, Color? color = null)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? new Color(0.15f, 0.12f, 0.08f));
        label.AddThemeConstantOverride("outline_size", 0);
        parent.AddChild(label);
        return label;
    }

    private static Label Wrapped(Container parent, int size, Color? color = null)
    {
        var label = Ink(parent, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(680, 0);
        return label;
    }
}

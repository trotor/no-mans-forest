using System;
using System.Linq;
using System.Collections.Generic;
using Godot;
using Nmf.Client.Campaign;
using Nmf.Content.Campaigns;
using Nmf.Client;
using Nmf.Sim.Mission;
using Nmf.Sim.Units;

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
    /// <summary>The player chose a specialty for a man (spec §9b): his name and the specialty.</summary>
    public Action<string, Specialty>? SpecialisePressed { get; set; }
    /// <summary>The lineup, the weapons or the platoon changed in <see cref="State"/> (spec §9c): to be saved.</summary>
    public Action? Changed { get; set; }

    private Control? _root;
    private ScrollContainer? _scroll;

    public override void _Ready() => Build();

    /// <summary>After a change: built again from the state, at the same scroll position.</summary>
    private void Rebuild()
    {
        Changed?.Invoke();
        int at = _scroll?.ScrollVertical ?? 0;
        _root?.QueueFree();
        Build();
        // The new list has its size only after a layout pass.
        Callable.From(() => { if (_scroll is not null) _scroll.ScrollVertical = at; }).CallDeferred();
    }

    private void Build()
    {
        bool fi = Language == "fi";
        var root = new Control { Theme = Hud.BuildTheme() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        _root = root;
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
        _scroll = scroll;
        var inside = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        inside.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(inside);
        Wrapped(inside, 17).Text = CampaignText.Story(Spec, State, Language);
        bool canPlay = !State.Completed && Mission is not null && CampaignMission.CanPlay(Mission, State);
        if (!State.Completed)
        {
            // Replacements and new specialties come first: they change who the lineup below sends.
            Recruits(inside);
            Specialties(inside);
            inside.AddChild(new HSeparator());
            if (Mission is not null && CampaignMission.CanPlay(State))
            {
                Lineup(inside, Mission);
                foreach (var line in CampaignText.Others(Mission, State, Language))
                    Wrapped(inside, 15).Text = line;
                if (CampaignText.Armoury(State, WeaponName, Language) is { } spare)
                    Wrapped(inside, 15).Text = spare;
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

    /// <summary>
    /// The mission's slots (spec §9c), one row each: what the slot is, who goes into it (or nobody), his weapon from
    /// the mission's and the armoury's, and where he stands.
    /// </summary>
    private void Lineup(Container inside, MissionSpec mission)
    {
        bool fi = Language == "fi";
        var head = new HBoxContainer();
        Ink(head, 19).Text = fi ? "Tehtävään lähtevät" : "Going on the mission";
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        if (State.Lineup.Count > 0)
        {
            var reset = new Button
            {
                Text = fi ? "Palauta oletus" : "Back to the default", FocusMode = Control.FocusModeEnum.None,
                TooltipText = fi ? "Miehet ja aseet paikoilleen sääntöjen mukaan" : "Men and weapons by the rules again",
            };
            reset.Pressed += () =>
            {
                State.Lineup.Clear();
                Rebuild();
            };
            head.AddChild(reset);
        }
        inside.AddChild(head);
        Wrapped(inside, 14).Text = fi
            ? "Valitse paikalle mies tai jätä se tyhjäksi; ase vaihtuu sen miehen kanssa, jolla se on, ellei varastossa ole vapaata."
            : "Pick a man for a slot or leave it empty; a weapon is swapped with the man who has it unless a spare one is free.";

        var fill = CampaignMission.Fill(mission, State);
        var weapons = CampaignLineup.Weapons(mission, State);
        var pool = CampaignLineup.Pool(mission, State);
        var men = CampaignMission.Available(State);
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 4);
        inside.AddChild(grid);
        for (int i = 0; i < mission.Player.Count; i++)
        {
            if (mission.Player[i].Stranded)
                continue;
            int slot = i;
            Ink(grid, 15).Text = CampaignText.SlotLabel(mission, slot, Language);

            var who = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(210, 0) };
            foreach (var man in men)
                who.AddItem(man.Name);
            who.AddItem(fi ? "— ei ketään —" : "— nobody —");
            who.Selected = fill[slot] is { } going ? men.ToList().IndexOf(going) : men.Count;
            who.ItemSelected += index =>
            {
                if (index < men.Count)
                    State.PickMan(mission, slot, men[(int)index].Name);
                else
                    State.LeaveEmpty(mission, slot);
                Rebuild();
            };
            grid.AddChild(who);

            var arms = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(170, 0), Disabled = fill[slot] is null };
            var ids = pool.Keys.ToList();
            foreach (var id in ids)
                arms.AddItem(WeaponName(id));
            arms.Selected = weapons[slot] is { } weapon ? ids.IndexOf(weapon) : -1;
            arms.ItemSelected += index =>
            {
                State.PickWeapon(mission, slot, ids[(int)index]);
                Rebuild();
            };
            grid.AddChild(arms);

            Ink(grid, 14).Text = fill[slot] is { } man2 ? CampaignText.Standing(man2, Language) : "";
        }
    }

    /// <summary>Replacements for the fallen (spec §9c): the open places and the men on offer, each with a button to take him.</summary>
    private void Recruits(Container inside)
    {
        bool fi = Language == "fi";
        int open = CampaignRecruits.Vacancies(Spec, State);
        var candidates = CampaignRecruits.Candidates(Spec, State);
        if (open == 0 || candidates.Count == 0)
            return;
        inside.AddChild(new HSeparator());
        Ink(inside, 19).Text = fi ? "Täydennys" : "Replacements";
        Wrapped(inside, 15).Text = CampaignText.Vacancies(open, Language);
        for (int k = 0; k < candidates.Count; k++)
        {
            int choice = k;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var take = new Button { Text = fi ? "Ota joukkoon" : "Take him", FocusMode = Control.FocusModeEnum.None };
            take.Pressed += () =>
            {
                if (State.Recruit(Spec, choice))
                    Rebuild();
            };
            row.AddChild(take);
            Ink(row, 15).Text = CampaignText.CandidateLine(candidates[k], Language);
            inside.AddChild(row);
        }
    }

    /// <summary>The seasoned men without a specialty, each with the four to choose from (a tooltip says what each does).</summary>
    private void Specialties(Container inside)
    {
        bool fi = Language == "fi";
        var able = CampaignText.Specialisable(State);
        if (able.Count == 0)
            return;
        inside.AddChild(new HSeparator());
        Ink(inside, 19).Text = fi ? "Erikoistuminen" : "Specialties";
        Wrapped(inside, 15).Text = fi ? "Kokenut mies voi erikoistua, kerran ja pysyvästi." : "A seasoned man may take a specialty, once and for good.";
        foreach (var man in able)
        {
            Ink(inside, 16).Text = CampaignText.SpecialiseLine(man, Language);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            foreach (var specialty in new[] { Specialty.Marksman, Specialty.Scout, Specialty.Sapper, Specialty.Medic })
            {
                string name = SpecialtyText.Name(specialty, Language);
                var button = new Button
                {
                    Text = char.ToUpperInvariant(name[0]) + name[1..], FocusMode = Control.FocusModeEnum.None,
                    TooltipText = SpecialtyText.Effect(specialty, Language),
                };
                string who = man.Name;
                button.Pressed += () => SpecialisePressed?.Invoke(who, specialty);
                row.AddChild(button);
            }
            inside.AddChild(row);
        }
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

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
        "Nothing selected       orders go to the whole platoon (both squads)\n" +
        "Click ground           go there (the men pick their pace) · double click: run · Alt/Option: crawl\n" +
        "Click enemy            fire at him · double click: attack (half dash to cover, half give covering fire,\n" +
        "                       then grenades and bayonets) · Shift + double click: straight assault\n" +
        "Double click fallen man   nearest man searches him (ammo, grenades, weapon, papers)\n" +
        "Under fire             men run to the nearest cover or drop prone; ★ tough men hold their ground\n" +
        "B / M                  mission orders / map of the area (click it to look there)\n" +
        "Click own soldier      command only him (Shift adds) · double click: his whole squad (or its name on the cards)\n" +
        "Ctrl/Cmd + click       area fire at that place · click a \"?\" (enemy last seen / heard): area fire there\n" +
        "Drag                   box select · Right click / Esc: the whole platoon again\n" +
        "1 / 2 · 0              select the strike / support squad (again: look at it) · the whole platoon\n" +
        "Z / X / C              stand / crouch / go prone\n" +
        "H                      halt\n" +
        "Space                  pause (orders still work)\n" +
        "+ / -                  game speed ×0.25 … ×8 (or the ▌▌ ×1 ×2 ×4 ×8 buttons beside the clock)\n" +
        "Hold the mouse on an enemy   what he looks like: leader or rifleman, weapon, what he does, distance\n" +
        "WASD, arrows, middle drag, two-finger pan   move camera\n" +
        "Wheel, pinch           zoom\n" +
        "Tab / Esc              select all / clear selection (Esc with nothing selected: the game menu)\n" +
        "Cards                  click selects, double click centres camera · K: large / small / icons\n" +
        "O                      clear outlines: the men rimmed in black and their side's colour\n" +
        "P                      fire policy: fire at will / return fire / hold fire\n" +
        "F11                    fullscreen\n" +
        "F                      debug: reveal all units\n" +
        "F1                     close this help";

    private const string HelpTextFi =
        "Ei valintaa            käskyt koko joukkueelle (molemmat ryhmät)\n" +
        "Klikkaus maastoon      mene sinne (miehet valitsevat tahdin) · tuplaklikkaus: juokse · Alt/Option: ryömi\n" +
        "Klikkaus viholliseen   ammu häntä · tuplaklikkaus: hyökkää (puolet syöksyy suojaan, puolet antaa suojatulta,\n" +
        "                       lopuksi kranaatit ja pistimet) · Shift + tuplaklikkaus: suora rynnäkkö\n" +
        "Tuplaklikkaus kaatuneeseen   lähin mies tutkii hänet (patruunat, kranaatit, ase, paperit)\n" +
        "Tulen alla             miehet juoksevat lähimpään suojaan tai menevät maahan; ★ kovimmat pysyvät paikoillaan\n" +
        "B / M                  tehtäväkäsky / alueen kartta (klikkaa katsoaksesi sinne)\n" +
        "Klikkaus omaan mieheen komenna vain häntä (Shift lisää) · tuplaklikkaus: hänen ryhmänsä (tai ryhmän nimi korteissa)\n" +
        "Ctrl/Cmd + klikkaus    aluetuli siihen kohtaan · klikkaus \"?\"-merkkiin (viimeksi nähty / kuultu): aluetuli sinne\n" +
        "Vedä                   laatikkovalinta · oikea klikkaus / Esc: taas koko joukkue\n" +
        "1 / 2 · 0              isku- / tukiryhmä (uudelleen: katso sitä) · koko joukkue\n" +
        "Z / X / C              seiso / kyykky / maahan\n" +
        "H                      pysähdy\n" +
        "Välilyönti             tauko (käskyjä voi antaa)\n" +
        "+ / -                  pelinopeus ×0,25 … ×8 (tai ▌▌ ×1 ×2 ×4 ×8 kellon vieressä)\n" +
        "Hiiri vihollisen päällä   millainen mies: johtaja vai kiväärimies, ase, mitä tekee, etäisyys\n" +
        "WASD, nuolet, keskinapin veto, kahden sormen veto   kamera\n" +
        "Rulla, nipistys        zoomaus\n" +
        "Tab / Esc              valitse kaikki / tyhjennä valinta (Esc ilman valintaa: pelin valikko)\n" +
        "Kortit                 klikkaus valitsee, tuplaklikkaus keskittää kameran · K: isot / pienet / ikonit\n" +
        "O                      selkeät reunat: miehet mustin ja puolensa värisin ääriviivoin\n" +
        "P                      tulitoiminta: vapaa tuli / vastatuli / tulenavauskielto\n" +
        "F11                    koko näyttö\n" +
        "F                      testaus: näytä kaikki yksiköt\n" +
        "F1                     sulje tämä ohje";

    private static readonly Color PanelColor = new(0.11f, 0.12f, 0.09f, 0.9f);
    private static readonly Color BorderColor = new(0.45f, 0.43f, 0.3f);
    private static readonly Color SelectedBorder = new(1f, 0.85f, 0.3f);

    private readonly List<Card> _cards = [];

    private sealed record Card(UnitId Id, string Name, PanelContainer Panel, TextureRect Portrait, Label NameLabel, Label Status, Label Condition, Label Policy, ProgressBar Morale, ProgressBar Suppression, StyleBoxFlat Style, Label Badge, StyleBoxFlat BadgeStyle);

    private static readonly Color MoraleGreen = new(0.35f, 0.7f, 0.3f);

    private static Color ToneColour(BadgeTone tone) => tone switch
    {
        BadgeTone.Warning => new Color(0.93f, 0.78f, 0.2f),
        BadgeTone.Bad => new Color(0.95f, 0.5f, 0.1f),
        BadgeTone.Critical => new Color(0.85f, 0.15f, 0.1f),
        BadgeTone.Gone => new Color(0.42f, 0.42f, 0.42f),
        _ => MoraleGreen,
    };

    /// <summary>The card size the player picked; kept over a restart of the mission.</summary>
    internal static CardSize CardSizeChoice { get; set; } = CardSize.Large;
    private Button _cardSizeButton = null!;
    private PanelContainer _bottom = null!;
    /// <summary>Told the new height of the card bar when the cards change size.</summary>
    public Action<float>? CardBarHeightChanged { get; set; }
    private Label _status = null!;

    /// <summary>Height of the top bar in base pixels; full-screen papers start below it.</summary>
    public const float TopBarHeight = 52f;
    private PanelContainer _help = null!;
    private Label _objectives = null!;
    private Label _toast = null!;
    private double _toastLeft;
    private PanelContainer _end = null!;
    private Label _endText = null!;
    private Control _root = null!;

    public Action? OrdersPressed { get; set; }
    public Action? PausePressed { get; set; }
    public Action<int>? SquadPressed { get; set; }
    public Action<double>? SpeedPressed { get; set; }
    /// <summary>Whether the game is (or, with a paper open, will be) paused: the ▌▌ button shows it.</summary>
    public Func<bool>? PausedAfter { get; set; }

    private static readonly double[] Speeds = [1, 2, 4, 8];
    private Button _pause = null!;
    private readonly List<(double Speed, Button Button)> _speedButtons = [];
    private PanelContainer _tip = null!;
    private Label _clock = null!;
    private readonly List<(int Squad, Button Header)> _squadHeaders = [];
    private Label _otherSpeed = null!;
    private PanelContainer _nextStep = null!;
    private GuideView _guide = null!;

    /// <summary>How much of the guide shows (it fades out while the men are busy).</summary>
    public float GuideAlpha
    {
        set => _guide.Modulate = new Color(1, 1, 1, value);
    }

    /// <summary>The way to the next objective (screen position), or none.</summary>
    public void ShowGuide(Vector2? screen, string text)
    {
        if (_guide.Target == screen && _guide.Text == text)
            return;
        _guide.Target = screen;
        _guide.Text = text;
        _guide.QueueRedraw();
    }
    private Label _nextStepText = null!;
    private Label _tipText = null!;

    /// <summary>The hover tip beside the cursor, or none.</summary>
    public void ShowTip(string? text, Vector2 mouse)
    {
        _tip.Visible = text is not null;
        if (text is null)
            return;
        if (_tipText.Text != text)
        {
            _tipText.Text = text;
            _tip.ResetSize();
        }
        var view = _root.GetViewportRect().Size;
        var size = _tip.Size;
        var at = mouse + new Vector2(22, 22);
        if (at.X + size.X > view.X - 8)
            at.X = mouse.X - size.X - 12;
        if (at.Y + size.Y > view.Y - 8)
            at.Y = mouse.Y - size.Y - 12;
        _tip.Position = new Vector2(Math.Clamp(at.X, 8, Math.Max(8, view.X - size.X - 8)), Math.Clamp(at.Y, 8, Math.Max(8, view.Y - size.Y - 8)));
    }
    public Action? MapPressed { get; set; }

    /// <summary>Full-screen views (orders, map) go on the HUD layer, over everything else.</summary>
    public void AddOverlay(Control overlay) => _root.AddChild(overlay);

    public bool EndPanelVisible => _end.Visible;

    public Action? RetryPressed { get; set; }
    public Action? MenuPressed { get; set; }
    public Action? QuitPressed { get; set; }
    private Control _menu = null!;

    /// <summary>The game menu; the owner pauses the war while it is visible.</summary>
    public Control GameMenu => _menu;

    /// <summary>Called before the menu opens (the owner closes the papers so it is never hidden under them).</summary>
    public Action? MenuOpening { get; set; }

    public void ToggleMenu()
    {
        if (_menu.Visible)
        {
            _menu.Visible = false;
            return;
        }
        MenuOpening?.Invoke();
        _end.Visible = false;
        _menu.MoveToFront();
        _menu.Visible = true;
    }

    public void HideMenu() => _menu.Visible = false;

    private static HBoxContainer ButtonRow(params (string Text, Action Pressed)[] buttons)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        foreach (var (text, pressed) in buttons)
        {
            var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(170, 38) };
            button.Pressed += pressed;
            row.AddChild(button);
        }
        return row;
    }

    public void HideEndPanel() => _end.Visible = false;

    /// <summary>Objective and mission-end messages.</summary>
    /// <param name="result">How the mission went, when it ended in these events (computed once, by the caller).</param>
    public void OnEvents(System.Collections.Generic.IEnumerable<Nmf.Sim.Events.SimEvent> events, Nmf.Client.Mission.MissionResult? result = null)
    {
        foreach (var e in events)
            if (e is Nmf.Sim.Events.CounterattackStarted shout && shout.Side != Session.PlayerSide
                && Nmf.Client.Mission.Alerts.Counterattack(shout, Session.OwnUnits.Where(u => !u.IsOutOfAction).Select(u => u.Position), Session.Language) is { } heard)
                ShowToast(heard);
        if (Session.Tracker is not { } tracker)
            return;
        bool fi = Session.Language == "fi";
        foreach (var e in events)
        {
            if (e is Nmf.Sim.Events.ObjectiveChanged changed && tracker.Spec.Objectives.FirstOrDefault(o => o.Id == changed.Id) is { } objective)
            {
                string text = objective.Text.In(Session.Language);
                ShowToast(changed.Done ? (fi ? "Tavoite täytetty: " : "Objective complete: ") + text
                                       : (fi ? "Tavoite menetetty: " : "Objective lost: ") + text);
            }
            else if (e is Nmf.Sim.Events.MissionEnded ended)
            {
                result ??= Nmf.Client.Mission.MissionResult.From(Session);
                string losses = Nmf.Client.Mission.MissionProgress.LossesText(result.OwnKilled, result.OwnWounded, Session.Language);
                string head = ended.Success ? (fi ? "TEHTÄVÄ SUORITETTU" : "MISSION ACCOMPLISHED") : (fi ? "TEHTÄVÄ EPÄONNISTUI" : "MISSION FAILED");
                string stats = fi
                    ? $"Aika {result.TimeText} · tavoitteet {result.ObjectivesDone}/{result.Objectives}\nOmat tappiot: {losses}\nVihollisia pois taistelusta (nähty): {result.EnemyDown}"
                    : $"Time {result.TimeText} · objectives {result.ObjectivesDone}/{result.Objectives}\nOwn losses: {losses}\nEnemy out of action (seen): {result.EnemyDown}";
                _endText.Text = $"{head}\n\n{stats}";
                _end.Visible = true;
            }
        }
    }

    public void ShowToast(string text)
    {
        _toast.Text = text;
        _toast.Visible = true;
        _toastLeft = 4;
    }

    public void Tick(double delta)
    {
        if (_toast.Visible && (_toastLeft -= delta) <= 0)
            _toast.Visible = false;
        if (Session.Tracker is { } tracker)
            _objectives.Text = (Session.Language == "fi" ? "Tavoitteet " : "Objectives ") + $"{tracker.DoneCount}/{tracker.Spec.Objectives.Count}";
    }

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
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 12);
        // The clock, then the speed where the game time is read, then the rest of the news.
        _clock = new Label();
        bar.AddChild(_clock);
        _pause = new Button { Text = "▌▌", ToggleMode = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Space" };
        _pause.Pressed += () => PausePressed?.Invoke();
        bar.AddChild(_pause);
        foreach (double speed in Speeds)
        {
            var button = new Button { Text = $"×{speed}", ToggleMode = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "+ / −" };
            button.Pressed += () => SpeedPressed?.Invoke(speed);
            bar.AddChild(button);
            _speedButtons.Add((speed, button));
        }
        _otherSpeed = new Label();
        bar.AddChild(_otherSpeed);
        _status = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
        bar.AddChild(_status);
        _objectives = new Label();
        bar.AddChild(_objectives);
        if (Session.Mission is not null)
        {
            var orders = new Button { Text = Session.Language == "fi" ? "Käsky (B)" : "Orders (B)", FocusMode = Control.FocusModeEnum.None };
            orders.Pressed += () => OrdersPressed?.Invoke();
            bar.AddChild(orders);
        }
        var mapButton = new Button { Text = Session.Language == "fi" ? "Kartta (M)" : "Map (M)", FocusMode = Control.FocusModeEnum.None };
        mapButton.Pressed += () => MapPressed?.Invoke();
        bar.AddChild(mapButton);
        var menuButton = new Button { Text = Session.Language == "fi" ? "Valikko" : "Menu", FocusMode = Control.FocusModeEnum.None, TooltipText = "Esc" };
        menuButton.Pressed += ToggleMenu;
        bar.AddChild(menuButton);
        top.AddChild(bar);
        root.AddChild(top);

        // What to do next, on a strip of paper under the top bar.
        _nextStep = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(12, TopBarHeight + 8) };
        _nextStep.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.93f, 0.9f, 0.8f, 0.92f), BorderColor = new Color(0.35f, 0.3f, 0.2f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        _nextStepText = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _nextStepText.AddThemeColorOverride("font_color", new Color(0.15f, 0.12f, 0.08f));
        _nextStep.AddChild(_nextStepText);
        root.AddChild(_nextStep);

        _guide = new GuideView { MouseFilter = Control.MouseFilterEnum.Ignore };
        _guide.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_guide);

        _toast = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _toast.GrowHorizontal = Control.GrowDirection.Both;
        _toast.OffsetTop = 70;
        _toast.AddThemeFontSizeOverride("font_size", 26);
        _toast.AddThemeColorOverride("font_outline_color", Colors.Black);
        _toast.AddThemeConstantOverride("outline_size", 8);
        root.AddChild(_toast);

        bool fiUi = Session.Language == "fi";
        _end = new PanelContainer { Visible = false };
        _end.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _end.GrowHorizontal = Control.GrowDirection.Both;
        _end.GrowVertical = Control.GrowDirection.Both;
        var endColumn = new VBoxContainer();
        endColumn.AddThemeConstantOverride("separation", 14);
        _endText = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _endText.AddThemeFontSizeOverride("font_size", 22);
        endColumn.AddChild(_endText);
        endColumn.AddChild(ButtonRow(
            (fiUi ? "Jatka katselua" : "Keep watching", () => _end.Visible = false),
            (fiUi ? "Yritä uudelleen" : "Try again", () => RetryPressed?.Invoke()),
            (fiUi ? "Tehtävävalikko" : "Missions", () => MenuPressed?.Invoke())));
        _end.AddChild(endColumn);
        // Added after the card bar (below) so it is drawn over it, and lifted clear of it.
        _end.OffsetTop -= 90;
        _end.OffsetBottom -= 90;

        // The game menu (Esc with nothing selected, or the button in the bar): the war waits while it is open.
        // Modal: a dim cover over the whole view catches the clicks, the panel sits in its middle.
        _menu = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _menu.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _menu.AddChild(dim);
        var menuPanel = new PanelContainer();
        menuPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        menuPanel.GrowHorizontal = Control.GrowDirection.Both;
        menuPanel.GrowVertical = Control.GrowDirection.Both;
        _menu.AddChild(menuPanel);
        var menuColumn = new VBoxContainer();
        menuColumn.AddThemeConstantOverride("separation", 10);
        var menuTitle = new Label { Text = Session.Mission?.Title.In(Session.Language) ?? "No Man's Forest", HorizontalAlignment = HorizontalAlignment.Center };
        menuTitle.AddThemeFontSizeOverride("font_size", 24);
        menuColumn.AddChild(menuTitle);
        foreach (var (text, action) in new (string, Action)[]
                 {
                     (fiUi ? "Jatka" : "Resume", () => _menu.Visible = false),
                     (fiUi ? "Aloita alusta" : "Restart", () => RetryPressed?.Invoke()),
                     (fiUi ? "Tehtävävalikko" : "Missions", () => MenuPressed?.Invoke()),
                     (fiUi ? "Lopeta peli" : "Quit", () => QuitPressed?.Invoke()),
                 })
        {
            var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(260, 40) };
            button.Pressed += action;
            menuColumn.AddChild(button);
        }
        menuPanel.AddChild(menuColumn);
        root.AddChild(_menu);
        _root = root;

        // Only as wide as its cards, so the rest of the south edge of the map stays visible.
        var bottom = new PanelContainer { GrowVertical = Control.GrowDirection.Begin, GrowHorizontal = Control.GrowDirection.End };
        bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _bottom = bottom;
        var cardColumn = new VBoxContainer();
        cardColumn.AddThemeConstantOverride("separation", 2);
        _cardSizeButton = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        _cardSizeButton.AddThemeFontSizeOverride("font_size", 12);
        _cardSizeButton.Pressed += CycleCardSize;
        cardColumn.AddChild(_cardSizeButton);
        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 8);
        cardColumn.AddChild(cards);
        bottom.AddChild(cardColumn);
        root.AddChild(bottom);
        root.AddChild(_end);

        // Grouped by squad; with more than one, each squad's name above its cards selects the whole squad.
        int index = 0;
        var squads = Session.OwnUnits.GroupBy(u => u.Squad).OrderBy(g => g.Key).ToList();
        foreach (var squad in squads)
        {
            var group = new VBoxContainer();
            group.AddThemeConstantOverride("separation", 2);
            if (squads.Count > 1)
            {
                int number = squad.Key;
                var header = new Button
                {
                    Text = $"{number + 1} · {Session.SquadName(number)}", ToggleMode = true, FocusMode = Control.FocusModeEnum.None,
                    TooltipText = Session.Language == "fi" ? $"Valitse koko ryhmä (näppäin {number + 1}, uudelleen: katso sitä · 0: koko joukkue)"
                                                           : $"Select the whole squad (key {number + 1}, again: look at it · 0: the whole platoon)",
                };
                header.Pressed += () => SquadPressed?.Invoke(number);
                group.AddChild(header);
                _squadHeaders.Add((number, header));
            }
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            foreach (var unit in squad)
                row.AddChild(BuildCard(unit, index++));
            group.AddChild(row);
            cards.AddChild(group);
        }

        ApplyCardSize();

        _help = new PanelContainer { Visible = false };
        _help.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _help.GrowHorizontal = Control.GrowDirection.Both;
        _help.GrowVertical = Control.GrowDirection.Both;
        var credits = Session.Sim.Map.Features.Properties.TryGetValue("source", out var source) ? "\n\nMap: " + Wrap(source, 90) : "";
        var helpLabel = new Label { Text = (Session.Language == "fi" ? HelpTextFi : HelpText) + credits };
        helpLabel.AddThemeFontOverride("font", MonospaceFont());
        _help.AddChild(helpLabel);
        root.AddChild(_help);

        _tip = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _tip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.93f, 0.9f, 0.8f, 0.96f), BorderColor = new Color(0.35f, 0.3f, 0.2f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        _tipText = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _tipText.AddThemeColorOverride("font_color", new Color(0.15f, 0.12f, 0.08f));
        _tip.AddChild(_tipText);
        root.AddChild(_tip);

        Refresh();
    }

    public void ToggleHelp() => _help.Visible = !_help.Visible;

    public bool HelpVisible => _help.Visible;

    public void Refresh()
    {
        var t = Session.GameTime;
        _clock.Text = string.Create(CultureInfo.InvariantCulture, $"  {(int)t.TotalMinutes:00}:{t.Seconds:00}");
        double scale = Session.Clock.TimeScale;
        _otherSpeed.Text = Speeds.Any(s => Math.Abs(s - scale) < 1e-9) ? "" : string.Create(CultureInfo.InvariantCulture, $"×{scale:0.##}");
        string commanding = Session.CommandingText(Session.Language, u => _cards.FirstOrDefault(c => c.Id == u.Id)?.Name ?? u.Id.ToString());
        _status.Text = "    " + HudText.Status(Session, Session.Language, commanding);
        if (Session.Tracker is { } tracker && Nmf.Client.Mission.MissionPaper.NextStep(tracker, Session.Language) is { Length: > 0 } next)
        {
            if (_nextStepText.Text != next)
            {
                _nextStepText.Text = next;
                _nextStep.ResetSize();
            }
            _nextStep.Visible = true;
        }


        int? selectedSquad = Session.SelectedSquad;
        foreach (var (squad, header) in _squadHeaders)
        {
            header.Disabled = !Session.OwnUnits.Any(u => u.Squad == squad && !u.IsOutOfAction); // nobody left to select
            header.SetPressedNoSignal(selectedSquad == squad);
        }
        bool pausedAfter = PausedAfter?.Invoke() ?? Session.Clock.Paused;
        _pause.SetPressedNoSignal(pausedAfter);
        foreach (var (speed, button) in _speedButtons)
            button.SetPressedNoSignal(!pausedAfter && Math.Abs(Session.Clock.TimeScale - speed) < 1e-9);

        foreach (var card in _cards)
        {
            if (Session.Sim.FindUnit(card.Id) is not { } unit)
                continue;
            string lang = Session.Language;
            card.Status.Text = UnitStatus.Describe(unit, lang);
            card.Condition.Text = UnitStatus.Condition(unit, lang);
            card.Policy.Text = UnitStatus.CardLine(unit, lang);
            card.Policy.TooltipText = lang == "fi"
                ? $"{UnitStatus.AmmoText(unit, lang)} (lippaassa + varalippaat) · {unit.Grenades} kranaattia · {UnitStatus.PolicyName(unit.FirePolicy, lang)}"
                : $"{UnitStatus.AmmoText(unit)} (rounds + spare magazines) · {unit.Grenades} grenades · {UnitStatus.PolicyName(unit.FirePolicy)}";
            card.Panel.TooltipText = CardSizeChoice == CardSize.Large ? ""
                : CardSizes.Tooltip(card.NameLabel.Text, card.Status.Text, card.Condition.Text, card.Policy.TooltipText);
            var mark = CardBadges.For(unit);
            card.Badge.Text = mark.Glyph;
            card.Badge.Visible = mark.Glyph.Length > 0;
            card.BadgeStyle.BgColor = ToneColour(mark.Tone);
            if (card.Morale.GetThemeStylebox("fill") is StyleBoxFlat fill)
                fill.BgColor = ToneColour(CardBadges.MoraleTone(unit.Morale));
            card.Morale.Value = unit.IsOutOfAction ? 0 : unit.Morale;
            card.Suppression.Value = unit.Suppression;
            card.Style.BorderColor = Session.Selection.Contains(card.Id) ? SelectedBorder : BorderColor;
            card.Panel.Modulate = unit.Wound == WoundLevel.Dead ? new Color(0.55f, 0.55f, 0.55f) : Colors.White;
        }
    }

    /// <summary>Breaks a long line at spaces so the help panel stays narrow.</summary>
    private static string Wrap(string text, int width)
    {
        var lines = new System.Collections.Generic.List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > width)
            {
                lines.Add(line);
                line = "";
            }
            line = line.Length == 0 ? word : line + " " + word;
        }
        lines.Add(line);
        return string.Join("\n     ", lines);
    }

    private PanelContainer BuildCard(Unit unit, int index)
    {
        var style = PanelStyle();
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer();
        panel.AddChild(column);

        var portrait = new TextureRect
        {
            Texture = new AtlasTexture { Atlas = Art.Portraits(unit.Side), Region = new Rect2(UnitNames.PortraitIndex(index) * 64, 0, 64, 64) },
            CustomMinimumSize = new Vector2(96, 96),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddChild(portrait);
        // The worst of his troubles, in a coloured tab on the portrait's corner: easy to read on the smallest card.
        var badgeStyle = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = new Color(0, 0, 0, 0.8f) };
        badgeStyle.SetCornerRadiusAll(4);
        badgeStyle.SetBorderWidthAll(1);
        badgeStyle.ContentMarginLeft = badgeStyle.ContentMarginRight = 3;
        var badge = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
        badge.AddThemeStyleboxOverride("normal", badgeStyle);
        badge.AddThemeColorOverride("font_color", Colors.White);
        badge.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        badge.GrowHorizontal = Control.GrowDirection.Begin;
        portrait.AddChild(badge);
        var name = new Label { Text = UnitNames.Of(unit, index) + (unit.IsTough ? " ★" : ""), TooltipText = unit.IsTough ? "Tough: holds his ground under fire" : "", MouseFilter = Control.MouseFilterEnum.Ignore };
        name.AddThemeFontSizeOverride("font_size", 15);
        column.AddChild(name);
        var status = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        status.AddThemeFontSizeOverride("font_size", 14);
        status.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.65f));
        column.AddChild(status);
        var condition = SmallLabel(column, new Color(0.9f, 0.75f, 0.7f));
        var policy = SmallLabel(column, new Color(0.7f, 0.8f, 0.9f));
        var morale = Bar(column, MoraleGreen);
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
        _cards.Add(new Card(id, UnitNames.Of(unit, index), panel, portrait, name, status, condition, policy, morale, suppression, style, badge, badgeStyle));
        return panel;
    }

    /// <summary>Large cards, small cards, icons, and round again (the K key or the button above the cards).</summary>
    public void CycleCardSize()
    {
        CardSizeChoice = CardSizes.Next(CardSizeChoice);
        ApplyCardSize();
    }

    private void ApplyCardSize()
    {
        var layout = CardSizes.Layout(CardSizeChoice);
        foreach (var card in _cards)
        {
            card.Portrait.CustomMinimumSize = new Vector2(layout.PortraitPx, layout.PortraitPx);
            card.NameLabel.Visible = layout.Name;
            card.NameLabel.AddThemeFontSizeOverride("font_size", layout.FontSize);
            card.Status.Visible = layout.Name;
            card.Status.AddThemeFontSizeOverride("font_size", layout.FontSize - 1);
            card.Condition.Visible = layout.Details;
            card.Badge.AddThemeFontSizeOverride("font_size", CardSizeChoice == CardSize.Large ? 18 : 14);
            card.Policy.Visible = layout.Details;
            foreach (var bar in new[] { card.Morale, card.Suppression })
                bar.CustomMinimumSize = new Vector2(layout.PortraitPx, CardSizeChoice == CardSize.Icon ? 4 : 6);
            card.Style.SetContentMarginAll(CardSizeChoice == CardSize.Large ? 8 : 4);
        }
        foreach (var (squad, header) in _squadHeaders)
            header.Text = CardSizeChoice == CardSize.Icon ? $"{squad + 1}" : $"{squad + 1} · {Session.SquadName(squad)}";
        _cardSizeButton.Text = CardSizes.ButtonText(CardSizeChoice, Session.Language);
        _bottom.ResetSize(); // shrink to the smaller cards
        _bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.KeepSize);
        CardBarHeightChanged?.Invoke(layout.BarHeightPx);
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

    internal static Theme BuildTheme()
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

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
        "Tab / Esc              select all / clear selection\n" +
        "Cards                  click selects, double click centres camera\n" +
        "P                      fire policy: fire at will / return fire / hold fire\n" +
        "F11                    fullscreen\n" +
        "F                      debug: reveal all units\n" +
        "F1                     close this help";

    private static readonly Color PanelColor = new(0.11f, 0.12f, 0.09f, 0.9f);
    private static readonly Color BorderColor = new(0.45f, 0.43f, 0.3f);
    private static readonly Color SelectedBorder = new(1f, 0.85f, 0.3f);

    private readonly List<Card> _cards = [];

    private sealed record Card(UnitId Id, string Name, PanelContainer Panel, Label Status, Label Condition, Label Policy, ProgressBar Morale, ProgressBar Suppression, StyleBoxFlat Style);
    private Label _status = null!;

    /// <summary>Approximate height of the card bar in base pixels; the camera may scroll this far past the map's south edge.</summary>
    public const float BottomBarHeight = 190f;
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
    private PanelContainer _menu = null!;

    /// <summary>The game menu; the owner pauses the war while it is visible.</summary>
    public PanelContainer GameMenu => _menu;

    public void ToggleMenu() => _menu.Visible = !_menu.Visible;

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
    public void OnEvents(System.Collections.Generic.IEnumerable<Nmf.Sim.Events.SimEvent> events)
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
                var result = Nmf.Client.Mission.MissionResult.From(Session);
                string head = ended.Success ? (fi ? "TEHTÄVÄ SUORITETTU" : "MISSION ACCOMPLISHED") : (fi ? "TEHTÄVÄ EPÄONNISTUI" : "MISSION FAILED");
                string stats = fi
                    ? $"Aika {result.TimeText} · tavoitteet {result.ObjectivesDone}/{result.Objectives}\nOmat tappiot: {result.OwnKilled} kaatunutta, {result.OwnWounded} haavoittunutta\nVihollisia pois taistelusta (nähty): {result.EnemyDown}"
                    : $"Time {result.TimeText} · objectives {result.ObjectivesDone}/{result.Objectives}\nOwn losses: {result.OwnKilled} killed, {result.OwnWounded} wounded\nEnemy out of action (seen): {result.EnemyDown}";
                _endText.Text = $"{head}\n\n{stats}";
                _end.Visible = true;
            }
        }
    }

    private void ShowToast(string text)
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
        root.AddChild(_end);

        // The game menu (Esc with nothing selected, or the button in the bar): the war waits while it is open.
        _menu = new PanelContainer { Visible = false };
        _menu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _menu.GrowHorizontal = Control.GrowDirection.Both;
        _menu.GrowVertical = Control.GrowDirection.Both;
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
        _menu.AddChild(menuColumn);
        root.AddChild(_menu);
        _root = root;

        // Only as wide as its cards, so the rest of the south edge of the map stays visible.
        var bottom = new PanelContainer { GrowVertical = Control.GrowDirection.Begin, GrowHorizontal = Control.GrowDirection.End };
        bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 8);
        bottom.AddChild(cards);
        root.AddChild(bottom);

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

        _help = new PanelContainer { Visible = false };
        _help.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _help.GrowHorizontal = Control.GrowDirection.Both;
        _help.GrowVertical = Control.GrowDirection.Both;
        var credits = Session.Sim.Map.Features.Properties.TryGetValue("source", out var source) ? "\n\nMap: " + Wrap(source, 90) : "";
        var helpLabel = new Label { Text = HelpText + credits };
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
        var contacts = Session.Knowledge.Contacts.ToList();
        int seen = contacts.Count(c => c.Level == ContactLevel.Visible && Session.Sim.FindUnit(c.Target) is { IsOutOfAction: false });
        int heard = contacts.Count(c => c.Level == ContactLevel.Suspected);
        int lastKnown = contacts.Count(c => c.Level == ContactLevel.LastKnown);
        var t = Session.GameTime;
        _clock.Text = string.Create(CultureInfo.InvariantCulture, $"  {(int)t.TotalMinutes:00}:{t.Seconds:00}");
        double scale = Session.Clock.TimeScale;
        _otherSpeed.Text = Speeds.Any(s => Math.Abs(s - scale) < 1e-9) ? "" : string.Create(CultureInfo.InvariantCulture, $"×{scale:0.##}");
        _status.Text = $"    Enemy: {seen} seen · {heard} heard · {lastKnown} last known      F1 help";
        if (Session.Tracker is { } tracker && Nmf.Client.Mission.MissionPaper.NextStep(tracker, Session.Language) is { Length: > 0 } next)
        {
            if (_nextStepText.Text != next)
            {
                _nextStepText.Text = next;
                _nextStep.ResetSize();
            }
            _nextStep.Visible = true;
        }

        int dead = Session.OwnUnits.Count(u => u.Wound == WoundLevel.Dead);
        int wounded = Session.OwnUnits.Count(u => u.Wound is > WoundLevel.None and < WoundLevel.Dead);
        int enemyDown = Session.Sim.Units.Count(u => u.Side != Session.PlayerSide && u.IsOutOfAction && Session.Knowledge.LevelOf(u.Id) == ContactLevel.Visible);
        _status.Text += string.Create(CultureInfo.InvariantCulture, $"      Losses: {dead} KIA · {wounded} wounded   Enemy down (seen): {enemyDown}");
        string commanding = Session.CommandingText("en", u => _cards.FirstOrDefault(c => c.Id == u.Id)?.Name ?? u.Id.ToString());
        _status.Text += $"      Commanding: {commanding}";
        if (Session.CarriedPapers.Count > 0)
            _status.Text += $"      Papers: {string.Join(", ", Session.CarriedPapers)}";

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
            card.Status.Text = UnitStatus.Describe(unit);
            card.Condition.Text = UnitStatus.Condition(unit);
            card.Policy.Text = UnitStatus.CardLine(unit);
            card.Policy.TooltipText = $"{UnitStatus.AmmoText(unit)} (rounds + spare magazines) · {unit.Grenades} grenades · {UnitStatus.PolicyName(unit.FirePolicy)}";
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

        column.AddChild(new TextureRect
        {
            Texture = new AtlasTexture { Atlas = Art.Portraits(unit.Side), Region = new Rect2(UnitNames.PortraitIndex(index) * 64, 0, 64, 64) },
            CustomMinimumSize = new Vector2(96, 96),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var name = new Label { Text = UnitNames.Of(unit, index) + (unit.IsTough ? " ★" : ""), TooltipText = unit.IsTough ? "Tough: holds his ground under fire" : "", MouseFilter = Control.MouseFilterEnum.Ignore };
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
        _cards.Add(new Card(id, UnitNames.Of(unit, index), panel, status, condition, policy, morale, suppression, style));
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

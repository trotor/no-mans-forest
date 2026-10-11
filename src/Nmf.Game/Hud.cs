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
    /// <summary>The F1 help: the one key list (<see cref="Nmf.Client.ControlsHelp"/>), keys in a column.</summary>
    internal static string HelpTextFor(string language)
    {
        var lines = Nmf.Client.ControlsHelp.Lines(language);
        int width = lines.Max(l => l.Keys.Length) + 3;
        return string.Join("\n", lines.Select(l => l.Keys.PadRight(width) + l.What));
    }

    private static readonly Color PanelColor = new(0.11f, 0.12f, 0.09f, 0.9f);
    private static readonly Color BorderColor = new(0.45f, 0.43f, 0.3f);
    private static readonly Color SelectedBorder = new(1f, 0.85f, 0.3f);

    private readonly List<Card> _cards = [];

    private sealed record Card(UnitId Id, string Name, bool Tough, PanelContainer Panel, TextureRect Portrait, Label NameLabel, Label Status, Label Condition, Label Policy, ProgressBar Morale, ProgressBar Suppression, StyleBoxFlat Style, Label Badge, StyleBoxFlat BadgeStyle);

    private static readonly Color MoraleGreen = new(0.35f, 0.7f, 0.3f);
    private const int SmallCardTextPx = 92;

    private static Color ToneColour(BadgeTone tone) => tone switch
    {
        BadgeTone.Warning => new Color(0.93f, 0.78f, 0.2f),
        BadgeTone.Bad => new Color(0.95f, 0.5f, 0.1f),
        BadgeTone.Critical => new Color(0.85f, 0.15f, 0.1f),
        BadgeTone.Gone => new Color(0.42f, 0.42f, 0.42f),
        _ => MoraleGreen,
    };

    /// <summary>The card size the player picked; kept over a restart of the mission.</summary>
    internal static CardSize CardSizeChoice { get; set; } = CardSize.Small; // small by default (user, 2026-10-09); K cycles
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
    private PanelContainer _watch = null!;
    private Label _watchText = null!;
    private Button _followButton = null!;
    /// <summary>The follow toggle (L) was pressed.</summary>
    public Action? FollowPressed { get; set; }
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
    private Label _cursorHint = null!;

    /// <summary>The short note by the cursor: what a click (and a double click) would do here.</summary>
    public void ShowCursorHint(string? text, Vector2 mouse)
    {
        _cursorHint.Visible = text is not null && !_tip.Visible;
        if (text is null)
            return;
        _cursorHint.Text = text;
        _cursorHint.Position = mouse + new Vector2(18, 20);
    }

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

    public Action? SettingsPressed { get; set; }

    public Action? RetryPressed { get; set; }
    /// <summary>Set for a campaign mission: the end screen goes back to the campaign instead of trying again or the menu.</summary>
    public Action? CampaignPressed { get; set; }
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

    private Label _endReport = null!;

    /// <summary>A campaign mission's end: what each man earned and where he stands (spec 2026-10-11-campaign-design §9a).</summary>
    public void ShowCampaignReport(string heading, System.Collections.Generic.IReadOnlyList<string> lines)
    {
        _endReport.Text = heading + "\n" + string.Join("\n", lines);
        _endReport.Visible = true;
    }

    /// <summary>Objective and mission-end messages.</summary>
    /// <param name="result">How the mission went, when it ended in these events (computed once, by the caller).</param>
    public void OnEvents(System.Collections.Generic.IEnumerable<Nmf.Sim.Events.SimEvent> events, Nmf.Client.Mission.MissionResult? result = null)
    {
        foreach (var e in events)
        {
            if (e is Nmf.Sim.Events.CounterattackStarted shout && shout.Side != Session.PlayerSide
                && Nmf.Client.Mission.Alerts.Counterattack(shout, Session.OwnUnits.Where(u => !u.IsOutOfAction).Select(u => u.Position), Session.Language) is { } heard)
                ShowToast(heard);
            // Our planned attacks: the assault, a halt and why, the position taken.
            else if (e is Nmf.Sim.Events.PlannedAttackPhaseChanged phase && phase.Side == Session.PlayerSide
                     && Nmf.Client.Mission.Alerts.Phase(phase, Session.Language) is { } going)
                ShowToast(going);
            else if (e is Nmf.Sim.Events.PlannedAttackHalted halted && halted.Side == Session.PlayerSide)
                ShowToast(Nmf.Client.Mission.Alerts.Halted(halted, Session.Language));
            else if (e is Nmf.Sim.Events.PlannedAttackEnded ended && ended.Side == Session.PlayerSide
                     && Nmf.Client.Mission.Alerts.Ended(ended, Session.Language) is { } over)
                ShowToast(over);
            else if (Nmf.Client.Mission.Alerts.Mortar(e, Session.Language, Session.PlayerSide) is { } mortar)
                ShowToast(mortar, seconds: 5);
            else if (Nmf.Client.Mission.Alerts.Alarm(e, Session.OwnUnits.Where(u => !u.IsOutOfAction && !u.Stranded).Select(u => u.Position),
                         Session.PlayerSide, Session.Language) is { } alarm)
                ShowToast(alarm, seconds: 5);
        }
        if (Session.Tracker is not { } tracker)
            return;
        bool fi = Session.Language == "fi";
        foreach (var e in events)
        {
            if (e is Nmf.Sim.Events.MissionMessage message)
                ShowToast(message.Text.In(Session.Language), seconds: 8);
            else if (e is Nmf.Sim.Events.StrandedFound found)
                ShowToast(Nmf.Client.Mission.Alerts.Found(found, Session));
            else if (e is Nmf.Sim.Events.ObjectiveRevealed revealed && Nmf.Client.Mission.Alerts.Revealed(revealed, Session) is { } newObjective)
                ShowToast(newObjective, seconds: 6);
            else if (e is Nmf.Sim.Events.ObjectiveChanged changed && tracker.IsVisible(changed.Id)
                     && tracker.Spec.Objectives.FirstOrDefault(o => o.Id == changed.Id) is { } objective)
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

    private PanelContainer _queuePrompt = null!;
    private Label _queueText = null!;
    private Button _queueGo = null!, _queueStop = null!;
    private Action? _onQueueGo, _onQueueStop;

    /// <summary>A queue stopped to ask (spec 2026-10-10-controls-design §4): go on with it, or call it off.</summary>
    public void AskAboutQueue(string text, Action goOn, Action callOff)
    {
        _queueText.Text = text;
        _onQueueGo = goOn;
        _onQueueStop = callOff;
        _queuePrompt.Visible = true;
    }

    public void HideQueuePrompt() => _queuePrompt.Visible = false;

    private PanelContainer _planPanel = null!;
    private VBoxContainer _planColumn = null!;
    public Action<int>? PlanSelected { get; set; }
    public Action? PlanStart { get; set; }
    public Action? PlanReset { get; set; }
    public Action? PlanCancel { get; set; }

    /// <summary>The attack preview's panel (spec 2026-10-10-plan-preview-design §2); null hides it.</summary>
    public void ShowPlanPanel(AttackPreview? preview)
    {
        _planPanel.Visible = preview is not null;
        foreach (var child in _planColumn.GetChildren())
            child.QueueFree();
        if (preview is null)
            return;
        string lang = Session.Language;
        bool fi = lang == "fi";
        var title = new Label { Text = PlanText.Title(preview, Session, lang), HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        _planColumn.AddChild(title);
        var tactics = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        tactics.AddThemeConstantOverride("separation", 8);
        for (int i = 0; i < preview.Options.Count; i++)
        {
            var option = preview.Options[i];
            int index = i;
            var button = new Button
            {
                Text = PlanText.Button(option, i, lang), ToggleMode = true, ButtonPressed = i == preview.Selected,
                Disabled = !option.Possible, FocusMode = Control.FocusModeEnum.None,
                TooltipText = PlanText.WhyNot(option, lang) ?? "",
            };
            button.Pressed += () => PlanSelected?.Invoke(index);
            tactics.AddChild(button);
        }
        _planColumn.AddChild(tactics);
        var reason = new Label { Text = PlanText.Reason(preview.Option, lang), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(620, 0) };
        _planColumn.AddChild(reason);
        foreach (var warning in PlanText.Warnings(preview, Session, lang))
        {
            var line = new Label { Text = "⚠ " + warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(620, 0) };
            line.AddThemeColorOverride("font_color", new Color(1f, 0.5f, 0.4f));
            _planColumn.AddChild(line);
        }
        if (preview.Plan is not null)
        {
            var help = new Label { Text = fi ? "Raahaa ympyröitä tai reittiä; oikea klikkaus poistaa reitin pisteen." : "Drag the circles or the way; right click removes a point of the way." };
            help.AddThemeFontSizeOverride("font_size", 13);
            _planColumn.AddChild(help);
        }
        _planColumn.AddChild(ButtonRow(
            (fi ? "Aloita (Enter)" : "Start (Enter)", () => PlanStart?.Invoke()),
            (fi ? "Palauta ehdotus (R)" : "Back to the suggestion (R)", () => PlanReset?.Invoke()),
            (fi ? "Peru (Esc)" : "Cancel (Esc)", () => PlanCancel?.Invoke())));
    }

    public void ShowToast(string text, double seconds = 4)
    {
        _toast.Text = text;
        _toast.Visible = true;
        _toastLeft = seconds;
    }

    public void Tick(double delta)
    {
        if (_toast.Visible && (_toastLeft -= delta) <= 0)
            _toast.Visible = false;
        if (Session.Tracker is { } tracker)
            _objectives.Text = (Session.Language == "fi" ? "Tavoitteet " : "Objectives ") + $"{tracker.DoneCount}/{tracker.VisibleCount}";
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
        var settingsButton = new Button { Text = Session.Language == "fi" ? "Asetukset" : "Settings", FocusMode = Control.FocusModeEnum.None };
        settingsButton.Pressed += () => SettingsPressed?.Invoke();
        bar.AddChild(settingsButton);
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

        // The mission's own watch: the day and the time of day, under the right end of the top bar.
        _watch = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _watch.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.1f, 0.08f, 0.78f), BorderColor = new Color(0.45f, 0.43f, 0.3f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 3, ContentMarginBottom = 3,
        });
        _watchText = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _watchText.AddThemeFontSizeOverride("font_size", 16);
        _watch.AddChild(_watchText);
        _watch.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _watch.GrowHorizontal = Control.GrowDirection.Begin;
        _watch.OffsetTop = TopBarHeight + 8;
        _watch.OffsetRight = -12;
        root.AddChild(_watch);

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

        _cursorHint = new Label { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _cursorHint.AddThemeFontSizeOverride("font_size", 15);
        _cursorHint.AddThemeColorOverride("font_outline_color", Colors.Black);
        _cursorHint.AddThemeConstantOverride("outline_size", 6);
        root.AddChild(_cursorHint);

        _queuePrompt = new PanelContainer { Visible = false };
        _queuePrompt.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _queuePrompt.GrowHorizontal = Control.GrowDirection.Both;
        _queuePrompt.OffsetTop = 120;
        var promptRow = new HBoxContainer();
        promptRow.AddThemeConstantOverride("separation", 14);
        _queueText = new Label();
        _queueGo = new Button { Text = Session.Language == "fi" ? "Jatka jonoa" : "Go on with the queue", FocusMode = Control.FocusModeEnum.None };
        _queueStop = new Button { Text = Session.Language == "fi" ? "Peru jono" : "Call the queue off", FocusMode = Control.FocusModeEnum.None };
        _queueGo.Pressed += () => { _queuePrompt.Visible = false; _onQueueGo?.Invoke(); };
        _queueStop.Pressed += () => { _queuePrompt.Visible = false; _onQueueStop?.Invoke(); };
        promptRow.AddChild(_queueText);
        promptRow.AddChild(_queueGo);
        promptRow.AddChild(_queueStop);
        _queuePrompt.AddChild(promptRow);
        root.AddChild(_queuePrompt);

        _planPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _planPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _planPanel.GrowHorizontal = Control.GrowDirection.Both;
        _planPanel.OffsetTop = 52;
        _planColumn = new VBoxContainer();
        _planColumn.AddThemeConstantOverride("separation", 8);
        _planPanel.AddChild(_planColumn);
        root.AddChild(_planPanel);

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
        _endReport = new Label { Visible = false };
        _endReport.AddThemeFontSizeOverride("font_size", 16);
        endColumn.AddChild(_endReport);
        endColumn.AddChild(CampaignPressed is { } toCampaign
            ? ButtonRow(
                (fiUi ? "Jatka katselua" : "Keep watching", () => _end.Visible = false),
                (fiUi ? "Takaisin kampanjaan" : "Back to the campaign", toCampaign))
            : ButtonRow(
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
                     (fiUi ? "Asetukset" : "Settings", () => SettingsPressed?.Invoke()),
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
        _cardSizeButton = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None };
        _cardSizeButton.AddThemeFontSizeOverride("font_size", 12);
        _cardSizeButton.Pressed += CycleCardSize;
        _followButton = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, ToggleMode = true };
        _followButton.AddThemeFontSizeOverride("font_size", 12);
        _followButton.Pressed += () => FollowPressed?.Invoke();
        var cardTools = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        cardTools.AddChild(_followButton);
        cardTools.AddChild(_cardSizeButton);
        cardColumn.AddChild(cardTools);
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
        var helpLabel = new Label { Text = HelpTextFor(Session.Language) + credits };
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
        // The mission's watch and, under it, the mortar on call (the status bar has no room left for it).
        var watchLines = new[] { MissionClock.Text(Session.Mission?.Start, t, Session.Language), MortarText.Status(Session, Session.Language) }
            .Where(line => line is not null).ToList();
        _watch.Visible = watchLines.Count > 0;
        _watchText.Text = string.Join("\n", watchLines);
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
            header.Disabled = !Session.CardUnits.Any(u => u.Squad == squad && !u.IsOutOfAction); // nobody left to select
            header.Visible = Session.CardUnits.Any(u => u.Squad == squad); // a squad of one man still to be found shows when he is
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
            // In an attack under way his part and its phase say more than how he moves.
            card.Status.Text = PlanPhaseText.For(Session.Sim, Session.PlayerSide, unit.Id, lang)
                               ?? CarryText.For(Session.Sim, unit, lang) ?? WinterText.Moving(Session.Sim, unit, lang) ?? UnitStatus.Describe(unit, lang);
            if (WinterText.Status(Session.Sim, unit, lang) is { } kit)
                card.Status.Text += " · " + kit; // on skis, with an ahkio
            card.Condition.Text = UnitStatus.Condition(unit, lang);
            card.Policy.Text = UnitStatus.CardLine(unit, lang);
            card.Policy.TooltipText = lang == "fi"
                ? $"{UnitStatus.AmmoText(unit, lang)} (lippaassa + varalippaat) · {unit.Grenades} kranaattia · {UnitStatus.PolicyName(unit.FirePolicy, lang)}"
                : $"{UnitStatus.AmmoText(unit)} (rounds + spare magazines) · {unit.Grenades} grenades · {UnitStatus.PolicyName(unit.FirePolicy)}";
            card.Panel.TooltipText = CardSizeChoice == CardSize.Large ? ""
                : CardSizes.Tooltip(card.Name + (card.Tough ? " ★" : ""), card.Status.Text, card.Condition.Text, card.Policy.TooltipText);
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
            card.Panel.Visible = !unit.Stranded; // not on the cards until he is found (spec 2026-10-10-events-design §6)
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
        _cards.Add(new Card(id, UnitNames.Of(unit, index), unit.IsTough, panel, portrait, name, status, condition, policy, morale, suppression, style, badge, badgeStyle));
        return panel;
    }

    /// <summary>Shows whether the camera follows the men (the L key or the button above the cards).</summary>
    public void ShowFollow(bool on)
    {
        _followButton.SetPressedNoSignal(on);
        _followButton.Text = FollowLeash.ButtonText(on, Session.Language);
        _bottom.ResetSize(); // "päällä" is longer than "pois"
        _bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.KeepSize);
    }

    /// <summary>Large cards, small cards, icons, and round again (the K key or the button above the cards).</summary>
    /// <summary>The settings window's card size, applied at once.</summary>
    public void SetCardSize(CardSize size)
    {
        CardSizeChoice = size;
        ApplyCardSize();
    }

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
            // Small cards keep one width, the longer names and doings cut short, so the bar does not jump as they change.
            bool fixedWidth = CardSizeChoice != CardSize.Large;
            // "Alik. Korpela" is "Korpela" on a small card; the tooltip keeps the rank.
            card.NameLabel.Text = (fixedWidth ? card.Name.Split(' ')[^1] : card.Name) + (card.Tough ? " ★" : "");
            foreach (var label in new[] { card.NameLabel, card.Status })
            {
                label.ClipText = fixedWidth;
                label.TextOverrunBehavior = fixedWidth ? TextServer.OverrunBehavior.TrimEllipsis : TextServer.OverrunBehavior.NoTrimming;
                label.CustomMinimumSize = new Vector2(fixedWidth ? SmallCardTextPx : 0, 0);
            }
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

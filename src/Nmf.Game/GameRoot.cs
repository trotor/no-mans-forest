using System;
using System.IO;
using System.Linq;
using Godot;
using Nmf.Client;
using Nmf.Client.Mission;
using Nmf.Client.Art;
using Nmf.Client.Effects;
using Nmf.Content;
using Nmf.Sim.Mission;
using Nmf.Content.Missions;
using Nmf.Content.Weapons;
using Nmf.Content.Tiled;
using Nmf.Sim.Core;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;
using GridMap = Nmf.Sim.World.GridMap;

namespace Nmf.Game;

/// <summary>Scene root: loads the skirmish, builds the views and turns input into session calls.</summary>
public partial class GameRoot : Node2D
{
    private const int ClickRadiusCm = 150;
    private int _screenshotFrame = 90;
    /// <summary>Game seconds to shoot at instead of a frame: the same moment of the fight whatever the frame rate.</summary>
    private double? _screenshotAtSeconds;

    private GameSession? _session;
    private ArtLibrary _art = null!;
    private DecorationView _decorations = null!;
    private UnitView _units = null!;
    private FogView _fog = null!;
    private OrdersView _orders = null!;
    private MapView _mapView = null!;
    private OverlayPause? _overlayPause;
    private Hud _hud = null!;
    private CameraController _camera = null!;
    private Vector2? _dragStart;
    private bool _ignoreNextRelease;
    private string? _screenshotPath;
    private int _frame;

    public override void _Ready()
    {
        if (!_windowSetUp) // once: a scene reload (a mission started, tried again) keeps the window as the player left it
        {
            SetupWindow();
            _windowSetUp = true;
        }
        // Space past the map edges (visible when scrolling the south edge above the cards) matches the HUD panels.
        RenderingServer.SetDefaultClearColor(new Color(0.09f, 0.1f, 0.07f));
        string? contentRoot = ContentLocator.FindContentRoot(ProjectSettings.GlobalizePath("res://"))
                              ?? ContentLocator.FindContentRoot(OS.GetExecutablePath().GetBaseDir());
        if (contentRoot is null)
        {
            GD.PushError("[NMF] content/ directory not found");
            GetTree().Quit(1);
            return;
        }

        _contentRoot = contentRoot;
        if (ShowsMenu())
        {
            ShowMenu(contentRoot);
            return;
        }

        MissionSpec? mission = null;
        GridMap map;
        try
        {
            if (MissionId() is { } missionId)
                mission = MissionLoader.Load(Path.Combine(contentRoot, "core", "missions", missionId));
            map = TmxMapLoader.Load(Path.Combine(contentRoot, "core", "maps", (mission?.Map ?? MapName()) + ".tmx"));
        }
        catch (Exception ex) when (ex is MapLoadException or ContentLoadException)
        {
            Fail(ex.Message);
            return;
        }

        System.Collections.Generic.IReadOnlyDictionary<string, Nmf.Sim.Combat.WeaponDef> weapons;
        System.Collections.Generic.IReadOnlyDictionary<string, Nmf.Sim.Combat.GrenadeDef> grenades;
        try
        {
            _art = ArtLibrary.Load(contentRoot);
            weapons = WeaponLoader.LoadDirectory(Path.Combine(contentRoot, "core", "weapons"));
            grenades = GrenadeLoader.LoadDirectory(Path.Combine(contentRoot, "core", "grenades"));
        }
        catch (Exception ex) when (ex is IOException or FormatException or ContentLoadException)
        {
            Fail(ex.Message);
            return;
        }

        GameSession session;
        try
        {
            session = mission is null
                ? new GameSession(SkirmishScenario.Create(map, seed: 1942, weapons, grenades))
                : new GameSession(MissionScenario.Create(map, mission, weapons, grenades, seed: 1942), mission, Language());
        }
        catch (ArgumentException ex)
        {
            Fail(ex.Message);
            return;
        }
        _session = session;
        _weapons = weapons;

        // World draw order: ground, rocks and bushes, soldiers, tree canopies, fog.
        var relief = Relief.Of(map);
        AddChild(GroundView.Create(map, _art, relief));
        _decorations = new DecorationView();
        _decorations.Build(map, _art, relief);
        AddChild(_decorations.LowLayer);
        _units = new UnitView { Session = session, Art = _art, Animator = new UnitAnimator(), Effects = new CombatEffects() };
        AddChild(_units);
        AddChild(_decorations.CanopyLayer);
        _fog = new FogView { Session = session };
        AddChild(_fog);
        _camera = new CameraController { WorldSize = new Vector2(map.Width, map.Height) * Coords.PixelsPerCell, BottomOverscroll = CardSizes.Layout(Hud.CardSizeChoice).BarHeightPx };
        AddChild(_camera);
        _camera.MakeCurrent();
        _camera.Zoom = new Vector2(0.7f, 0.7f); // a Close Combat-like overview to start with
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--cards=", StringComparison.Ordinal) && Enum.TryParse<CardSize>(arg["--cards=".Length..], ignoreCase: true, out var cardSize))
                Hud.CardSizeChoice = cardSize; // --cards=large|small|icon
            else if (arg == "--outlines=strong")
                UnitView.OutlineChoice = OutlineStrength.Strong;
            else if (arg == "--follow")
                FollowOn = true;
        _hud = new Hud
        {
            Session = session,
            Art = _art,
            CardClicked = (id, additive) =>
            {
                if (!additive)
                    session.Selection.Clear();
                var unit = session.Sim.FindUnit(id);
                if (unit is { IsOutOfAction: false })
                    session.Selection.SelectAt([unit], session.PlayerSide, unit.Position, 1, additive: true);
            },
            CardDoubleClicked = id =>
            {
                if (session.Sim.FindUnit(id) is { } unit)
                    _camera.CenterOn(Coords.ToPixels(unit.Position));
            },
            CardBarHeightChanged = height => _camera.BottomOverscroll = height,
        };
        AddChild(_hud);

        var firstOwn = session.OwnUnits.FirstOrDefault();
        if (firstOwn is not null)
            _camera.CenterOn(Coords.ToPixels(firstOwn.Position) + new Vector2(0, -200));

        _orders = new OrdersView { Session = session };
        _hud.AddOverlay(_orders);
        _mapView = new MapView
        {
            Session = session,
            ViewRect = () =>
            {
                var size = GetViewportRect().Size / _camera.Zoom;
                return new Rect2(_camera.Position - size / 2, size);
            },
            LookAt = point =>
            {
                if (FollowOn)
                {
                    FollowOn = false; // looking elsewhere on the map lets go of the men
                    _hud.ShowFollow(false);
                    _hud.ShowToast(FollowLeash.ButtonText(false, session.Language));
                }
                _camera.CenterOn(point);
            },
            Signals = () => _units.Effects.Signals,
        };
        _hud.AddOverlay(_mapView);
        // Any open paper stops the war; the game resumes as it was when the last one closes.
        var pause = new OverlayPause(session.Clock);
        _overlayPause = pause;
        _orders.VisibilityChanged += () => { if (_orders.Visible) pause.Opened("orders"); else pause.Closed("orders"); };
        _mapView.VisibilityChanged += () => { if (_mapView.Visible) pause.Opened("map"); else pause.Closed("map"); };
        _hud.OrdersPressed = () => { _hud.HideMenu(); _mapView.Visible = false; _orders.Toggle(); };
        _hud.MapPressed = () => { _hud.HideMenu(); _orders.Close(); _mapView.Toggle(); };
        // While a paper is open the war stays stopped; the buttons then choose how it resumes when the paper closes.
        _hud.PausePressed = pause.TogglePause;
        _hud.SpeedPressed = speed =>
        {
            session.SetSpeed(speed);
            pause.Resume();
        };
        _hud.PausedAfter = () => pause.PausedAfter;
        _hud.RetryPressed = Retry;
        _hud.MenuPressed = () => Relaunch(null);
        _hud.QuitPressed = () => GetTree().Quit();
        _hud.MenuOpening = () => { _orders.Close(); _mapView.Visible = false; };
        _hud.GameMenu.VisibilityChanged += () => { if (_hud.GameMenu.Visible) pause.Opened("menu"); else pause.Closed("menu"); };
        _hud.SquadPressed = squad => session.SelectSquad(squad);
        _hud.FollowPressed = () => ToggleFollow(session);
        _hud.ShowFollow(FollowOn);

        var args = Args();
        bool automated = args.Any(a => a.StartsWith("--demo", StringComparison.Ordinal) || a.StartsWith("--screenshot", StringComparison.Ordinal));
        _automated = automated;
        _mapView.Visible = args.Contains("--open=map");
        if (mission?.Debug == true)
        {
            _units.RevealAll = true; // a testbed is shown whole
            _fog.Visible = false;
        }
        if ((mission is not null && !automated) || args.Contains("--open=orders"))
            _orders.Open(); // every mission starts with its orders, the game paused while they are read

        ParseScreenshotArgs(args);
        foreach (var arg in args)
        {
            if (arg == "--demo")
                StartDemo(session);
            else if (arg == "--demo=attack")
                StartAttackDemo(session);
            else if (arg == "--reveal")
            {
                _units.RevealAll = true;
                _fog.Visible = false;
            }
            else if (arg.StartsWith("--look=", StringComparison.Ordinal) && arg["--look=".Length..].Split(',') is [var lx, var ly]
                     && int.TryParse(lx, out int lookX) && int.TryParse(ly, out int lookY))
                _camera.CenterOn(Coords.ToPixels(new CellCoord(lookX, lookY).CenterCm)); // metres from the top left
            else if (arg.StartsWith("--zoom=", StringComparison.Ordinal)
                     && float.TryParse(arg["--zoom=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float zoom) && zoom > 0)
            {
                _camera.Zoom = new Vector2(zoom, zoom);
                _camera.CenterOn(_camera.Position); // re-clamp: zoomed far out, a big map is centred in the view
            }
        }

        GD.Print($"[NMF] ready map={map.Width}x{map.Height} units={session.Sim.Units.Count} content={contentRoot}");
    }

    public override void _Process(double delta)
    {
        if (_session is null)
        {
            TakeScreenshotWhenDue(); // the menu
            return;
        }
        int steps = _session.Update(delta);
        if (_demoFollow || _demoAttackPending)
            UpdateAttackDemo(_session);
        if (FollowOn && !_demoFollow && CommandedBox(_session) is { } men)
            _camera.Follow(men, Hud.TopBarHeight, delta);
        var events = _session.TakeEvents();
        _units.Effects.Add(events, id => _session.Sim.FindUnit(id) is { } shooter && _session.IsShownToPlayer(shooter, _units.RevealAll),
            looted => LootText.Describe(looted, id => _weapons.TryGetValue(id, out var w) ? w.Name : id, _session.Language,
                item => _session.Mission?.Items.TryGetValue(item.Id, out var name) == true ? name!.In(_session.Language) : item.Name));
        _units.Effects.AddSignals(events, id => _session.Sim.FindUnit(id) is { } who
            ? new Nmf.Client.Effects.SignalUnit(who.Position, who.Side == _session.PlayerSide, _session.IsShownToPlayer(who, _units.RevealAll))
            : null, _session.OwnUnits.Where(u => !u.IsOutOfAction).Select(u => u.Position).ToList());
        _units.Effects.UpdateSignals(steps / (double)Nmf.Sim.SimConstants.TicksPerSecond);
        // A mission over: what came of it is shown, and kept for the menu (not for demos and screenshots).
        var result = _session.Mission is not null && events.Any(e => e is Nmf.Sim.Events.MissionEnded) ? MissionResult.From(_session) : null;
        _hud.OnEvents(events, result);
        if (!_automated && result is not null && _session.Mission?.Debug != true) // a testbed leaves no record
        {
            var progress = LoadProgress();
            progress.Record(_session.Mission!.Id, result);
            SaveProgress(progress);
        }
        _hud.Tick(delta);
        _units.Effects.Update(delta);
        var ownPixels = new System.Collections.Generic.List<Vector2>();
        foreach (var unit in _session.Sim.Units)
        {
            if (!_session.IsShownToPlayer(unit, _units.RevealAll))
                continue;
            var (x, y) = _session.InterpolatedPositionCm(unit);
            ownPixels.Add(Coords.ToPixels(x, y));
        }
        var view = GetViewportRect().Size;
        _camera.MinZoom = ViewScale.MinZoomToFit(view.X, view.Y, _camera.WorldSize.X, _camera.WorldSize.Y);
        bool decorations = ViewScale.ShowsDecorations(_camera.Zoom.X);
        _decorations.LowLayer.Visible = decorations;
        _decorations.CanopyLayer.Visible = decorations;
        if (decorations)
            _decorations.UpdateCanopyFade(ownPixels, (float)delta);
        _units.DragRect = _dragStart is { } start ? new Rect2(start, GetGlobalMousePosition() - start).Abs() : null;
        _units.Animate();
        _units.HoverCm = Coords.ToCm(GetGlobalMousePosition());
        UpdateHoverTip(delta);
        UpdateGuide(_session, events, delta);
        _units.Zoom = _camera.Zoom.X;
        _units.PickRadiusCm = PickRadiusCm();
        _units.QueueRedraw();
        _fog.Refresh();
        _hud.Refresh();

        TakeScreenshotWhenDue();
    }

    private void TakeScreenshotWhenDue()
    {
        ++_frame;
        bool due = _screenshotAtSeconds is { } at ? _session is not null && _session.GameTime.TotalSeconds >= at : _frame == _screenshotFrame;
        if (_screenshotPath is not null && due)
        {
            // Draw this very frame first: a window hidden behind others is not redrawn, and its last picture may be
            // minutes old.
            RenderingServer.ForceDraw(swapBuffers: false);
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
            GD.Print($"[NMF] screenshot saved to {_screenshotPath} at {_session?.GameTime.TotalSeconds:F1} s");
            _screenshotPath = null;
            GetTree().Quit();
        }
    }

    private readonly GuideFade _guideFade = new();
    private int _ordersSeen;
    private const int GuideNearCm = 6000;

    /// <summary>
    /// The arrow to the next objective, with its name and how far it is from the platoon — out of the way while the
    /// men move or fight or once they are near it, back when all has been quiet a while.
    /// </summary>
    private void UpdateGuide(GameSession session, System.Collections.Generic.IReadOnlyList<Nmf.Sim.Events.SimEvent> events, double delta)
    {
        var target = _overlayPause?.AnyOpen == true ? null : ObjectiveGuide.Next(session);
        var log = session.Sim.OrderLog;
        bool ordered = false;
        for (int i = _ordersSeen; i < log.Count; i++)
            ordered |= log[i].Issuer == session.PlayerSide;
        _ordersSeen = log.Count;
        var own = session.OwnUnits.Where(u => !u.IsOutOfAction).ToList();
        bool activity = ordered || own.Any(u => u.MoveTarget is not null)
                        || events.Any(e => e is Nmf.Sim.Events.ShotFired or Nmf.Sim.Events.GrenadeExploded or Nmf.Sim.Events.UnitWounded);
        bool near = target is { } t0 && own.Any(u => t0.Zone is { } z
            ? u.Position.X >= z.Min.X - GuideNearCm / 2 && u.Position.X <= z.Max.X + GuideNearCm / 2 && u.Position.Y >= z.Min.Y - GuideNearCm / 2 && u.Position.Y <= z.Max.Y + GuideNearCm / 2
            : (u.Position - t0.At).LengthSquared <= (long)GuideNearCm * GuideNearCm);
        _guideFade.Update(delta, activity, near);
        _units.GuideAlpha = (float)_guideFade.Alpha;
        _hud.GuideAlpha = (float)_guideFade.Alpha;
        _units.GuideZone = target?.Zone;
        _units.GuideLabel = target?.Label ?? "";
        if (target is not { } t)
        {
            _hud.ShowGuide(null, "");
            return;
        }
        var men = session.OwnUnits.Where(u => !u.IsOutOfAction).ToList();
        string distance = "";
        if (men.Count > 0)
        {
            var centre = new Vec2((int)men.Average(u => u.Position.X), (int)men.Average(u => u.Position.Y));
            distance = $"  {(t.At - centre).Length / 100} m";
        }
        var screen = GetViewport().GetCanvasTransform() * Coords.ToPixels(t.At);
        _hud.ShowGuide(new Vector2(Mathf.Round(screen.X), Mathf.Round(screen.Y)), t.Label + distance);
    }

    private const double HoverDelaySeconds = 0.35;
    private UnitId? _hoverId;
    private double _hoverSeconds;
    private bool _mouseInWindow = true;
    private bool _focused = true;

    /// <summary>How far from a man a click or the pointer still picks him: far out a man is a dot, so the reach grows.</summary>
    private int PickRadiusCm() => Math.Max(GameSession.ClickRadiusCm, (int)(14f / _camera.Zoom.X / Coords.PixelsPerCm));

    public override void _Notification(int what)
    {
        if (what == (int)NotificationWMMouseExit)
            _mouseInWindow = false;
        else if (what == (int)NotificationWMMouseEnter)
            _mouseInWindow = true;
        else if (what == (int)NotificationApplicationFocusOut)
            _focused = false;
        else if (what == (int)NotificationApplicationFocusIn)
            _focused = true;
    }

    /// <summary>Held on a seen enemy (alive or fallen) for a moment, the cursor shows what our men can tell about him.</summary>
    private void UpdateHoverTip(double delta)
    {
        Nmf.Sim.Units.Unit? under = null;
        bool buttonHeld = Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Middle)
                          || Input.IsMouseButtonPressed(MouseButton.Right);
        bool pointing = _mouseInWindow && _focused && !buttonHeld && _overlayPause?.AnyOpen != true && !_hud.HelpVisible
                        && _dragStart is null && GetViewport().GuiGetHoveredControl() is null;
        _units.HoverActive = pointing;
        if (pointing)
        {
            var point = Coords.ToCm(GetGlobalMousePosition());
            int radius = PickRadiusCm();
            under = _session!.InspectAt(point, radius);
            // Stay with the man already pointed at while he is still in reach, so two men close together don't flicker.
        }
        if (under?.Id != _hoverId)
        {
            // Between two men close together the tip follows the nearer (the one the ring marks and a click picks)
            // without starting its delay over, so it does not flicker.
            bool stillPointing = under is not null && _hoverId is { } held && _session!.Sim.FindUnit(held) is { } previous
                                 && _session.IsShownToPlayer(previous, false)
                                 && (previous.Position - Coords.ToCm(GetGlobalMousePosition())).LengthSquared <= (long)PickRadiusCm() * PickRadiusCm();
            _hoverId = under?.Id;
            if (!stillPointing)
                _hoverSeconds = 0;
        }
        else
        {
            _hoverSeconds += delta;
        }
        string? text = under is not null && _hoverSeconds >= HoverDelaySeconds
            ? string.Join("\n", EnemyInfo.Describe(under, _session!.OwnUnits, _session.Language, _session.Fog.IsVisible(under.Position)))
            : null;
        _hud.ShowTip(text, GetViewport().GetMousePosition());
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_session is null)
            return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                HandleLeftClick(_session, click);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                _session.HandleRightClick();
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                // Digits and the Z/X/C row by where the key sits, so other keyboard layouts work too; with Ctrl/Cmd they
                // are the system's (undo, cut, copy), not stance orders.
                var code = key.PhysicalKeycode is (>= Key.Key0 and <= Key.Key9) or Key.Z or Key.X or Key.C ? key.PhysicalKeycode : key.Keycode;
                if ((key.CtrlPressed || key.MetaPressed) && code is Key.Z or Key.X or Key.C)
                    break;
                HandleKey(_session, code);
                break;
        }
    }

    /// <summary>
    /// A click acts on release (so a drag can become a box selection); the second press of a double click acts at once
    /// and upgrades the first click's order (walk → run, one man → the squad).
    /// </summary>
    private void HandleLeftClick(GameSession session, InputEventMouseButton click)
    {
        if (click.Pressed)
        {
            if (click.DoubleClick)
            {
                ShowOutcome(session.HandleLeftClick(Coords.ToCm(GetGlobalMousePosition()), true, click.ShiftPressed, click.AltPressed, PickRadiusCm(),
                    click.CtrlPressed || click.MetaPressed));
                _dragStart = null;
                _ignoreNextRelease = true;
                return;
            }
            _dragStart = GetGlobalMousePosition();
            return;
        }
        if (_ignoreNextRelease)
        {
            _ignoreNextRelease = false;
            return;
        }
        if (_dragStart is not { } start)
            return;
        var end = GetGlobalMousePosition();
        _dragStart = null;
        if (start.DistanceTo(end) < 6f / _camera.Zoom.X)
            ShowOutcome(session.HandleLeftClick(Coords.ToCm(end), false, click.ShiftPressed, click.AltPressed, PickRadiusCm(),
                click.CtrlPressed || click.MetaPressed));
        else
            session.Selection.SelectInBox(session.Sim.Units.Where(u => !u.IsOutOfAction), session.PlayerSide, Coords.ToCm(start), Coords.ToCm(end), click.ShiftPressed);
    }

    private System.Collections.Generic.IReadOnlyDictionary<string, Nmf.Sim.Combat.WeaponDef> _weapons = new System.Collections.Generic.Dictionary<string, Nmf.Sim.Combat.WeaponDef>();

    /// <summary>What to start after a scene reload (a mission chosen in the menu, or tried again); survives the reload.</summary>
    private static (string Mission, string Language)? _launch;
    /// <summary>Back to the menu even if the command line named a mission.</summary>
    private static bool _backToMenu;
    private string _contentRoot = "";

    private static string ProgressPath => ProjectSettings.GlobalizePath("user://progress.json");
    private static bool _windowSetUp;
    /// <summary>Why the last chosen mission could not start, shown in the menu.</summary>
    private static string? _menuError;

    /// <summary>A mission chosen in the menu that cannot start goes back to the menu with the reason; otherwise the game quits.</summary>
    private void Fail(string message)
    {
        GD.PushError($"[NMF] {message}");
        if (_launch is null)
        {
            GetTree().Quit(1);
            return;
        }
        _menuError = message;
        Callable.From(() => Relaunch(null)).CallDeferred();
    }

    /// <summary>The command line; after a relaunch from the menu only the window and language options still apply.</summary>
    private static string[] Args() => _launch is null
        ? OS.GetCmdlineUserArgs()
        : OS.GetCmdlineUserArgs().Where(a => a.StartsWith("--window=", StringComparison.Ordinal) || a.StartsWith("--lang=", StringComparison.Ordinal)).ToArray();

    private void ParseScreenshotArgs(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("--screenshot=", StringComparison.Ordinal))
                _screenshotPath = arg["--screenshot=".Length..];
            else if (arg.StartsWith("--screenshot-frame=", StringComparison.Ordinal) && int.TryParse(arg["--screenshot-frame=".Length..], out int frame) && frame > 0)
                _screenshotFrame = frame;
            else if (arg.StartsWith("--screenshot-at=", StringComparison.Ordinal)
                     && double.TryParse(arg["--screenshot-at=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds) && seconds >= 0)
                _screenshotAtSeconds = seconds;
        }
    }

    private static MissionProgress LoadProgress()
    {
        try
        {
            return File.Exists(ProgressPath) ? MissionProgress.FromJson(File.ReadAllText(ProgressPath)) : new MissionProgress();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"[NMF] progress not read: {ex.Message}");
            return new MissionProgress();
        }
    }

    private static void SaveProgress(MissionProgress progress)
    {
        try
        {
            // Written aside and moved into place, so a crash mid-write cannot lose what was kept before.
            string temporary = ProgressPath + ".tmp";
            File.WriteAllText(temporary, progress.ToJson());
            File.Move(temporary, ProgressPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"[NMF] progress not saved: {ex.Message}");
        }
    }

    /// <summary>The menu opens unless a mission was chosen, or the command line asks for a mission, a map, a demo or a screenshot.</summary>
    private static bool ShowsMenu()
    {
        if (_launch is not null)
            return false;
        if (_backToMenu || OS.GetCmdlineUserArgs().Contains("--menu"))
            return true;
        return !OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--mission=", StringComparison.Ordinal) || a.StartsWith("--map=", StringComparison.Ordinal)
                                                  || a.StartsWith("--demo", StringComparison.Ordinal) || a.StartsWith("--screenshot", StringComparison.Ordinal)
                                                  || a.StartsWith("--open=", StringComparison.Ordinal));
    }

    private void ShowMenu(string contentRoot)
    {
        ParseScreenshotArgs(OS.GetCmdlineUserArgs());
        var menu = new MenuView
        {
            // Testbeds (debug: true) only with --debug.
            Missions = MissionLoader.LoadAll(Path.Combine(contentRoot, "core", "missions"))
                .Where(e => e.Spec?.Debug != true || OS.GetCmdlineUserArgs().Contains("--debug")).ToList(),
            Progress = LoadProgress(),
            Language = Language(),
            MissionChosen = (id, language) => Relaunch((id, language)),
            Error = _menuError,
        };
        _menuError = null;
        AddChild(menu);
        GD.Print("[NMF] mission menu");
    }

    /// <summary>Start a mission afresh (or go to the menu with none) by reloading the scene.</summary>
    private void Relaunch((string Mission, string Language)? launch)
    {
        _launch = launch;
        _backToMenu = launch is null;
        if (launch is { } l)
            _menuLanguage = l.Language;
        GetTree().ReloadCurrentScene();
    }

    /// <summary>The same mission again from the start (a bare map: the same map).</summary>
    private void Retry()
    {
        if (_session?.Mission is { } mission)
            Relaunch((mission.Id, _session.Language));
        else
        {
            _backToMenu = false;
            GetTree().ReloadCurrentScene();
        }
    }

    private bool _automated;

    /// <summary>The mission to play: <c>--mission=id</c>; by default Iskuosasto, unless a bare <c>--map=</c> is asked for.</summary>
    private static string? MissionId()
    {
        if (_launch is { } launch)
            return launch.Mission;
        string? id = "iskuosasto";
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--mission=", StringComparison.Ordinal))
                return arg["--mission=".Length..];
            if (arg.StartsWith("--map=", StringComparison.Ordinal))
                id = null;
        }
        return id;
    }

    /// <summary>Mission texts in <c>--lang=fi</c> or English.</summary>
    private static string Language() =>
        _launch?.Language ?? _menuLanguage ?? (OS.GetCmdlineUserArgs().Any(a => a == "--lang=fi") ? "fi" : "en");

    private static string? _menuLanguage;

    /// <summary>The map to play: <c>--map=name</c> (a file in content/core/maps), by default the 1 km Karhumäki map.</summary>
    private static string MapName()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--map=", StringComparison.Ordinal) && arg.Length > "--map=".Length)
                return Path.GetFileNameWithoutExtension(arg["--map=".Length..]);
        return "karhumaki";
    }

    private void ShowOutcome(ClickOutcome outcome)
    {
        if (outcome.Result is ClickResult.MoveOrdered or ClickResult.LootOrdered)
            _units.Effects.AddMarker(EffectKind.MoveMarker, outcome.Point);
        else if (outcome.Result is ClickResult.FireOrdered or ClickResult.AssaultOrdered or ClickResult.AttackOrdered or ClickResult.AreaFireOrdered)
            _units.Effects.AddMarker(EffectKind.FireMarker, outcome.Point);
        // The men who got the order flash, with a line to where it sends them (easy to follow when zoomed far out).
        var ordered = outcome.Result == ClickResult.LootOrdered && _session!.LastLooter is { } looter ? [looter] : _session!.CommandedIds;
        switch (outcome.Result)
        {
            case ClickResult.MoveOrdered or ClickResult.LootOrdered:
                _units.Effects.AddOrderFlash(ordered, outcome.Point, OrderFlashKind.Move);
                break;
            case ClickResult.FireOrdered or ClickResult.AssaultOrdered or ClickResult.AttackOrdered or ClickResult.AreaFireOrdered:
                _units.Effects.AddOrderFlash(ordered, outcome.Point, OrderFlashKind.Fire);
                break;
        }
    }

    private int _lastSquadKey = -1;
    private ulong _lastSquadKeyMs;

    /// <summary>1, 2, …: select that squad; the same key again at once: look at it.</summary>
    private void SquadKey(GameSession session, int squad)
    {
        if (squad >= session.SquadCount)
            return;
        ulong now = Time.GetTicksMsec();
        bool again = squad == _lastSquadKey && now - _lastSquadKeyMs < 400;
        _lastSquadKey = squad;
        _lastSquadKeyMs = now;
        session.SelectSquad(squad);
        var men = session.OwnUnits.Where(u => u.Squad == squad && !u.IsOutOfAction).ToList();
        if (again && men.Count > 0)
            _camera.CenterOn(Coords.ToPixels(new Vec2((int)men.Average(u => u.Position.X), (int)men.Average(u => u.Position.Y))));
    }

    private void FlashStanceOrder(GameSession session) =>
        _units.Effects.AddOrderFlash(session.CommandedIds, null, OrderFlashKind.Stance);

    private void HandleKey(GameSession session, Key key)
    {
        // Over the orders or the map only the paper keys and the speed work; Space closes the paper and lets the war go on.
        if (_overlayPause?.AnyOpen == true
            && key is not (Key.B or Key.M or Key.Escape or Key.F11 or Key.F1 or Key.Plus or Key.Equal or Key.KpAdd or Key.Minus or Key.KpSubtract))
        {
            if (key == Key.Space)
            {
                _orders.Close();
                _mapView.Visible = false;
            }
            return;
        }
        switch (key)
        {
            case Key.Space:
                session.Clock.Paused = !session.Clock.Paused;
                break;
            case Key.Z:
                session.OrderStance(Stance.Standing);
                FlashStanceOrder(session);
                break;
            case Key.X:
                session.OrderStance(Stance.Crouching);
                FlashStanceOrder(session);
                break;
            case Key.C:
                session.OrderStance(Stance.Prone);
                FlashStanceOrder(session);
                break;
            case Key.Key0 or Key.Kp0:
                session.Selection.Clear(); // the whole platoon
                break;
            case >= Key.Key1 and <= Key.Key9:
                SquadKey(session, (int)(key - Key.Key1));
                break;
            case >= Key.Kp1 and <= Key.Kp9:
                SquadKey(session, (int)(key - Key.Kp1));
                break;
            case Key.P:
                session.CycleFirePolicy();
                break;
            case Key.K:
                _hud.CycleCardSize();
                break;
            case Key.L:
                ToggleFollow(session);
                break;
            case Key.O:
                UnitView.OutlineChoice = UnitOutlines.Next(UnitView.OutlineChoice);
                _hud.ShowToast(UnitOutlines.Toast(UnitView.OutlineChoice, session.Language));
                break;
            case Key.H:
                session.OrderStop();
                FlashStanceOrder(session);
                break;
            case Key.Plus or Key.Equal or Key.KpAdd:
                session.SpeedUp();
                break;
            case Key.Minus or Key.KpSubtract:
                session.SlowDown();
                break;
            case Key.Tab:
                SelectAll(session);
                break;
            case Key.Escape:
                if (_orders.Visible || _mapView.Visible)
                {
                    _orders.Close();
                    _mapView.Visible = false;
                }
                else if (_hud.GameMenu.Visible)
                {
                    _hud.ToggleMenu();
                }
                else if (_hud.EndPanelVisible)
                {
                    _hud.HideEndPanel();
                }
                else if (session.Selection.Count > 0)
                {
                    session.Selection.Clear();
                }
                else
                {
                    _hud.ToggleMenu(); // nothing to clear: the game menu
                }
                break;
            case Key.B:
                _hud.HideMenu();
                _mapView.Visible = false;
                _orders.Toggle();
                break;
            case Key.M:
                _hud.HideMenu();
                _orders.Close();
                _mapView.Toggle();
                break;
            case Key.Enter or Key.KpEnter:
                _hud.HideEndPanel();
                break;
            case Key.F1:
                _hud.ToggleHelp();
                break;
            case Key.F11:
                DisplayServer.WindowSetMode(DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen
                    ? DisplayServer.WindowMode.Windowed
                    : DisplayServer.WindowMode.Fullscreen);
                break;
            case Key.F:
                _units.RevealAll = !_units.RevealAll;
                _fog.Visible = !_units.RevealAll;
                break;
        }
    }

    /// <summary>Opens the window at 90 % of the usable screen, or at the size given with the user arg --window=WxH.</summary>
    private static void SetupWindow()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        int screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var size = new Vector2I((int)(usable.Size.X * 0.9f), (int)(usable.Size.Y * 0.9f));
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            var parts = arg.StartsWith("--window=", StringComparison.Ordinal) ? arg["--window=".Length..].Split('x') : [];
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0)
                size = new Vector2I(w, h);
        }
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
    }

    private static void SelectAll(GameSession session) =>
        session.Selection.SelectInBox(session.Sim.Units, session.PlayerSide, Vec2.Zero,
            new Vec2(session.Sim.Map.WidthCm, session.Sim.Map.HeightCm), additive: false);

    /// <summary>For screenshots: the platoon heads for the enemy post, attacks the first enemy it sees; the camera follows.</summary>
    private bool _demoAttackPending;
    private bool _demoFollow;

    private void StartAttackDemo(GameSession session)
    {
        session.Selection.Clear();
        var post = session.Sim.Units.First(u => u.Side != session.PlayerSide).Position;
        session.OrderMove(post, MoveMode.Auto);
        session.Clock.TimeScale = 8;
        _demoAttackPending = true;
        _demoFollow = !FollowOn; // with follow on (L), the demo shows it instead of its own camera
    }

    private void UpdateAttackDemo(GameSession session)
    {
        var own = session.OwnUnits.Where(u => !u.IsOutOfAction).ToList();
        if (_demoFollow && own.Count > 0)
            _camera.CenterOn(Coords.ToPixels(new Vec2((int)own.Average(u => u.Position.X), (int)own.Average(u => u.Position.Y))));
        if (!_demoAttackPending)
        {
            DemoCarryOn(session);
            return;
        }
        var seen = session.Sim.Units.FirstOrDefault(u => u.Side != session.PlayerSide && !u.IsOutOfAction
                                                         && session.Knowledge.LevelOf(u.Id) == Nmf.Sim.Vision.ContactLevel.Visible);
        if (seen is null)
            return;
        session.Selection.Clear();
        session.OrderAttack(seen.Id);
        _units.Effects.AddOrderFlash(session.CommandedIds, seen.Position, OrderFlashKind.Fire);
        session.Clock.TimeScale = 2;
        _demoAttackPending = false;
    }

    /// <summary>
    /// Once a fight dies down with no enemy in sight: the men go on towards the nearest enemy still standing (the demo
    /// knows where he is) and attack him when they see him; with none left, the nearest man searches a fallen one.
    /// </summary>
    private void DemoCarryOn(GameSession session)
    {
        bool fighting = session.Sim.Units.Any(u => u.Side != session.PlayerSide && !u.IsOutOfAction
                                                   && session.Knowledge.LevelOf(u.Id) == Nmf.Sim.Vision.ContactLevel.Visible);
        var own = session.OwnUnits.Where(u => !u.IsOutOfAction).ToList();
        if (fighting || own.Count == 0 || own.Any(u => u.LootTarget is not null || u.AttackRole != AttackRole.None || u.MoveTarget is not null))
            return;
        var centre = new Vec2((int)own.Average(u => u.Position.X), (int)own.Average(u => u.Position.Y));
        var standing = session.Sim.Units.Where(u => u.Side != session.PlayerSide && !u.IsOutOfAction)
            .OrderBy(u => (u.Position - centre).LengthSquared).FirstOrDefault();
        if (standing is not null)
        {
            session.Selection.Clear();
            session.OrderMove(standing.Position, MoveMode.Auto);
            _demoAttackPending = true;
            return;
        }
        var body = session.Sim.Units.FirstOrDefault(u => u.Side != session.PlayerSide && u.IsOutOfAction && !u.IsCaptured
                                                         && !u.WasSearchedBy(session.PlayerSide) && !_demoSearchTried.Contains(u.Id)
                                                         && session.IsShownToPlayer(u, revealAll: false));
        if (body is not null)
        {
            _demoSearchTried.Add(body.Id); // one try each: a refused order is not sent again every frame
            session.Selection.Clear();
            session.OrderLoot(body);
        }
    }

    private readonly System.Collections.Generic.HashSet<UnitId> _demoSearchTried = [];

    /// <summary>Whether the camera follows the men in command (L); kept over a restart of the mission.</summary>
    private static bool FollowOn { get; set; }

    private void ToggleFollow(GameSession session)
    {
        FollowOn = !FollowOn;
        _hud.ShowFollow(FollowOn);
        _hud.ShowToast(FollowLeash.ButtonText(FollowOn, session.Language));
    }

    /// <summary>The men in command (all of them when none is picked), still in the fight, as a box in world pixels.</summary>
    private static Rect2? CommandedBox(GameSession session)
    {
        var men = session.CommandedIds.Select(session.Sim.FindUnit).Where(u => u is { IsOutOfAction: false }).ToList();
        if (men.Count == 0)
            return null;
        var first = Coords.ToPixels(session.InterpolatedPositionCm(men[0]!).X, session.InterpolatedPositionCm(men[0]!).Y);
        var box = new Rect2(first, Vector2.Zero);
        foreach (var man in men)
        {
            var (x, y) = session.InterpolatedPositionCm(man!);
            box = box.Expand(Coords.ToPixels(x, y));
        }
        return box.Grow(Coords.PixelsPerCell * 2); // a little room round them
    }

    private void StartDemo(GameSession session)
    {
        session.Selection.Clear(); // the whole squad
        var centre = new Vec2(session.Sim.Map.WidthCm / 2, session.Sim.Map.HeightCm / 2);
        session.OrderMove(centre, MoveMode.Auto);
        _units.Effects.AddOrderFlash(session.CommandedIds, centre, OrderFlashKind.Move);
        session.Clock.TimeScale = 4;
    }
}

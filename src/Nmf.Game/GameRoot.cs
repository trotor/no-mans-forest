using System;
using System.IO;
using System.Linq;
using Godot;
using Nmf.Client;
using Nmf.Client.Art;
using Nmf.Client.Effects;
using Nmf.Content;
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

    private GameSession? _session;
    private ArtLibrary _art = null!;
    private DecorationView _decorations = null!;
    private UnitView _units = null!;
    private FogView _fog = null!;
    private Hud _hud = null!;
    private CameraController _camera = null!;
    private Vector2? _dragStart;
    private string? _screenshotPath;
    private int _frame;

    public override void _Ready()
    {
        SetupWindow();
        string? contentRoot = ContentLocator.FindContentRoot(ProjectSettings.GlobalizePath("res://"))
                              ?? ContentLocator.FindContentRoot(OS.GetExecutablePath().GetBaseDir());
        if (contentRoot is null)
        {
            GD.PushError("[NMF] content/ directory not found");
            GetTree().Quit(1);
            return;
        }

        GridMap map;
        try
        {
            map = TmxMapLoader.Load(Path.Combine(contentRoot, "core", "maps", "skirmish.tmx"));
        }
        catch (MapLoadException ex)
        {
            GD.PushError($"[NMF] {ex.Message}");
            GetTree().Quit(1);
            return;
        }

        System.Collections.Generic.IReadOnlyDictionary<string, Nmf.Sim.Combat.WeaponDef> weapons;
        try
        {
            _art = ArtLibrary.Load(contentRoot);
            weapons = WeaponLoader.LoadDirectory(Path.Combine(contentRoot, "core", "weapons"));
        }
        catch (Exception ex) when (ex is IOException or FormatException or ContentLoadException)
        {
            GD.PushError($"[NMF] {ex.Message}");
            GetTree().Quit(1);
            return;
        }

        GameSession session;
        try
        {
            session = new GameSession(SkirmishScenario.Create(map, seed: 1942, weapons));
        }
        catch (ArgumentException ex)
        {
            GD.PushError($"[NMF] {ex.Message}");
            GetTree().Quit(1);
            return;
        }
        _session = session;

        // World draw order: ground, rocks and bushes, soldiers, tree canopies, fog.
        AddChild(GroundView.Create(map, _art));
        _decorations = new DecorationView();
        _decorations.Build(map, _art);
        AddChild(_decorations.LowLayer);
        _units = new UnitView { Session = session, Art = _art, Animator = new UnitAnimator(), Effects = new CombatEffects() };
        AddChild(_units);
        AddChild(_decorations.CanopyLayer);
        _fog = new FogView { Session = session };
        AddChild(_fog);
        _camera = new CameraController { WorldSize = new Vector2(map.Width, map.Height) * Coords.PixelsPerCell, BottomOverscroll = Hud.BottomBarHeight };
        AddChild(_camera);
        _camera.MakeCurrent();
        _hud = new Hud
        {
            Session = session,
            Art = _art,
            CardClicked = (id, additive) =>
            {
                if (!additive)
                    session.Selection.Clear();
                var unit = session.Sim.FindUnit(id);
                if (unit is not null)
                    session.Selection.SelectAt([unit], session.PlayerSide, unit.Position, 1, additive: true);
            },
            CardDoubleClicked = id =>
            {
                if (session.Sim.FindUnit(id) is { } unit)
                    _camera.CenterOn(Coords.ToPixels(unit.Position));
            },
        };
        AddChild(_hud);

        var firstOwn = session.OwnUnits.FirstOrDefault();
        if (firstOwn is not null)
            _camera.CenterOn(Coords.ToPixels(firstOwn.Position) + new Vector2(0, -200));

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot=", StringComparison.Ordinal))
                _screenshotPath = arg["--screenshot=".Length..];
            else if (arg.StartsWith("--screenshot-frame=", StringComparison.Ordinal) && int.TryParse(arg["--screenshot-frame=".Length..], out int frame) && frame > 0)
                _screenshotFrame = frame;
            else if (arg == "--demo")
                StartDemo(session);
        }

        GD.Print($"[NMF] ready map={map.Width}x{map.Height} units={session.Sim.Units.Count} content={contentRoot}");
    }

    public override void _Process(double delta)
    {
        if (_session is null)
            return;
        _session.Update(delta);
        _units.Effects.Add(_session.TakeEvents(), id => _session.Sim.FindUnit(id) is { } shooter && _session.IsShownToPlayer(shooter, _units.RevealAll));
        _units.Effects.Update(delta);
        var ownPixels = new System.Collections.Generic.List<Vector2>();
        foreach (var unit in _session.Sim.Units)
        {
            if (!_session.IsShownToPlayer(unit, _units.RevealAll))
                continue;
            var (x, y) = _session.InterpolatedPositionCm(unit);
            ownPixels.Add(Coords.ToPixels(x, y));
        }
        _decorations.UpdateCanopyFade(ownPixels, (float)delta);
        _units.DragRect = _dragStart is { } start ? new Rect2(start, GetGlobalMousePosition() - start).Abs() : null;
        _units.Animate();
        _units.QueueRedraw();
        _fog.Refresh();
        _hud.Refresh();

        if (_screenshotPath is not null && ++_frame == _screenshotFrame)
        {
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
            GD.Print($"[NMF] screenshot saved to {_screenshotPath}");
            GetTree().Quit();
        }
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
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } click:
                var target = Coords.ToCm(GetGlobalMousePosition());
                if (_session.EnemyAt(target, ClickRadiusCm) is { } enemy)
                    _session.OrderFireAt(enemy.Id);
                else if (_session.Sim.Map.Contains(target))
                    _session.OrderMove(target, click.ShiftPressed ? MoveMode.Run : click.AltPressed ? MoveMode.Crawl : MoveMode.Walk);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(_session, key.Keycode);
                break;
        }
    }

    private void HandleLeftClick(GameSession session, InputEventMouseButton click)
    {
        if (click.Pressed)
        {
            _dragStart = GetGlobalMousePosition();
            return;
        }
        if (_dragStart is not { } start)
            return;
        var end = GetGlobalMousePosition();
        _dragStart = null;
        if (start.DistanceTo(end) < 6f / _camera.Zoom.X)
            session.Selection.SelectAt(session.Sim.Units, session.PlayerSide, Coords.ToCm(end), ClickRadiusCm, click.ShiftPressed);
        else
            session.Selection.SelectInBox(session.Sim.Units, session.PlayerSide, Coords.ToCm(start), Coords.ToCm(end), click.ShiftPressed);
    }

    private void HandleKey(GameSession session, Key key)
    {
        switch (key)
        {
            case Key.Space:
                session.Clock.Paused = !session.Clock.Paused;
                break;
            case Key.Key1:
                session.OrderStance(Stance.Standing);
                break;
            case Key.Key2:
                session.OrderStance(Stance.Crouching);
                break;
            case Key.Key3:
                session.OrderStance(Stance.Prone);
                break;
            case Key.P:
                session.CycleFirePolicy();
                break;
            case Key.H:
                session.OrderStop();
                break;
            case Key.Plus or Key.Equal or Key.KpAdd:
                session.Clock.TimeScale = Math.Min(4, session.Clock.TimeScale * 2);
                break;
            case Key.Minus or Key.KpSubtract:
                session.Clock.TimeScale = Math.Max(0.25, session.Clock.TimeScale / 2);
                break;
            case Key.Tab:
                SelectAll(session);
                break;
            case Key.Escape:
                session.Selection.Clear();
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

    private static void StartDemo(GameSession session)
    {
        SelectAll(session);
        session.OrderMove(new Vec2(session.Sim.Map.WidthCm / 2, session.Sim.Map.HeightCm / 2), MoveMode.Walk);
        session.Clock.TimeScale = 4;
    }
}

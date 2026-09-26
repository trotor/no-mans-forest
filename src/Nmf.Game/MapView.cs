using System;
using System.Linq;
using Godot;
using Nmf.Client;
using Nmf.Client.Mission;
using Nmf.Sim.Mission;
using Nmf.Sim.Vision;

namespace Nmf.Game;

/// <summary>The whole area as a paper map with what the player knows marked on it (M); a click looks at that spot.</summary>
public partial class MapView : Control
{
    private static readonly Color Own = new(0.12f, 0.3f, 0.75f);
    private static readonly Color Enemy = new(0.75f, 0.12f, 0.1f);
    private static readonly Color StartZone = new(0.1f, 0.42f, 0.15f);
    private static readonly Color EnemyZone = new(0.72f, 0.16f, 0.1f);
    private static readonly Color InkColor = new(0.15f, 0.11f, 0.07f);
    private static readonly Color ViewFrame = new(0.95f, 0.72f, 0.1f);

    private ImageTexture _paper = null!;

    public GameSession Session { get; set; } = null!;
    /// <summary>World pixels the camera shows now, for the frame on the map.</summary>
    public Func<Rect2> ViewRect { get; set; } = () => new Rect2();
    public Action<Vector2> LookAt { get; set; } = _ => { };

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        OffsetTop = Hud.TopBarHeight; // the top bar stays usable over the map
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var image = PaperMap.Render(Session.Sim.Map, 2);
        _paper = ImageTexture.CreateFromImage(Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Rgba));
        TextureFilter = TextureFilterEnum.Linear;
    }

    public void Toggle() => Visible = !Visible;

    public override void _Process(double delta)
    {
        if (Visible)
            QueueRedraw();
    }

    private Rect2 MapRect()
    {
        var size = Size;
        // Room for the title above and the hint below.
        float side = Mathf.Min(size.X - 80, size.Y - 150);
        var map = Session.Sim.Map;
        var paper = new Vector2(side, side * map.Height / map.Width);
        return new Rect2(new Vector2((size.X - paper.X) / 2, 45), paper);
    }

    private Vector2 ToScreen(Rect2 rect, double xCm, double yCm) =>
        rect.Position + new Vector2((float)(xCm / Session.Sim.Map.WidthCm), (float)(yCm / Session.Sim.Map.HeightCm)) * rect.Size;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            var rect = MapRect();
            if (rect.HasPoint(click.Position))
            {
                var t = (click.Position - rect.Position) / rect.Size;
                LookAt(new Vector2(t.X * Session.Sim.Map.Width, t.Y * Session.Sim.Map.Height) * Coords.PixelsPerCell);
                Visible = false;
            }
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        bool fi = Session.Language == "fi";
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.6f));
        var rect = MapRect();
        DrawRect(rect.Grow(6), new Color(0.3f, 0.23f, 0.15f));
        DrawTextureRect(_paper, rect, false);

        // Zones: the start area and the enemy position as reported.
        foreach (var zone in Session.Sim.Map.Features.Zones)
        {
            var a = ToScreen(rect, zone.Min.X, zone.Min.Y);
            var b = ToScreen(rect, zone.Max.X, zone.Max.Y);
            bool enemy = zone.Name == "outpost";
            var colour = enemy ? EnemyZone : StartZone;
            DashedBox(a, b, colour);
            string label = zone.Name switch
            {
                "start_zone" => fi ? "Lähtöalue" : "Start area",
                "outpost" => fi ? "Ilmoitettu vihollinen" : "Reported enemy",
                _ => zone.Name,
            };
            Text(font, new Vector2(a.X, a.Y - 6), label, 15, colour);
        }

        // Objectives still open, numbered as on the orders.
        if (Session.Tracker is { } tracker)
        {
            int number = 0;
            foreach (var objective in tracker.Spec.Objectives)
            {
                number++;
                if (tracker.IsDone(objective.Id) || ObjectiveSpot(objective) is not { } spot)
                    continue;
                var at = ToScreen(rect, spot.X, spot.Y) + new Vector2(0, -14);
                if (objective.Zone is not null || objective.Item is { } && !Session.OwnUnits.Any(u => u.Items.Any(i => i.Id == objective.Item)))
                    at = ZoneCorner(rect, objective) ?? at; // beside the zone box, clear of its name
                DrawCircle(at, 11, new Color(0.95f, 0.85f, 0.3f));
                DrawArc(at, 11, 0, Mathf.Tau, 24, InkColor, 1.5f);
                Text(font, at + new Vector2(-4, 6), number.ToString(), 16, InkColor);
            }
        }

        // What the men know about the enemy: seen now, or last seen / heard.
        foreach (var contact in Session.Knowledge.Contacts)
        {
            if (contact.Level == ContactLevel.Unknown || Session.Sim.FindUnit(contact.Target) is not { } target)
                continue;
            var p = ToScreen(rect, contact.Position.X, contact.Position.Y);
            if (contact.Level == ContactLevel.Visible)
            {
                if (target.IsOutOfAction)
                    Cross(p, 4, Enemy);
                else
                    DrawCircle(p, 4.5f, Enemy);
            }
            else
            {
                Text(font, p + new Vector2(-4, 6), "?", 17, Enemy with { A = 0.7f });
            }
        }

        foreach (var unit in Session.OwnUnits)
        {
            var (x, y) = Session.InterpolatedPositionCm(unit);
            var p = ToScreen(rect, x, y);
            if (unit.IsOutOfAction)
            {
                Cross(p, 4, Own with { A = 0.6f });
                continue;
            }
            DrawCircle(p, 5, Own);
            DrawArc(p, 5, 0, Mathf.Tau, 16, Colors.White, 1.2f);
        }

        // Where the camera looks now.
        var view = ViewRect();
        var worldPx = new Vector2(Session.Sim.Map.Width, Session.Sim.Map.Height) * Coords.PixelsPerCell;
        var va = rect.Position + view.Position / worldPx * rect.Size;
        var vb = rect.Position + view.End / worldPx * rect.Size;
        DrawRect(new Rect2(va, vb - va).Intersection(rect), ViewFrame, false, 1.5f);

        // North arrow and a 100 m scale bar.
        var north = rect.Position + new Vector2(rect.Size.X - 34, 40);
        DrawColoredPolygon([north + new Vector2(0, -22), north + new Vector2(9, 8), north + new Vector2(-9, 8)], InkColor);
        Text(font, north + new Vector2(-6, 30), fi ? "P" : "N", 18, InkColor);
        float hundred = rect.Size.X * 100f / Session.Sim.Map.Width;
        var s = rect.Position + new Vector2(24, rect.Size.Y - 24);
        DrawLine(s, s + new Vector2(hundred, 0), InkColor, 3);
        DrawLine(s + new Vector2(0, -6), s + new Vector2(0, 6), InkColor, 2);
        DrawLine(s + new Vector2(hundred, -6), s + new Vector2(hundred, 6), InkColor, 2);
        Text(font, s + new Vector2(hundred + 8, 6), "100 m", 15, InkColor);

        string title = Session.Mission?.Title.In(Session.Language) ?? "";
        Text(font, new Vector2(rect.Position.X, rect.Position.Y - 16), title, 22, Colors.White);
        string hint = fi ? "M / Esc — sulje · klikkaa katsoaksesi sinne" : "M / Esc — close · click to look there";
        Text(font, new Vector2(rect.Position.X, rect.End.Y + 26), hint, 15, new Color(0.85f, 0.85f, 0.8f));
    }

    /// <summary>Just right of the top-right corner of the objective's zone (or of the reported enemy position).</summary>
    private Vector2? ZoneCorner(Rect2 rect, ObjectiveSpec objective)
    {
        var zones = Session.Sim.Map.Features.Zones;
        var zone = zones.FirstOrDefault(z => z.Name == objective.Zone) ?? zones.FirstOrDefault(z => z.Name == "outpost");
        return zone is null ? null : ToScreen(rect, zone.Max.X, zone.Min.Y) + new Vector2(16, 4);
    }

    /// <summary>Where to put an objective's marker: its zone, the man carrying its item, or the reported enemy position.</summary>
    private Nmf.Sim.Core.Vec2? ObjectiveSpot(ObjectiveSpec objective)
    {
        var zones = Session.Sim.Map.Features.Zones;
        if (objective.Zone is { } name && zones.FirstOrDefault(z => z.Name == name) is { } zone)
            return new Nmf.Sim.Core.Vec2((zone.Min.X + zone.Max.X) / 2, (zone.Min.Y + zone.Max.Y) / 2);
        // The man who has it, standing or fallen (then the papers lie with him).
        if (objective.Item is { } item && Session.OwnUnits.FirstOrDefault(u => u.Items.Any(i => i.Id == item)) is { } carrier)
            return carrier.Position;
        return zones.FirstOrDefault(z => z.Name == "outpost") is { } outpost
            ? new Nmf.Sim.Core.Vec2((outpost.Min.X + outpost.Max.X) / 2, (outpost.Min.Y + outpost.Max.Y) / 2)
            : null;
    }

    private void DashedBox(Vector2 a, Vector2 b, Color colour)
    {
        DrawDashedLine(a, new Vector2(b.X, a.Y), colour, 2, 6);
        DrawDashedLine(new Vector2(b.X, a.Y), b, colour, 2, 6);
        DrawDashedLine(b, new Vector2(a.X, b.Y), colour, 2, 6);
        DrawDashedLine(new Vector2(a.X, b.Y), a, colour, 2, 6);
    }

    private void Cross(Vector2 p, float r, Color colour)
    {
        DrawLine(p - new Vector2(r, r), p + new Vector2(r, r), colour, 2);
        DrawLine(p + new Vector2(-r, r), p + new Vector2(r, -r), colour, 2);
    }

    private void Text(Font font, Vector2 at, string text, int size, Color colour)
    {
        DrawString(font, at + new Vector2(1, 1), text, HorizontalAlignment.Left, -1, size, new Color(1, 1, 1, 0.5f * colour.A));
        DrawString(font, at, text, HorizontalAlignment.Left, -1, size, colour);
    }
}

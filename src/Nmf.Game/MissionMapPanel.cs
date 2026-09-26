using System.Collections.Generic;
using System.Linq;
using Godot;
using Nmf.Client;
using Nmf.Client.Mission;
using Nmf.Sim.Core;
using Nmf.Sim.Mission;

namespace Nmf.Game;

/// <summary>
/// The mission map beside the orders (spec 2026-09-26-maps-design §2): the operation area on the topographic sheet, with
/// the start area, the reported enemy position hatched in red, the attack route as a bold arrow, the way back dashed,
/// and the objective numbers — drawn over the sheet as if by the section leader's pencil.
/// </summary>
public partial class MissionMapPanel : Control
{
    private static readonly Color Friendly = new(0.12f, 0.26f, 0.68f);
    private static readonly Color Hostile = new(0.72f, 0.12f, 0.08f);
    private static readonly Color Ink = new(0.15f, 0.11f, 0.07f);

    private ImageTexture _sheet = null!;
    private MapRegion _region;

    public GameSession Session { get; set; } = null!;

    public override void _Ready()
    {
        var map = Session.Sim.Map;
        var mission = Session.Mission!;
        var points = new List<Vec2>();
        foreach (var zone in map.Features.Zones.Where(z => z.Name is "start_zone" or "outpost"))
        {
            points.Add(zone.Min);
            points.Add(zone.Max);
        }
        points.AddRange(mission.Plan.SelectMany(a => a.Points));
        _region = MissionMapFrame.Region(map, points, 60);
        var image = PaperMap.Render(map, _region, 2);
        var godotImage = Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Rgba);
        godotImage.GenerateMipmaps();
        _sheet = ImageTexture.CreateFromImage(godotImage);
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private Vector2 ToLocal(Rect2 rect, Vec2 cm) =>
        rect.Position + new Vector2((cm.X / 100f - _region.X) / _region.Width, (cm.Y / 100f - _region.Y) / _region.Height) * rect.Size;

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        bool fi = Session.Language == "fi";
        var rect = new Rect2(new Vector2(0, 34), new Vector2(Size.X, Size.X));
        DrawString(font, new Vector2(0, 24), fi ? "TEHTÄVÄKARTTA" : "MISSION MAP", HorizontalAlignment.Left, -1, 20, Ink);
        DrawRect(rect.Grow(3), Ink);
        DrawTextureRect(_sheet, rect, false);

        var zones = Session.Sim.Map.Features.Zones;
        if (zones.FirstOrDefault(z => z.Name == "outpost") is { } outpost)
        {
            var a = ToLocal(rect, outpost.Min);
            var b = ToLocal(rect, outpost.Max);
            Hatch(new Rect2(a, b - a), Hostile);
            DrawRect(new Rect2(a, b - a), Hostile, false, 2.5f);
            DrawString(font, new Vector2(a.X, a.Y - 6), fi ? "VIHOLLINEN (ilm.)" : "ENEMY (reported)", HorizontalAlignment.Left, -1, 14, Hostile);
        }
        if (zones.FirstOrDefault(z => z.Name == "start_zone") is { } start)
        {
            var a = ToLocal(rect, start.Min);
            var b = ToLocal(rect, start.Max);
            DrawRect(new Rect2(a, b - a), Friendly, false, 2.5f);
            DrawString(font, new Vector2(a.X, b.Y + 16), fi ? "LÄHTÖ" : "START", HorizontalAlignment.Left, -1, 14, Friendly);
        }

        foreach (var arrow in Session.Mission!.Plan)
        {
            var line = Smooth(arrow.Points.Select(p => ToLocal(rect, p)).ToList());
            if (arrow.Kind == PlanKind.Attack)
            {
                DrawPolyline(line.ToArray(), Friendly with { A = 0.85f }, 6f, true);
                ArrowHead(line, Friendly, 18);
            }
            else
            {
                for (int i = 0; i + 1 < line.Count; i++)
                    if (i / 3 % 2 == 0)
                        DrawLine(line[i], line[i + 1], Friendly with { A = 0.8f }, 3f, true);
                ArrowHead(line, Friendly, 12);
            }
        }

        // Objective numbers: the papers at the enemy position, the way home at the start.
        int number = 0;
        foreach (var objective in Session.Mission.Objectives)
        {
            number++;
            var zone = zones.FirstOrDefault(z => z.Name == (objective.Zone ?? "outpost"));
            if (zone is null)
                continue;
            var min = ToLocal(rect, zone.Min);
            var max = ToLocal(rect, zone.Max);
            var at = new Vector2(max.X + 16, (min.Y + max.Y) / 2); // right of the box, clear of its name
            DrawCircle(at, 11, new Color(0.97f, 0.88f, 0.35f));
            DrawArc(at, 11, 0, Mathf.Tau, 24, Ink, 1.5f, true);
            DrawString(font, at + new Vector2(-4, 6), number.ToString(), HorizontalAlignment.Left, -1, 15, Ink);
        }

        // North and a 50 m bar.
        var north = rect.Position + new Vector2(rect.Size.X - 26, 30);
        DrawColoredPolygon([north + new Vector2(0, -18), north + new Vector2(7, 6), north + new Vector2(-7, 6)], Ink);
        float fifty = rect.Size.X * 50f / _region.Width;
        var s = rect.Position + new Vector2(14, rect.Size.Y - 16);
        DrawLine(s, s + new Vector2(fifty, 0), Ink, 3);
        DrawString(font, s + new Vector2(fifty + 6, 5), "50 m", HorizontalAlignment.Left, -1, 13, Ink);
    }

    /// <summary>Diagonal red lines across an area, clipped to it.</summary>
    private void Hatch(Rect2 area, Color colour)
    {
        for (float d = -area.Size.Y; d < area.Size.X; d += 7)
        {
            var p0 = new Vector2(area.Position.X + Mathf.Max(d, 0), area.Position.Y + Mathf.Max(-d, 0));
            float len = Mathf.Min(area.Size.X - Mathf.Max(d, 0), area.Size.Y - Mathf.Max(-d, 0));
            if (len > 0)
                DrawLine(p0, p0 + new Vector2(len, len), colour with { A = 0.45f }, 1.5f, true);
        }
    }

    private void ArrowHead(IReadOnlyList<Vector2> line, Color colour, float size)
    {
        if (line.Count < 2)
            return;
        var tip = line[^1];
        var dir = (tip - line[^2]).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        DrawColoredPolygon([tip + dir * size * 0.4f, tip - dir * size + side * size * 0.6f, tip - dir * size - side * size * 0.6f], colour);
    }

    /// <summary>A Catmull-Rom curve through the route points, so arrows sweep like pencil strokes.</summary>
    private static List<Vector2> Smooth(IReadOnlyList<Vector2> points)
    {
        var result = new List<Vector2>();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            var p0 = points[System.Math.Max(0, i - 1)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[System.Math.Min(points.Count - 1, i + 2)];
            for (int k = 0; k < 12; k++)
            {
                float t = k / 12f, t2 = t * t, t3 = t2 * t;
                result.Add(0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3));
            }
        }
        result.Add(points[^1]);
        return result;
    }
}

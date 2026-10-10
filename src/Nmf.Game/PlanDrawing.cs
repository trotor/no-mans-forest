using System.Collections.Generic;
using System.Linq;
using Godot;
using Nmf.Client;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;
using Vec2 = Nmf.Sim.Core.Vec2;

namespace Nmf.Game;

/// <summary>
/// An attack plan on the map (spec 2026-10-10-plan-preview-design §3): the target, the fire base and its line of fire, the
/// strike team's way (red where the enemy sees it) and its form-up, the assault; in the preview with handles to drag.
/// </summary>
public static class PlanDrawing
{
    private static readonly Color Target = new(1f, 0.3f, 0.25f);
    private static readonly Color Fire = new(1f, 0.62f, 0.2f);
    private static readonly Color Hidden = new(0.45f, 1f, 0.45f);
    private static readonly Color Seen = new(1f, 0.28f, 0.22f);
    private static readonly Color Handle = new(1f, 1f, 1f);
    private static readonly Color HotHandle = new(1f, 0.9f, 0.35f);
    private static readonly Color Shadow = new(0, 0, 0, 0.55f);

    /// <param name="alpha">1 in the preview; faded for an attack under way.</param>
    /// <param name="handles">The handles to draw (the preview only).</param>
    /// <param name="hot">The handle under the mouse.</param>
    /// <param name="drag">A handle being dragged, and where to (a new route point when the handle is null).</param>
    public static void Draw(CanvasItem c, GameSession session, TacticPlan plan, float zoom, float alpha,
        IReadOnlyList<PlanHandle>? handles = null, PlanHandle? hot = null, (PlanHandle? Handle, Vec2 At)? drag = null)
    {
        float px = 1f / Mathf.Max(zoom, 0.05f);
        var aim = Coords.ToPixels(plan.Aim);
        DrawTarget(c, session, plan.Aim, plan.Position, px, alpha);

        // The fire base: each man's spot, the line of fire to the position, the fire team's own way there.
        var fireSpots = plan.FireTeam.Select(p => Coords.ToPixels(p.At)).ToList();
        var fireCentre = Centre(fireSpots);
        if (plan.FireRoute is { Count: > 0 } fireRoute)
            Polyline(c, [MenCentre(session, plan.FireTeam.Select(p => p.Man)) ?? fireCentre, .. fireRoute.Select(Coords.ToPixels)], Fire with { A = 0.7f * alpha }, 2f * px, dashed: true, px);
        Line(c, fireCentre, aim, Fire with { A = 0.85f * alpha }, 3f * px, dashed: true, px);
        foreach (var spot in fireSpots)
            Spot(c, spot, Fire with { A = alpha }, px);

        // The strike team: its way to the form-up, coloured by what the enemy sees, the spots there, the assault.
        if (plan.StrikeTeam.Count > 0 && plan.Route.Count > 0)
        {
            var start = MenCentre(session, plan.StrikeTeam) ?? Coords.ToPixels(plan.Route[0]);
            var points = new List<Vector2> { start };
            points.AddRange(plan.Route.Select(Coords.ToPixels));
            var exposed = plan.RouteExposed ?? [];
            for (int i = 1; i < points.Count; i++)
            {
                bool seen = i - 1 < exposed.Count && exposed[i - 1];
                var colour = (seen ? Seen : Hidden) with { A = 0.9f * alpha };
                c.DrawLine(points[i - 1], points[i], Shadow with { A = Shadow.A * alpha }, 6f * px, true);
                c.DrawLine(points[i - 1], points[i], colour, 3.5f * px, true);
            }
            Arrowhead(c, points[^2], points[^1], Hidden with { A = alpha }, px);
            var formUp = plan.FormUp.Select(p => Coords.ToPixels(p.At)).ToList();
            foreach (var spot in formUp)
                Spot(c, spot, Hidden with { A = alpha }, px);
            var formUpCentre = Centre(formUp);
            Line(c, formUpCentre, aim, Hidden with { A = 0.8f * alpha }, 3f * px, dashed: true, px);
            Arrowhead(c, formUpCentre, aim, Hidden with { A = 0.8f * alpha }, px);
        }

        if (handles is null)
            return;
        foreach (var h in handles)
        {
            bool dragging = drag?.Handle == h;
            var at = Coords.ToPixels(dragging ? drag!.Value.At : h.At);
            if (dragging)
                Line(c, Coords.ToPixels(h.At), at, HotHandle, 2f * px, dashed: true, px);
            DrawHandle(c, at, h == hot || dragging, h.Kind == PlanHandleKind.Via, px);
        }
        if (drag is { Handle: null, At: var newPoint })
            DrawHandle(c, Coords.ToPixels(newPoint), true, true, px);
    }

    /// <summary>The straight assault: an arrow from every man to the target.</summary>
    public static void DrawAssault(CanvasItem c, GameSession session, IReadOnlyList<UnitId> men, UnitId target, float zoom)
    {
        if (TacticPlanner.AimOf(session.Sim, session.PlayerSide, target) is not { } aimCm)
            return;
        float px = 1f / Mathf.Max(zoom, 0.05f);
        var aim = Coords.ToPixels(aimCm);
        DrawTarget(c, session, aimCm, TacticPlanner.PositionOf(session.Sim, session.PlayerSide, aimCm), px, 1f);
        foreach (var id in men)
        {
            if (session.Sim.FindUnit(id) is not { IsOutOfAction: false } man)
                continue;
            var from = Coords.ToPixels(man.Position);
            c.DrawLine(from, aim, Shadow, 5f * px, true);
            c.DrawLine(from, aim, Seen with { A = 0.85f }, 2.5f * px, true);
            Arrowhead(c, from, aim, Seen, px);
        }
    }

    private static void DrawTarget(CanvasItem c, GameSession session, Vec2 aimCm, IReadOnlyList<UnitId> position, float px, float alpha)
    {
        var aim = Coords.ToPixels(aimCm);
        c.DrawArc(aim, 22f * px, 0, Mathf.Tau, 32, Shadow with { A = Shadow.A * alpha }, 6f * px, true);
        c.DrawArc(aim, 22f * px, 0, Mathf.Tau, 32, Target with { A = alpha }, 3f * px, true);
        var knowledge = session.Sim.Knowledge(session.PlayerSide);
        foreach (var id in position)
        {
            if (knowledge.Get(id) is not { } contact)
                continue;
            var at = Coords.ToPixels(TacticPlanner.KnownPlace(session.Sim, contact));
            c.DrawArc(at, 12f * px, 0, Mathf.Tau, 24, Target with { A = 0.7f * alpha }, 2f * px, true);
        }
    }

    private static void DrawHandle(CanvasItem c, Vector2 at, bool hot, bool small, float px)
    {
        float r = (small ? 11f : 15f) * (hot ? 1.25f : 1f) * px;
        var colour = hot ? HotHandle : Handle;
        c.DrawCircle(at, r, new Color(0, 0, 0, 0.35f));
        c.DrawArc(at, r, 0, Mathf.Tau, 32, Shadow, 6f * px, true);
        c.DrawArc(at, r, 0, Mathf.Tau, 32, colour, 3f * px, true);
    }

    private static void Spot(CanvasItem c, Vector2 at, Color colour, float px)
    {
        c.DrawCircle(at, 6f * px, Shadow with { A = Shadow.A * colour.A });
        c.DrawCircle(at, 4.5f * px, colour);
    }

    private static void Line(CanvasItem c, Vector2 a, Vector2 b, Color colour, float width, bool dashed, float px)
    {
        if (dashed)
        {
            c.DrawDashedLine(a, b, Shadow with { A = Shadow.A * colour.A }, width + 3f * px, 14f * px, true);
            c.DrawDashedLine(a, b, colour, width, 14f * px, true);
        }
        else
        {
            c.DrawLine(a, b, Shadow with { A = Shadow.A * colour.A }, width + 3f * px, true);
            c.DrawLine(a, b, colour, width, true);
        }
    }

    private static void Polyline(CanvasItem c, IReadOnlyList<Vector2> points, Color colour, float width, bool dashed, float px)
    {
        for (int i = 1; i < points.Count; i++)
            Line(c, points[i - 1], points[i], colour, width, dashed, px);
    }

    private static void Arrowhead(CanvasItem c, Vector2 from, Vector2 to, Color colour, float px)
    {
        var dir = to - from;
        if (dir.LengthSquared() < 1e-6f)
            return;
        dir = dir.Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        float size = 14f * px;
        var tip = to;
        var a = to - dir * size + side * size * 0.55f;
        var b = to - dir * size - side * size * 0.55f;
        c.DrawColoredPolygon([tip, a, b], colour);
    }

    private static Vector2 Centre(IReadOnlyList<Vector2> points) =>
        points.Count == 0 ? Vector2.Zero : points.Aggregate(Vector2.Zero, (s, p) => s + p) / points.Count;

    private static Vector2? MenCentre(GameSession session, IEnumerable<UnitId> men)
    {
        var at = men.Select(session.Sim.FindUnit).Where(u => u is { IsOutOfAction: false }).Select(u => Coords.ToPixels(u!.Position)).ToList();
        return at.Count == 0 ? null : Centre(at);
    }
}

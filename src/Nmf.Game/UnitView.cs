using System.Collections.Generic;
using Godot;
using Nmf.Client;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Game;

/// <summary>Draws soldiers, selection, paths and enemy contacts the player knows about.</summary>
public partial class UnitView : Node2D
{
    private static readonly Color BlueFill = new(0.27f, 0.45f, 0.85f);
    private static readonly Color RedFill = new(0.82f, 0.22f, 0.18f);
    private static readonly Color SelectedRing = new(1f, 0.92f, 0.3f);
    private static readonly Color PathColor = new(1f, 0.92f, 0.3f, 0.6f);
    private static readonly Color LastKnownColor = new(0.9f, 0.3f, 0.2f, 0.7f);
    private static readonly Color SuspectedColor = new(1f, 0.6f, 0.1f, 0.18f);

    public GameSession Session { get; set; } = null!;
    public bool RevealAll { get; set; }
    public Rect2? DragRect { get; set; }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        foreach (var contact in Session.Knowledge.Contacts)
        {
            var at = Coords.ToPixels(contact.Position);
            switch (contact.Level)
            {
                case ContactLevel.Suspected:
                    DrawCircle(at, 5f * Coords.PixelsPerCell, SuspectedColor);
                    DrawString(font, at + new Vector2(-4, 6), "?", HorizontalAlignment.Left, -1, 18, new Color(1f, 0.7f, 0.2f));
                    break;
                case ContactLevel.LastKnown:
                    DrawArc(at, 6f, 0f, Mathf.Tau, 20, LastKnownColor, 1.5f);
                    DrawString(font, at + new Vector2(8, -6), "?", HorizontalAlignment.Left, -1, 14, LastKnownColor);
                    break;
            }
        }

        foreach (var unit in Session.Sim.Units)
        {
            bool own = unit.Side == Session.PlayerSide;
            if (!own && !RevealAll && Session.Knowledge.LevelOf(unit.Id) != ContactLevel.Visible)
                continue;

            var (x, y) = Session.InterpolatedPositionCm(unit);
            var pos = Coords.ToPixels(x, y);
            float radius = unit.Stance switch { Stance.Standing => 6f, Stance.Crouching => 5f, _ => 4f };

            if (own && Session.Selection.Contains(unit.Id))
            {
                var points = new List<Vector2> { pos };
                for (int i = unit.PathIndex; i < unit.Path.Count; i++)
                    points.Add(Coords.ToPixels(unit.Path[i]));
                if (points.Count >= 2)
                    DrawPolyline(points.ToArray(), PathColor, 1.5f);
                DrawArc(pos, radius + 3f, 0f, Mathf.Tau, 24, SelectedRing, 2f);
            }

            DrawCircle(pos, radius, own ? BlueFill : RedFill);
            DrawArc(pos, radius, 0f, Mathf.Tau, 16, Colors.Black, 1f);
            if (unit.TargetStance is not null)
                DrawArc(pos, radius + 1.5f, 0f, Mathf.Pi, 12, Colors.White, 1f);
        }

        if (DragRect is { } rect)
            DrawRect(rect, SelectedRing, false, 1f);
    }
}

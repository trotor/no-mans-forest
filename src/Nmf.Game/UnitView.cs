using System.Collections.Generic;
using System.Linq;
using Godot;
using Nmf.Client;
using Nmf.Client.Art;
using Nmf.Sim.Units;
using Side = Nmf.Sim.Units.Side;
using Nmf.Sim.Vision;

namespace Nmf.Game;

/// <summary>Draws soldiers as animated sprites plus selection, paths and the contacts the player knows about.</summary>
public partial class UnitView : Node2D
{
    private static readonly Color SelectedRing = new(1f, 0.9f, 0.35f, 0.95f);
    private static readonly Color PathColor = new(1f, 0.9f, 0.35f, 0.75f);
    private static readonly Color SuspectedColor = new(1f, 0.62f, 0.15f, 0.9f);
    private static readonly Color GhostTint = new(1f, 0.55f, 0.5f, 0.4f);

    // Soldiers are drawn larger than true scale (as in JA2 / Close Combat) so they read against the terrain.
    private const float SpriteScale = 1.5f;

    public GameSession Session { get; set; } = null!;
    public ArtLibrary Art { get; set; } = null!;
    public UnitAnimator Animator { get; set; } = null!;
    public bool RevealAll { get; set; }
    public Rect2? DragRect { get; set; }

    public void Animate()
    {
        foreach (var unit in Session.Sim.Units)
            Animator.Update(unit, Session.InterpolatedPositionCm(unit), Session.Clock.Paused);
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var sheet = Art.SoldierSheet;
        float cell = sheet.CellSize * SpriteScale;

        foreach (var contact in Session.Knowledge.Contacts)
        {
            var at = Coords.ToPixels(contact.Position);
            if (contact.Level == ContactLevel.Suspected)
            {
                float r = 5f * Coords.PixelsPerCell;
                for (int i = 0; i < 16; i += 2)
                    DrawArc(at, r, i * Mathf.Tau / 16, (i + 1) * Mathf.Tau / 16, 6, SuspectedColor, 2f);
                DrawString(font, at + new Vector2(-9, 12), "?", HorizontalAlignment.Left, -1, 34, SuspectedColor);
            }
            else if (contact.Level == ContactLevel.LastKnown && Session.Sim.FindUnit(contact.Target) is { } ghost)
            {
                var frame = sheet.FrameRect("idle", Animator.Current(ghost, sheet).Direction, 0);
                DrawTextureRectRegion(Art.Soldiers(ghost.Side), new Rect2(at - new Vector2(cell, cell) / 2, cell, cell),
                    new Rect2(frame.X, frame.Y, frame.Size, frame.Size), GhostTint);
                DrawString(font, at + new Vector2(10, -12), "?", HorizontalAlignment.Left, -1, 24, GhostTint with { A = 0.9f });
            }
        }

        var visible = Session.Sim.Units
            .Where(u => u.Side == Session.PlayerSide || RevealAll || Session.Knowledge.LevelOf(u.Id) == ContactLevel.Visible)
            .Select(u => (Unit: u, Pos: Coords.ToPixels(Session.InterpolatedPositionCm(u).X, Session.InterpolatedPositionCm(u).Y)))
            .OrderBy(p => p.Pos.Y)
            .ToList();

        foreach (var (unit, pos) in visible)
        {
            bool prone = unit.Stance == Stance.Prone;
            var shadowSize = new Vector2(prone ? 50 : 38, prone ? 30 : 24);
            DrawTextureRect(Art.Shadow, new Rect2(pos - shadowSize / 2 + new Vector2(6, 7), shadowSize), false, new Color(1, 1, 1, 0.55f));
            if (unit.Side == Session.PlayerSide && Session.Selection.Contains(unit.Id))
            {
                DrawSetTransform(pos, 0, new Vector2(1f, 0.62f));
                DrawArc(Vector2.Zero, prone ? 44 : 25, 0, Mathf.Tau, 32, SelectedRing, 2.5f);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                var from = pos;
                for (int i = unit.PathIndex; i < unit.Path.Count; i++)
                {
                    var to = Coords.ToPixels(unit.Path[i]);
                    DrawDashedLine(from, to, PathColor, 2f, 8f);
                    from = to;
                }
            }
        }

        foreach (var (unit, pos) in visible)
        {
            var anim = Animator.Current(unit, sheet);
            var frame = sheet.FrameRect(anim.Animation, anim.Direction, anim.Frame);
            DrawTextureRectRegion(Art.Soldiers(unit.Side), new Rect2(pos - new Vector2(cell, cell) / 2, cell, cell),
                new Rect2(frame.X, frame.Y, frame.Size, frame.Size));
        }

        if (DragRect is { } rect)
        {
            DrawRect(rect, SelectedRing with { A = 0.12f }, true);
            DrawRect(rect, SelectedRing, false, 1.5f);
        }
    }
}

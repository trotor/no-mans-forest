using System.Collections.Generic;
using System.Linq;
using Godot;
using Nmf.Client;
using Nmf.Client.Art;
using Nmf.Client.Effects;
using Nmf.Sim.Combat;
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
    private static readonly Color PinnedColor = new(1f, 0.6f, 0.15f);
    private static readonly Color BrokenColor = new(1f, 0.25f, 0.2f);
    private static readonly Color ReloadColor = new(0.85f, 0.85f, 0.8f);
    private static readonly Color AimLine = new(1f, 0.3f, 0.25f, 0.6f);
    private const float MuzzleCm = 60f;
    private static readonly Color OwnGlow = new(0.55f, 0.85f, 1f, 0.85f);
    private static readonly Color EnemyGlow = new(1f, 0.4f, 0.3f, 0.85f);
    private static readonly Color HoverEnemy = new(1f, 0.3f, 0.25f, 0.9f);
    private static readonly Color MoveMarkerColor = new(0.45f, 1f, 0.45f);
    private static readonly Vector2[] GlowOffsets =
        [new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(0.7f, 0.7f), new(-0.7f, 0.7f), new(0.7f, -0.7f), new(-0.7f, -0.7f)];

    // Soldiers are drawn larger than true scale (as in JA2 / Close Combat) so they read against the terrain.
    private const float SpriteScale = 1.5f;

    public GameSession Session { get; set; } = null!;
    public ArtLibrary Art { get; set; } = null!;
    public UnitAnimator Animator { get; set; } = null!;
    public bool RevealAll { get; set; }
    public Rect2? DragRect { get; set; }
    public CombatEffects Effects { get; set; } = null!;
    public Nmf.Sim.Core.Vec2 HoverCm { get; set; }
    public float Zoom { get; set; } = 1f;

    /// <summary>Only units the player may see are animated, so a last-known ghost keeps the facing it was last seen with.</summary>
    public void Animate()
    {
        foreach (var unit in Session.Sim.Units)
            if (Session.IsShownToPlayer(unit, RevealAll))
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
            else if (contact.Level == ContactLevel.LastKnown)
            {
                // Drawn from the player's memory only: the enemy side and the facing when last seen.
                var enemySide = Session.PlayerSide == Side.Blue ? Side.Red : Side.Blue;
                var frame = sheet.FrameRect("idle", Animator.DirectionOf(contact.Target, Facing.South), 0);
                DrawTextureRectRegion(Art.Soldiers(enemySide), new Rect2(at - new Vector2(cell, cell) / 2, cell, cell),
                    new Rect2(frame.X, frame.Y, frame.Size, frame.Size), GhostTint);
                DrawString(font, at + new Vector2(10, -12), "?", HorizontalAlignment.Left, -1, 24, GhostTint with { A = 0.9f });
            }
        }

        var visible = Session.Sim.Units
            .Where(u => Session.IsShownToPlayer(u, RevealAll))
            .Select(u => (Unit: u, Pos: Coords.ToPixels(Session.InterpolatedPositionCm(u).X, Session.InterpolatedPositionCm(u).Y)))
            .OrderBy(p => p.Unit.IsAlive ? 1 : 0) // corpses under the living
            .ThenBy(p => p.Pos.Y)
            .ToList();

        foreach (var (unit, pos) in visible)
        {
            if (!unit.IsAlive)
            {
                float blood = Art.Blood.GetHeight() * 1.2f;
                DrawTextureRectRegion(Art.Blood, new Rect2(pos - new Vector2(blood, blood) / 2 + new Vector2(0, 6), blood, blood),
                    new Rect2(unit.Id.Value % 2 * Art.Blood.GetHeight(), 0, Art.Blood.GetHeight(), Art.Blood.GetHeight()), new Color(1, 1, 1, 0.85f));
                continue;
            }
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
            var src = new Rect2(frame.X, frame.Y, frame.Size, frame.Size);
            var dest = new Rect2(pos - new Vector2(cell, cell) / 2, cell, cell);
            if (unit.IsAlive)
            {
                // A thin team-coloured glow keeps soldiers readable on any ground and at any zoom.
                float width = Mathf.Clamp(1.6f / Zoom, 1.2f, 5f);
                var glow = unit.Side == Session.PlayerSide ? OwnGlow : EnemyGlow;
                foreach (var offset in GlowOffsets)
                    DrawTextureRectRegion(Art.Silhouettes(unit.Side), new Rect2(dest.Position + offset * width, dest.Size), src, glow);
            }
            DrawTextureRectRegion(Art.Soldiers(unit.Side), dest, src);
        }

        foreach (var (unit, pos) in visible)
        {
            if (unit.Side != Session.PlayerSide || unit.IsOutOfAction)
                continue;
            if (Session.Selection.Contains(unit.Id) && unit.Target is { } targetId && Session.Sim.FindUnit(targetId) is { } target
                && Session.IsShownToPlayer(target, RevealAll))
            {
                var (tx, ty) = Session.InterpolatedPositionCm(target);
                DrawDashedLine(pos, Coords.ToPixels(tx, ty), AimLine, 1.5f, 6f);
            }
            var (icon, colour) = unit.MoraleState switch
            {
                MoraleState.Broken => ("!!", BrokenColor),
                MoraleState.Pinned => ("!", PinnedColor),
                _ => unit.Action == CombatAction.Reloading ? ("R", ReloadColor) : ("", ReloadColor),
            };
            if (icon.Length > 0)
            {
                var at = pos + new Vector2(-6, -cell * 0.32f);
                DrawString(font, at + new Vector2(1, 1), icon, HorizontalAlignment.Left, -1, 22, Colors.Black);
                DrawString(font, at, icon, HorizontalAlignment.Left, -1, 22, colour);
            }
        }

        if (Session.Selection.Count > 0 && Session.EnemyAt(HoverCm, GameSession.ClickRadiusCm) is { } hovered)
        {
            var (hx, hy) = Session.InterpolatedPositionCm(hovered);
            var at = Coords.ToPixels(hx, hy);
            DrawSetTransform(at, 0, new Vector2(1f, 0.62f));
            DrawArc(Vector2.Zero, 30, 0, Mathf.Tau, 32, HoverEnemy, 3f / Mathf.Max(Zoom, 0.5f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        foreach (var effect in Effects.Active)
        {
            float fade = 1f - (float)effect.Progress;
            var from = Coords.ToPixels(effect.From);
            var to = Coords.ToPixels(effect.To);
            var dir = (to - from).Normalized();
            var muzzle = from + dir * MuzzleCm * Coords.PixelsPerCm;
            switch (effect.Kind)
            {
                case EffectKind.Tracer:
                    DrawLine(muzzle, to, new Color(1f, 0.92f, 0.55f, fade), 2f);
                    break;
                case EffectKind.MuzzleFlash:
                    DrawCircle(muzzle, 5f * fade + 2f, new Color(1f, 0.95f, 0.65f, fade));
                    break;
                case EffectKind.Impact:
                    DrawCircle(to, 3f + 6f * (float)effect.Progress, new Color(0.55f, 0.45f, 0.3f, 0.7f * fade));
                    break;
                case EffectKind.MoveMarker:
                    DrawSetTransform(to, 0, new Vector2(1f, 0.62f));
                    DrawArc(Vector2.Zero, 12f + 22f * fade, 0, Mathf.Tau, 28, MoveMarkerColor with { A = fade }, 2.5f);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    break;
                case EffectKind.FireMarker:
                    float r = 14f + 10f * fade;
                    var red = HoverEnemy with { A = fade };
                    DrawArc(to, r, 0, Mathf.Tau, 28, red, 2.5f);
                    DrawLine(to - new Vector2(r + 6, 0), to - new Vector2(r - 6, 0), red, 2.5f);
                    DrawLine(to + new Vector2(r - 6, 0), to + new Vector2(r + 6, 0), red, 2.5f);
                    DrawLine(to - new Vector2(0, r + 6), to - new Vector2(0, r - 6), red, 2.5f);
                    DrawLine(to + new Vector2(0, r - 6), to + new Vector2(0, r + 6), red, 2.5f);
                    break;
            }
        }

        if (DragRect is { } rect)
        {
            DrawRect(rect, SelectedRing with { A = 0.12f }, true);
            DrawRect(rect, SelectedRing, false, 1.5f);
        }
    }
}

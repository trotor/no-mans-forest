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
    private static readonly Color BagColor = new(0.78f, 0.68f, 0.45f);
    private static readonly Color BagEdge = new(0.2f, 0.16f, 0.08f);
    private static readonly Color NoteColor = new(1f, 0.95f, 0.75f);
    private static readonly Color PathColor = new(1f, 0.9f, 0.35f, 0.75f);
    private static readonly Color SuspectedColor = new(1f, 0.62f, 0.15f, 0.9f);
    private static readonly Color GhostTint = new(1f, 0.55f, 0.5f, 0.4f);
    private static readonly Color PinnedColor = new(1f, 0.6f, 0.15f);
    private static readonly Color BrokenColor = new(1f, 0.25f, 0.2f);
    private static readonly Color ReloadColor = new(0.85f, 0.85f, 0.8f);
    private static readonly Color AimLine = new(1f, 0.3f, 0.25f, 0.6f);
    private const float MuzzleCm = 60f;
    private static readonly Color CraterColor = new(0.12f, 0.1f, 0.07f, 0.55f);
    private static readonly Color GrenadeColor = new(0.15f, 0.17f, 0.12f);
    private static readonly Color SquadRing = new(1f, 0.9f, 0.35f, 0.35f);
    private static readonly Color CapturedTint = new(0.6f, 0.6f, 0.6f);
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

    /// <summary>Below this zoom events are shown as signals (spec 2026-09-26-maps-design §4).</summary>
    public const float SignalZoom = 0.4f;
    private const float SignalRadiusPx = 14f;

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

        foreach (var crater in Effects.Craters)
        {
            DrawSetTransform(Coords.ToPixels(crater), 0.3f, new Vector2(1f, 0.7f));
            DrawCircle(Vector2.Zero, 22f, CraterColor);
            DrawCircle(new Vector2(4, 3), 11f, CraterColor);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

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
            if (unit.Side == Session.PlayerSide && Session.IsSquadCommanded && !unit.IsOutOfAction)
            {
                DrawSetTransform(pos, 0, new Vector2(1f, 0.62f));
                DrawArc(Vector2.Zero, prone ? 44 : 25, 0, Mathf.Tau, 32, SquadRing, 2f);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
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
            if (unit.IsAlive && !unit.IsCaptured)
            {
                // A thin team-coloured glow keeps soldiers readable on any ground and at any zoom.
                float width = Mathf.Clamp(1.6f / Zoom, 1.2f, 5f);
                var glow = unit.Side == Session.PlayerSide ? OwnGlow : EnemyGlow;
                foreach (var offset in GlowOffsets)
                    DrawTextureRectRegion(Art.Silhouettes(unit.Side), new Rect2(dest.Position + offset * width, dest.Size), src, glow);
            }
            DrawTextureRectRegion(Art.Soldiers(unit.Side), dest, src, unit.IsCaptured ? CapturedTint : null);
        }

        var commanded = Session.CommandedIds;
        foreach (var (unit, pos) in visible)
        {
            if (unit.Side != Session.PlayerSide || unit.IsOutOfAction)
                continue;
            if (commanded.Contains(unit.Id) && unit.Target is { } targetId && Session.Sim.FindUnit(targetId) is { } target
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

        foreach (var (unit, pos) in visible)
        {
            if (unit.Action == CombatAction.Melee)
                Icon(font, pos + new Vector2(-10, -cell * 0.36f), "⚔", new Color(1f, 0.85f, 0.5f));
            else if (unit.IsCaptured)
                Icon(font, pos + new Vector2(-6, -cell * 0.3f), "⚑", Colors.White);
            if (unit.IsOutOfAction && !unit.Looted)
                DrawBag(pos + new Vector2(cell * 0.28f, cell * 0.1f));
        }

        // What a man found on a body, floating over him for a few seconds.
        foreach (var note in Effects.Notes)
        {
            if (Session.Sim.FindUnit(note.Unit) is not { } looter || !Session.IsShownToPlayer(looter, RevealAll))
                continue;
            var (lx, ly) = Session.InterpolatedPositionCm(looter);
            float rise = (float)(note.Age / note.Lifetime) * 14f;
            var size = font.GetStringSize(note.Text, HorizontalAlignment.Left, -1, 18);
            var at = Coords.ToPixels(lx, ly) + new Vector2(-size.X / 2, -cell * 0.5f - rise);
            DrawString(font, at + new Vector2(1, 1), note.Text, HorizontalAlignment.Left, -1, 18, Colors.Black);
            DrawString(font, at, note.Text, HorizontalAlignment.Left, -1, 18, NoteColor);
        }

        // Zoomed far out, shots and blasts are too small to see: show them as signals of a fixed screen size.
        if (Zoom < SignalZoom)
        {
            float r = SignalRadiusPx / Zoom, w = 2.5f / Zoom;
            foreach (var signal in Effects.Signals)
                SignalDrawing.Draw(this, signal, Coords.ToPixels(signal.At), r, w);
        }

        foreach (var grenade in Session.Sim.Grenades)
        {
            var landingCell = grenade.Landing.ToCell();
            bool landingSeen = Session.Sim.Map.InBounds(landingCell)
                               && Session.Fog.IsVisible(grenade.Landing);
            bool throwerSeen = Session.Sim.FindUnit(grenade.Thrower) is { } thrower && Session.IsShownToPlayer(thrower, RevealAll);
            if (!landingSeen && !throwerSeen)
                continue;
            long now = Session.Sim.Tick;
            float t = Mathf.Clamp((now - grenade.ThrowTick) / (float)(grenade.LandTick - grenade.ThrowTick), 0f, 1f);
            var from = Coords.ToPixels(grenade.From);
            var to = Coords.ToPixels(grenade.Landing);
            var at = from.Lerp(to, t) + new Vector2(0, -Mathf.Sin(t * Mathf.Pi) * 40f); // lobbed arc
            bool landed = t >= 1f;
            if (landed && (now / 4) % 2 == 0)
                DrawCircle(at, 7f, new Color(1f, 0.3f, 0.2f, 0.45f)); // blinking: live grenade on the ground
            if (grenade.Def.Id == "m32")
                DrawLine(at, at + new Vector2(6, -6), new Color(0.45f, 0.32f, 0.2f), 2.5f); // stick grenade handle
            DrawCircle(at, 3.5f, GrenadeColor);
        }

        if (Session.CommandedIds.Count > 0 && Session.EnemyAt(HoverCm, GameSession.ClickRadiusCm) is { } hovered)
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
                case EffectKind.Explosion:
                    float p = (float)effect.Progress;
                    DrawCircle(to, 18f + 30f * p, new Color(1f, 0.85f, 0.45f, 0.8f * fade * fade));
                    DrawArc(to, 20f + 70f * p, 0, Mathf.Tau, 36, new Color(0.35f, 0.33f, 0.3f, 0.7f * fade), 6f);
                    DrawCircle(to, 12f + 40f * p, new Color(0.45f, 0.4f, 0.32f, 0.35f * fade));
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

    /// <summary>A small haversack by a body that has not been searched yet.</summary>
    private void DrawBag(Vector2 at)
    {
        var rect = new Rect2(at - new Vector2(7, 5), new Vector2(14, 11));
        DrawRect(rect.Grow(1.5f), BagEdge);
        DrawRect(rect, BagColor);
        DrawLine(at + new Vector2(-7, -1), at + new Vector2(7, -1), BagEdge, 1.5f);
        DrawArc(at + new Vector2(0, -5), 4, Mathf.Pi, Mathf.Tau, 8, BagEdge, 1.5f);
    }

    private void Icon(Font font, Vector2 at, string text, Color colour)
    {
        DrawString(font, at + new Vector2(1, 1), text, HorizontalAlignment.Left, -1, 22, Colors.Black);
        DrawString(font, at, text, HorizontalAlignment.Left, -1, 22, colour);
    }
}

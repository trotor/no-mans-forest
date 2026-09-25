using Nmf.Client.Effects;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Client.Tests.Effects;

public class CombatEffectsTests
{
    private static readonly ShotFired Miss = new(0, new UnitId(1), new Vec2(0, 0), new Vec2(1000, 0), null);
    private static readonly ShotFired Hit = new(0, new UnitId(1), new Vec2(0, 0), new Vec2(1000, 0), new UnitId(2));

    [Fact]
    public void ShownShooter_AddsTracerFlashAndImpactOnMiss()
    {
        var fx = new CombatEffects();
        fx.Add([Miss, Hit], _ => true);
        Assert.Equal(2, fx.Active.Count(e => e.Kind == EffectKind.Tracer));
        Assert.Equal(2, fx.Active.Count(e => e.Kind == EffectKind.MuzzleFlash));
        Assert.Single(fx.Active, e => e.Kind == EffectKind.Impact);
    }

    [Fact]
    public void HiddenShooter_ShowsOnlyTheImpact()
    {
        var fx = new CombatEffects();
        fx.Add([Miss], _ => false);
        var only = Assert.Single(fx.Active);
        Assert.Equal(EffectKind.Impact, only.Kind);
        Assert.Equal(new Vec2(1000, 0), only.From);
    }

    [Fact]
    public void Update_AgesAndRemovesEffects()
    {
        var fx = new CombatEffects();
        fx.Add([Miss], _ => true);
        fx.Update(0.1);
        Assert.DoesNotContain(fx.Active, e => e.Kind == EffectKind.MuzzleFlash);
        Assert.Contains(fx.Active, e => e.Kind == EffectKind.Tracer);
        Assert.InRange(fx.Active.First(e => e.Kind == EffectKind.Tracer).Progress, 0.8, 0.9);
        fx.Update(1.0);
        Assert.Empty(fx.Active);
    }

    [Fact]
    public void HiddenShooterHit_StillShowsWhereTheBulletLanded()
    {
        var fx = new CombatEffects();
        fx.Add([Hit], _ => false);
        var only = Assert.Single(fx.Active);
        Assert.Equal(EffectKind.Impact, only.Kind);
    }

    [Fact]
    public void Markers_ShowBrieflyWhereAClickSentTheMen()
    {
        var fx = new CombatEffects();
        fx.AddMarker(EffectKind.MoveMarker, new Vec2(500, 500));
        var marker = Assert.Single(fx.Active);
        Assert.Equal(EffectKind.MoveMarker, marker.Kind);
        fx.Update(CombatEffects.MarkerSeconds + 0.01);
        Assert.Empty(fx.Active);
    }

    [Fact]
    public void GrenadeExplosion_AddsAnExplosionAndALastingCrater()
    {
        var fx = new CombatEffects();
        fx.Add([new GrenadeExploded(0, 1, new Vec2(700, 700))], _ => false);
        Assert.Contains(fx.Active, e => e.Kind == EffectKind.Explosion);
        fx.Update(5);
        Assert.Empty(fx.Active);
        Assert.Equal(new[] { new Vec2(700, 700) }, fx.Craters);
    }

    [Fact]
    public void Loot_ShowsANoteOverTheLooter_ForAWhile()
    {
        var fx = new CombatEffects();
        var loot = new UnitLooted(0, new UnitId(1), new UnitId(2), 2, 0, null, []);
        fx.Add([loot], _ => true, e => $"note {e.Magazines}");
        var note = Assert.Single(fx.Notes);
        Assert.Equal(new UnitId(1), note.Unit);
        Assert.Equal("note 2", note.Text);
        fx.Update(CombatEffects.NoteSeconds + 0.1);
        Assert.Empty(fx.Notes);
    }

    [Fact]
    public void Loot_ByAHiddenMan_ShowsNoNote()
    {
        var fx = new CombatEffects();
        fx.Add([new UnitLooted(0, new UnitId(1), new UnitId(2), 2, 0, null, [])], _ => false, _ => "x");
        Assert.Empty(fx.Notes);
    }
}

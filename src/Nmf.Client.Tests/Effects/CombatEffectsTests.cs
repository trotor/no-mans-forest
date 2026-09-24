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
}

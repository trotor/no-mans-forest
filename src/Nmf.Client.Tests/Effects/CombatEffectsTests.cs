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

    private static SignalUnit? Units(UnitId id) => id.Value switch
    {
        1 => new SignalUnit(new Vec2(0, 0), Own: true, Shown: true),        // our rifleman
        2 => new SignalUnit(new Vec2(5000, 0), Own: false, Shown: true),    // a seen enemy
        3 => new SignalUnit(new Vec2(9000, 9000), Own: false, Shown: false), // a hidden enemy
        _ => null,
    };

    [Fact]
    public void Signals_ShotBySeenShooter_AtTheShooter_HiddenOne_WhereItStruck()
    {
        var fx = new CombatEffects();
        fx.AddSignals([new ShotFired(0, new UnitId(2), new Vec2(5000, 0), new Vec2(100, 0), null),
                       new ShotFired(0, new UnitId(3), new Vec2(9000, 9000), new Vec2(300, 300), null)], Units);
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.Gunfire && s.At == new Vec2(5000, 0));
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.Gunfire && s.At == new Vec2(300, 300));
        Assert.DoesNotContain(fx.Signals, s => s.At == new Vec2(9000, 9000)); // the hidden shooter is not given away
    }

    [Fact]
    public void Signals_BurstFire_IsOneSignalRefreshed()
    {
        var fx = new CombatEffects();
        for (int i = 0; i < 5; i++)
        {
            fx.AddSignals([new ShotFired(0, new UnitId(2), new Vec2(5000 + i * 100, 0), new Vec2(100, 0), null)], Units);
            fx.Update(0.2);
        }
        Assert.Single(fx.Signals, s => s.Kind == SignalKind.Gunfire);
        Assert.True(fx.Signals[0].Age < 0.3);
    }

    [Fact]
    public void Signals_Explosion_OwnHit_EnemyDown_AndTheyFade()
    {
        var fx = new CombatEffects();
        fx.AddSignals([new GrenadeExploded(0, 1, new Vec2(7000, 7000)),
                       new UnitWounded(0, new UnitId(1), Nmf.Sim.Combat.WoundLevel.Light),
                       new UnitWounded(0, new UnitId(2), Nmf.Sim.Combat.WoundLevel.Dead),
                       new UnitWounded(0, new UnitId(3), Nmf.Sim.Combat.WoundLevel.Dead)], Units);
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.Explosion && s.At == new Vec2(7000, 7000));
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.OwnHit && s.At == new Vec2(0, 0));
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.EnemyDown && s.At == new Vec2(5000, 0));
        Assert.DoesNotContain(fx.Signals, s => s.At == new Vec2(9000, 9000));
        fx.Update(CombatEffects.SignalSeconds + 0.1);
        Assert.Empty(fx.Signals);
    }
}

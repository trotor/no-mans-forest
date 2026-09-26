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

    private static readonly Vec2[] OwnMen = [new Vec2(0, 0)];

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
                       new ShotFired(0, new UnitId(3), new Vec2(9000, 9000), new Vec2(300, 300), null)], Units, OwnMen);
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.Gunfire && s.At == new Vec2(5000, 0));
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.Gunfire && s.At == new Vec2(300, 300));
        Assert.DoesNotContain(fx.Signals, s => s.At == new Vec2(9000, 9000)); // the hidden shooter is not given away
    }

    [Fact]
    public void Signals_TheEnemyCallingACounterattack_IsMarkedRoughly_WhenHeard()
    {
        var fx = new CombatEffects();
        fx.AddSignals([new CounterattackStarted(0, Side.Red, new UnitId(3), new Vec2(20_340, 10_720), new UnitId(1))], Units, OwnMen);
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.Shout && s.At == new Vec2(20_500, 10_500)); // heard: the 10 m square
        Assert.True(fx.Signals.Single(s => s.Kind == SignalKind.Shout).Lifetime >= 4); // a one-off call: it stays up a while
        var far = new CombatEffects();
        far.AddSignals([new CounterattackStarted(0, Side.Red, new UnitId(3), new Vec2(80_000, 0), new UnitId(1))], Units, OwnMen);
        Assert.Empty(far.Signals);
        var ours = new CombatEffects();
        ours.AddSignals([new CounterattackStarted(0, Side.Blue, new UnitId(1), new Vec2(100, 0), new UnitId(2))], Units, OwnMen);
        Assert.Empty(ours.Signals); // our own leader's call is not news
    }

    [Fact]
    public void Signals_BurstFire_IsOneSignalRefreshed()
    {
        var fx = new CombatEffects();
        for (int i = 0; i < 5; i++)
        {
            fx.AddSignals([new ShotFired(0, new UnitId(2), new Vec2(5000 + i * 100, 0), new Vec2(100, 0), null)], Units, OwnMen);
            fx.UpdateSignals(0.2);
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
                       new UnitWounded(0, new UnitId(3), Nmf.Sim.Combat.WoundLevel.Dead)], Units, OwnMen);
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.Explosion && s.At == new Vec2(7000, 7000));
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.OwnHit && s.At == new Vec2(0, 0));
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.EnemyDown && s.At == new Vec2(5000, 0));
        Assert.DoesNotContain(fx.Signals, s => s.At == new Vec2(9000, 9000));
        fx.UpdateSignals(CombatEffects.SignalSeconds + 0.1);
        Assert.Empty(fx.Signals);
    }

    [Fact]
    public void Signals_HiddenShooterFarFromOurMen_GivesNoSignal()
    {
        var fx = new CombatEffects();
        // His bullet stopped in a tree 1.5 m in front of him, 120 m from any of ours: nothing to show but the heard "?".
        fx.AddSignals([new ShotFired(0, new UnitId(3), new Vec2(9000, 9000), new Vec2(9150, 9000), null)], Units, OwnMen);
        Assert.Empty(fx.Signals);
    }

    [Fact]
    public void Signals_KeepPulsingUnderSustainedFire()
    {
        var fx = new CombatEffects();
        for (int i = 0; i < 10; i++)
        {
            fx.AddSignals([new ShotFired(0, new UnitId(2), new Vec2(5000, 0), new Vec2(100, 0), null)], Units, OwnMen);
            fx.UpdateSignals(0.1);
        }
        var ring = Assert.Single(fx.Signals);
        Assert.True(ring.Age < 0.15);
        Assert.True(ring.Pulse > 0.9); // the animation runs on even though the ring keeps being renewed
    }

    [Fact]
    public void Signals_HitsAndFallsAreNeverMerged()
    {
        var fx = new CombatEffects();
        SignalUnit? Two(UnitId id) => id.Value switch
        {
            1 => new SignalUnit(new Vec2(0, 0), true, true),
            4 => new SignalUnit(new Vec2(500, 0), true, true),
            _ => null,
        };
        fx.AddSignals([new UnitWounded(0, new UnitId(1), Nmf.Sim.Combat.WoundLevel.Light),
                       new UnitWounded(0, new UnitId(4), Nmf.Sim.Combat.WoundLevel.Light)], Two, OwnMen);
        Assert.Equal(2, fx.Signals.Count(s => s.Kind == SignalKind.OwnHit));
    }

    [Fact]
    public void Signals_AgeInGameTime_NotRealTime()
    {
        var fx = new CombatEffects();
        fx.AddSignals([new GrenadeExploded(0, 1, new Vec2(7000, 7000))], Units, OwnMen);
        fx.Update(5.0); // real seconds pass while the game is paused on the map
        Assert.Single(fx.Signals);
        fx.UpdateSignals(CombatEffects.SignalSeconds + 0.1);
        Assert.Empty(fx.Signals);
    }

    [Fact]
    public void OrderFlash_MarksTheOrderedMen_AndTheirTarget_ForAMoment()
    {
        var fx = new CombatEffects();
        fx.AddOrderFlash([new UnitId(1), new UnitId(2)], new Vec2(9000, 0), OrderFlashKind.Move);
        var flash = Assert.Single(fx.OrderFlashes);
        Assert.Equal(new[] { new UnitId(1), new UnitId(2) }, flash.Units);
        Assert.Equal(new Vec2(9000, 0), flash.Target);
        Assert.Equal(OrderFlashKind.Move, flash.Kind);
        fx.UpdateSignals(10); // game time does not age it: orders given while paused still flash
        Assert.Single(fx.OrderFlashes);
        fx.Update(CombatEffects.OrderFlashSeconds + 0.1);
        Assert.Empty(fx.OrderFlashes);
    }

    [Fact]
    public void OrderFlash_ANewOrderReplacesTheLastOne()
    {
        var fx = new CombatEffects();
        fx.AddOrderFlash([new UnitId(1)], new Vec2(9000, 0), OrderFlashKind.Move);
        fx.AddOrderFlash([new UnitId(1)], null, OrderFlashKind.Stance);
        var flash = Assert.Single(fx.OrderFlashes);
        Assert.Null(flash.Target);
        Assert.Equal(OrderFlashKind.Stance, flash.Kind);
    }
}

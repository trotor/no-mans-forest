using Nmf.Sim.AI;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

/// <summary>Spec 2026-09-26-experience-prone-design: experience helps an attack; a man lying down shoots worse.</summary>
public class ExperienceAndProneTests
{
    private static (Simulation Sim, Unit Shooter, Unit Target) Setup()
    {
        var sim = new Simulation(new GridMap(60, 20, ["none"]), 3);
        var shooter = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle());
        var target = sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        return (sim, shooter, target);
    }

    [Fact]
    public void LyingDown_AMan_ShootsNoBetterThanStanding_AndWorseThanKneeling()
    {
        Assert.Equal(100, CombatRules.StanceSpreadPct(Stance.Prone));
        Assert.True(CombatRules.StanceSpreadPct(Stance.Crouching) < CombatRules.StanceSpreadPct(Stance.Prone));
    }

    [Fact]
    public void LyingDown_AVeteranStillShootsWell_ARecruitHuggingTheGroundWorse()
    {
        var (_, man, _) = Setup();
        man.Stance = Stance.Prone;
        Assert.Equal(100, CombatRules.StanceSpreadPct(man));
        man.Experience = 90;
        Assert.Equal(60, CombatRules.StanceSpreadPct(man));
        man.Experience = 20;
        Assert.Equal(130, CombatRules.StanceSpreadPct(man));
        man.Stance = Stance.Crouching;
        Assert.Equal(80, CombatRules.StanceSpreadPct(man)); // kneeling is the same for all
    }

    [Fact]
    public void FiringPosition_AVeteranLiesDown_WhereHeShootsBestThatWay()
    {
        var (sim, shooter, target) = Setup();
        shooter.Experience = 90;
        SoldierBrain.TakeFiringStance(sim, shooter, target.Position);
        Assert.Equal(Stance.Prone, shooter.TargetStance);
    }

    [Fact]
    public void LyingDown_AMan_TakesLongerToAim()
    {
        var (_, shooter, target) = Setup();
        Firing.StartAiming(shooter, target);
        int standing = shooter.ActionTicksLeft;
        shooter.Stance = Stance.Prone;
        Firing.StartAiming(shooter, target);
        Assert.Equal(standing * CombatRules.ProneAimPct / 100, shooter.ActionTicksLeft);
        Assert.True(CombatRules.ProneAimPct > 100);
    }

    [Fact]
    public void FiringPosition_Kneels_WhenHeCanSeeFromThere_UnlessHeIsUnderHeavyFire()
    {
        var (sim, shooter, target) = Setup();
        SoldierBrain.TakeFiringStance(sim, shooter, target.Position);
        Assert.Equal(Stance.Crouching, shooter.TargetStance);

        var (sim2, shooter2, target2) = Setup();
        shooter2.Suppression = MoraleSystem.GoProneAt(shooter2);
        SoldierBrain.TakeFiringStance(sim2, shooter2, target2.Position);
        Assert.Equal(Stance.Prone, shooter2.TargetStance);
    }

    private static (Simulation Sim, Unit Man) DownedAfterFire(int experience, bool ordered = false)
    {
        var (sim, man, _) = Setup();
        man.Experience = experience;
        sim.Submit(Side.Blue, new SetFirePolicyOrder(man.Id, FirePolicy.HoldFire));
        if (ordered)
            sim.Submit(Side.Blue, new SetStanceOrder(man.Id, Stance.Prone));
        man.Stance = Stance.Prone; // he went down under a burst; the fire has since died away
        man.Suppression = 0;
        for (int i = 0; i < 40; i++) sim.Step();
        return (sim, man);
    }

    [Fact]
    public void WhenTheFireDiesDown_AManWhoWentDown_KneelsAgainToShoot()
    {
        var (_, man) = DownedAfterFire(50);
        Assert.Equal(Stance.Crouching, man.Stance);
    }

    [Fact]
    public void WhenTheFireDiesDown_AVeteranStaysDown_AndSoDoesAManToldToLieDown()
    {
        Assert.Equal(Stance.Prone, DownedAfterFire(90).Man.Stance);
        Assert.Equal(Stance.Prone, DownedAfterFire(50, ordered: true).Man.Stance);
    }

    [Fact]
    public void Veteran_OnTheMove_IsHarderToHit()
    {
        var (_, _, target) = Setup();
        target.MoveTarget = new Vec2(4050, 1050);
        target.MoveMode = MoveMode.Run;
        Assert.Equal(200, CombatRules.TargetMovingSpreadPct(target));
        target.Experience = 90;
        Assert.Equal(240, CombatRules.TargetMovingSpreadPct(target));
        target.Experience = 10;
        Assert.Equal(160, CombatRules.TargetMovingSpreadPct(target));
        target.MoveTarget = null;
        target.Experience = 90;
        Assert.Equal(100, CombatRules.TargetMovingSpreadPct(target));
    }

    [Fact]
    public void Veteran_GetsDownFaster_ARecruitSlower()
    {
        var (_, shooter, _) = Setup();
        Movement.BeginStanceChange(shooter, Stance.Prone);
        int average = shooter.StanceTicksLeft;
        var (_, veteran, _) = Setup();
        veteran.Experience = 90;
        Movement.BeginStanceChange(veteran, Stance.Prone);
        var (_, recruit, _) = Setup();
        recruit.Experience = 10;
        Movement.BeginStanceChange(recruit, Stance.Prone);
        Assert.True(veteran.StanceTicksLeft < average, $"{veteran.StanceTicksLeft} vs {average}");
        Assert.True(recruit.StanceTicksLeft > average, $"{recruit.StanceTicksLeft} vs {average}");
    }

    [Fact]
    public void VeteransCoveringFire_KeepsHeadsDownBetter()
    {
        var (_, shooter, _) = Setup();
        Assert.Equal(100, CombatRules.SuppressionPct(shooter));
        shooter.Experience = 90;
        Assert.Equal(100, CombatRules.SuppressionPct(shooter)); // only while giving covering fire in an attack
        shooter.AttackRole = AttackRole.Covering;
        Assert.Equal(140, CombatRules.SuppressionPct(shooter));
        shooter.Experience = 20;
        Assert.Equal(70, CombatRules.SuppressionPct(shooter));
    }

    [Fact]
    public void Veterans_GoInSooner_OnAHalfSuppressedEnemy()
    {
        bool GoesIn(int experience)
        {
            var sim = new Simulation(new GridMap(100, 220, ["none"]), 1);
            var blues = Enumerable.Range(0, 4).Select(i => sim.SpawnUnit(Side.Blue, new Vec2(4050 + i * 300, 20050), 8, TestWeapons.Rifle(lethality: 0))).ToList();
            var red = sim.SpawnUnit(Side.Red, new Vec2(4500, 20050 - 2200), 8);
            foreach (var b in blues)
            {
                b.Experience = experience;
                sim.Submit(Side.Blue, new SetFirePolicyOrder(b.Id, FirePolicy.HoldFire));
            }
            for (int i = 0; i < 30; i++) sim.Step();
            foreach (var b in blues)
                sim.Submit(Side.Blue, new AttackOrder(b.Id, red.Id));
            for (int i = 0; i < 5; i++)
            {
                red.Suppression = 170;
                sim.Step();
            }
            return blues.Any(b => b.AssaultTarget == red.Id);
        }
        Assert.False(GoesIn(50));
        Assert.True(GoesIn(90));
    }
}

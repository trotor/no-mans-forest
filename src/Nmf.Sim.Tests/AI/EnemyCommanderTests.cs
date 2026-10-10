using Nmf.Sim.AI;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Mission;
using Nmf.Sim.Orders;
using Nmf.Sim.Tactics;
using Nmf.Sim.Tests.Tactics;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

/// <summary>Spec 2026-09-26-enemy-initiative-design: the enemy commander counterattacks, scouts and returns to his post.</summary>
[Collection(Nmf.Sim.Tests.TimingCollection.Name)]
public class EnemyCommanderTests
{
    private sealed class Fight
    {
        public required Simulation Sim { get; init; }
        public required EnemyCommander Commander { get; init; }
        public required List<Unit> Reds { get; init; }
        public required List<Unit> Blues { get; init; }
        public List<SimEvent> Events { get; } = [];

        /// <summary>
        /// Steps the fight. The Finns stand their ground: what the step does to them (wounds from fire that cannot kill,
        /// fear, running) is undone, so the counterattack goes on against them; what a test does to them stays.
        /// </summary>
        public void Run(int ticks, Action? everyTick = null)
        {
            for (int i = 0; i < ticks; i++)
            {
                everyTick?.Invoke();
                var wounds = Blues.Select(b => b.Wound).ToList();
                Commander.Tick(Sim);
                Events.AddRange(Sim.Step());
                for (int b = 0; b < Blues.Count; b++)
                {
                    var blue = Blues[b];
                    if (blue.Wound > wounds[b])
                        blue.Wound = wounds[b];
                    if (blue.IsOutOfAction)
                        continue;
                    blue.MoraleState = MoraleState.Steady;
                    Movement.ClearPath(blue);
                }
            }
        }

        public PlannedAttack? RedAttack => Sim.PlannedAttacks.FirstOrDefault(a => a.Side == Side.Red && !a.Ended);
    }

    /// <summary>Their weapons cannot hurt: the fire team's fire on the Finn's position must not end the attack it is part of.</summary>
    private static readonly WeaponDef Lmg =
        new("test_lmg", "Test LMG", WeaponClass.Lmg, 20, 8, 5, 2, 6, 30, 12, 40_000, 0, 100, 40_000);

    /// <summary>
    /// A Soviet post in a row on open ground (leader first, then optionally a machine gunner) and unarmed Finns in a row
    /// <paramref name="blueDistanceM"/> south of it. Everyone holds fire, so nothing but the commander's orders moves anyone.
    /// Woods 70–150 m down the map west of 45 m and between 85 and 110 m: somewhere for a strike team to go round unseen.
    /// </summary>
    private static Fight Setup(int reds = 5, int blues = 1, bool counterattack = true, bool investigate = true, int blueDistanceM = 60,
        bool lmg = true, GridMap? map = null, bool engaged = true, int patrolling = -1, int reserve = 0, bool woods = true, bool tactics = true)
    {
        map ??= new GridMap(160, 200, ["none"]);
        if (woods)
            for (int y = 70; y < 150; y++)
                for (int x = 0; x < 160; x++)
                    if (x < 45 || x is >= 85 and < 110)
                    {
                        ref var cell = ref map[new CellCoord(x, y)];
                        cell.ConcealmentPerM = Math.Max(cell.ConcealmentPerM, (byte)60);
                        cell.ObstacleHeightCm = Math.Max(cell.ObstacleHeightCm, (short)1500);
                    }
        var sim = new Simulation(map, 1);
        var redList = Enumerable.Range(0, reds).Select(i =>
            sim.SpawnUnit(Side.Red, new Vec2(6050 + i * 300, 5050), 8, lmg && i == 1 ? Lmg : TestWeapons.Rifle(lethality: 0), isLeader: i == 0)).ToList();
        // A reserve squad 30 m behind the post (away from the Finns): its leader first.
        redList.AddRange(Enumerable.Range(0, reserve).Select(i =>
        {
            var man = sim.SpawnUnit(Side.Red, new Vec2(6050 + i * 300, 2050), 8, TestWeapons.Rifle(lethality: 0), isLeader: i == 0);
            man.Squad = 1;
            return man;
        }));
        var blueList = Enumerable.Range(0, blues).Select(i =>
            sim.SpawnUnit(Side.Blue, new Vec2(6050 + i * 400, 5050 + blueDistanceM * 100), 8)).ToList();
        foreach (var red in redList)
            sim.Submit(Side.Red, new SetFirePolicyOrder(red.Id, FirePolicy.HoldFire));
        if (engaged)
            redList[reds - 1].LastSuppressedTick = 0; // the fight has been on: they have been under fire
        var commander = new EnemyCommander(Side.Red, new EnemyAiSpec(counterattack, investigate), redList,
            patrolling >= 0 ? [redList[patrolling].Id] : null,
            tactics ? [TacticPlannerTests.FireAndMovement, TacticPlannerTests.LongFire] : null);
        return new Fight { Sim = sim, Commander = commander, Reds = redList, Blues = blueList };
    }

    [Fact]
    public void NotAllowedByTheMission_TheyNeverAttackNorScout()
    {
        var fight = Setup(counterattack: false, investigate: false);
        fight.Run(1200);
        Assert.Null(fight.RedAttack);
        Assert.All(fight.Reds, r => Assert.Null(r.MoveTarget));
    }

    [Fact]
    public void Outnumbering_AQuietEnemy_TheyCounterattack_TheMachineGunStaysToGiveFire()
    {
        var fight = Setup();
        fight.Run(900);
        var attack = fight.RedAttack;
        Assert.NotNull(attack);
        Assert.Equal(fight.Blues[0].Id, attack!.Plan.Target);
        Assert.Equal([fight.Reds[1].Id], attack.FireTeam); // the machine gun gives the fire from where it is
        Assert.Equal(fight.Reds[1].Position, attack.Plan.FireTeam[0].At);
        Assert.Equal(4, attack.StrikeTeam.Count());
        var shout = Assert.Single(fight.Events.OfType<CounterattackStarted>());
        Assert.Equal(fight.Reds[0].Id, shout.Leader);
        Assert.Equal(Side.Red, shout.Side);
    }

    [Fact]
    public void AtFirstSight_TheyHoldTheirPostAndWatch()
    {
        var fight = Setup(engaged: false);
        fight.Run(1200);
        Assert.Null(fight.RedAttack);
    }

    [Fact]
    public void TheyWatchHalfAMinuteBeforeGoingIn()
    {
        var fight = Setup();
        fight.Run(EnemyCommander.WatchTicks - 100);
        Assert.Null(fight.RedAttack);
        fight.Run(300);
        Assert.NotNull(fight.RedAttack);
    }

    [Fact]
    public void MenHeardButNotSeen_CountAgainstTheOdds()
    {
        var map = new GridMap(160, 200, ["none"]);
        for (int y = 180; y < 200; y++)
            for (int x = 0; x < 160; x++)
                map[new CellCoord(x, y)] = new CellData(0, 1500, 255, 26, 0); // a thicket they cannot see into
        var fight = Setup(reds: 4, lmg: false, map: map); // 4 against the 1 they see: enough ...
        foreach (var i in Enumerable.Range(0, 2))
        {
            var heard = fight.Sim.SpawnUnit(Side.Blue, new Vec2(1050 + i * 300, 19_050), 8);
            var contact = fight.Sim.Knowledge(Side.Red).GetOrAdd(heard.Id);
            contact.Level = ContactLevel.Suspected;
            contact.Position = heard.Position;
        }
        fight.Run(900, () =>
        {
            foreach (var c in fight.Sim.Knowledge(Side.Red).Contacts.Where(c => c.Level == ContactLevel.Suspected))
                c.LastUpdateTick = fight.Sim.Tick; // ... but two more keep firing somewhere out of sight
        });
        Assert.Null(fight.RedAttack);
    }

    [Fact]
    public void WithAReserve_TheReserveCounterattacks_WhileThePostHoldsAndGivesFire()
    {
        var fight = Setup(reserve: 4);
        fight.Run(900);
        var attack = fight.RedAttack;
        Assert.NotNull(attack);
        Assert.Equal(fight.Reds.Skip(5).Select(r => r.Id).OrderBy(id => id.Value), attack!.StrikeTeam.OrderBy(id => id.Value));
        Assert.Equal(fight.Reds.Take(5).Select(r => r.Id).OrderBy(id => id.Value), attack.FireTeam.OrderBy(id => id.Value)); // the post holds
        Assert.Equal(fight.Reds[5].Id, fight.Events.OfType<CounterattackStarted>().Single().Leader); // the reserve's leader calls it
    }

    [Fact]
    public void ThePostCannotSeeHim_NoCounterattack_NoShoutAgainAndAgain()
    {
        var map = new GridMap(160, 200, ["none"]);
        for (int y = 100; y < 130; y++)
            for (int x = 0; x < 160; x++)
                map[new CellCoord(x, y)] = new CellData(0, 1500, 255, 26, 0);
        var fight = Setup(reserve: 4, map: map, blueDistanceM: 60); // the Finn is in the thicket, 60 m south
        var contact = fight.Sim.Knowledge(Side.Red).GetOrAdd(fight.Blues[0].Id);
        contact.Level = ContactLevel.LastKnown;
        contact.Position = fight.Blues[0].Position;
        fight.Run(5_000, () => contact.LastUpdateTick = fight.Sim.Tick); // glimpses of him, on and off
        // A post that cannot see him cannot keep him down, so the reserve would never go in: they do not start it.
        Assert.Empty(fight.Events.OfType<CounterattackStarted>());
        Assert.Null(fight.RedAttack);
    }

    [Fact]
    public void WithAOneManReserve_ThePostGoesIn_ButItsMachineGunStays()
    {
        var fight = Setup(reserve: 1);
        fight.Run(900);
        Assert.NotNull(fight.RedAttack);
        Assert.DoesNotContain(fight.Reds[1].Id, fight.RedAttack!.StrikeTeam);
        Assert.Contains(fight.Reds[1].Id, fight.RedAttack.FireTeam);
    }

    [Fact]
    public void UnderFire_TheyWait()
    {
        var fight = Setup();
        fight.Run(1200, () => fight.Reds[3].LastSuppressedTick = fight.Sim.Tick);
        Assert.Null(fight.RedAttack);
    }

    [Fact]
    public void Outnumbered_TheyHoldTheirPost()
    {
        var fight = Setup(reds: 4, blues: 5, lmg: false);
        fight.Run(1200);
        Assert.Null(fight.RedAttack);
    }

    [Fact]
    public void EvenOdds_WithOneOfThemSeenDown_TheyGoForIt()
    {
        var fight = Setup(reds: 4, blues: 5, lmg: false);
        fight.Blues[4].Wound = WoundLevel.Dead;
        fight.Run(1200);
        Assert.NotNull(fight.RedAttack);
    }

    [Fact]
    public void WithNoLeader_TheyHold()
    {
        var fight = Setup();
        fight.Reds[0].IsLeader = false;
        fight.Run(1200);
        Assert.Null(fight.RedAttack);
    }

    [Fact]
    public void TheyGoForTheWeakest_AWoundedManBeforeTheNearest()
    {
        var fight = Setup(blues: 2, blueDistanceM: 50);
        fight.Blues[1].Position = new Vec2(6450, 5050 + 9000); // 90 m, but wounded
        fight.Blues[1].Wound = WoundLevel.Light;
        fight.Run(900);
        Assert.Equal(fight.Blues[1].Id, fight.RedAttack?.Plan.Target);
    }

    [Fact]
    public void AfterACounterattack_TheyWaitAMinuteBeforeTheNext()
    {
        var map = new GridMap(160, 200, ["none"]);
        for (int y = 70; y < 100; y++)
            for (int x = 148; x < 160; x++)
            {
                ref var cell = ref map[new CellCoord(x, y)];
                cell.ConcealmentPerM = 60; // a copse south-east of him: a way round to him too
                cell.ObstacleHeightCm = 1500;
            }
        var fight = Setup(blues: 2, map: map);
        fight.Blues[1].Position = new Vec2(14_050, 6050); // 75 m east of the post in the open, 90 m from the first: not part of his position
        fight.Blues[1].Wound = WoundLevel.Light;                  // so he is the first one they go for
        fight.Run(900);
        Assert.Equal(fight.Blues[1].Id, fight.RedAttack?.Plan.Target);
        fight.Blues[1].Wound = WoundLevel.Dead;
        fight.Run(100);
        Assert.Null(fight.RedAttack);
        fight.Run(EnemyCommander.CooldownTicks - 200);
        Assert.Null(fight.RedAttack);
        fight.Run(400);
        Assert.Equal(fight.Blues[0].Id, fight.RedAttack?.Plan.Target);
    }

    [Fact]
    public void HearingShotsFromOutOfSight_TwoMenSneakOutToLook_TheLeaderStays()
    {
        var map = new GridMap(160, 200, ["none"]);
        for (int y = 100; y < 120; y++)
            for (int x = 0; x < 160; x++)
                map[new CellCoord(x, y)] = new CellData(0, 1500, 255, 26, 0); // a thicket they cannot see into
        var fight = Setup(blueDistanceM: 60, map: map, counterattack: false);
        var heard = fight.Sim.Knowledge(Side.Red).GetOrAdd(fight.Blues[0].Id);
        heard.Level = ContactLevel.Suspected;
        heard.Position = new Vec2(6500, 11_500);
        heard.LastUpdateTick = 0;
        fight.Run(45);
        var scouts = fight.Reds.Where(r => r.MoveTarget is not null).ToList();
        Assert.Equal(2, scouts.Count);
        Assert.All(scouts, s => Assert.Equal(MoveMode.Sneak, s.MoveMode));
        Assert.DoesNotContain(fight.Reds[0], scouts); // the leader
        Assert.DoesNotContain(fight.Reds[1], scouts); // the machine gunner
    }

    [Fact]
    public void AManOnPatrol_KeepsToHisRound_AndIsNotSentIntoTheAttack()
    {
        var fight = Setup(patrolling: 4);
        fight.Run(900);
        Assert.NotNull(fight.RedAttack);
        Assert.DoesNotContain(fight.Reds[4].Id, fight.RedAttack!.Members);
    }

    [Fact]
    public void ScoutsWhoRunIntoTheEnemy_StopToFight_InsteadOfWalkingUpToHim()
    {
        var map = new GridMap(160, 200, ["none"]);
        for (int y = 150; y < 170; y++)
            for (int x = 0; x < 160; x++)
                map[new CellCoord(x, y)] = new CellData(0, 1500, 255, 26, 0);
        var fight = Setup(blueDistanceM: 110, map: map, counterattack: false); // the Finn hides in the thicket
        var heard = fight.Sim.Knowledge(Side.Red).GetOrAdd(fight.Blues[0].Id);
        heard.Level = ContactLevel.Suspected;
        heard.Position = new Vec2(6500, 18_500);
        heard.LastUpdateTick = 0;
        fight.Run(45);
        var scouts = fight.Reds.Where(r => r.MoveTarget is not null).ToList();
        Assert.Equal(2, scouts.Count);
        fight.Sim.SpawnUnit(Side.Blue, new Vec2(7550, 8050), 8); // another Finn, in the open ahead of them
        fight.Run(120);
        Assert.All(scouts, s => Assert.Null(s.MoveTarget));
    }

    [Fact]
    public void AManWhoseWayHomeWasCutShort_StillGetsHome()
    {
        var fight = Setup();
        var home = fight.Reds.ToDictionary(r => r.Id, r => r.Position);
        fight.Run(900);
        fight.Run(200);
        fight.Blues[0].Wound = WoundLevel.Dead;
        var walker = fight.Reds[2];
        bool cut = false;
        fight.Run(EnemyCommander.ReturnQuietTicks + 2000, () =>
        {
            if (!cut && walker.MoveTarget == home[walker.Id])
            {
                Movement.ClearPath(walker); // he dived for cover on the way
                cut = true;
            }
        });
        Assert.True(cut);
        Assert.True((walker.Position - home[walker.Id]).LengthSquared <= 1000L * 1000, $"{walker.Position} vs {home[walker.Id]}");
    }

    [Fact]
    public void WhenItIsOver_TheyGoBackToTheirPost()
    {
        var fight = Setup();
        var home = fight.Reds.ToDictionary(r => r.Id, r => r.Position);
        fight.Run(900);
        Assert.NotNull(fight.RedAttack);
        fight.Run(200); // they are on their way
        fight.Blues[0].Wound = WoundLevel.Dead;
        fight.Run(EnemyCommander.ReturnQuietTicks + 2000);
        Assert.All(fight.Reds, r => Assert.True((r.Position - home[r.Id]).LengthSquared <= 1000L * 1000,
            $"{r.Id} at {r.Position}, home {home[r.Id]}"));
        Assert.All(fight.Reds, r => Assert.Null(r.AreaTarget)); // the fire on the position stopped with the attack
    }

    [Fact]
    public void NoWayRound_TheyHold_AndDoNotKeepTrying()
    {
        var fight = Setup(woods: false); // bare ground: nowhere to go round unseen
        fight.Run(2400);
        Assert.Null(fight.RedAttack);
        Assert.DoesNotContain(fight.Sim.OrderLog, o => o.Order is PlannedAttackOrder);
        Assert.Empty(fight.Events.OfType<CounterattackStarted>());
    }

    [Fact]
    public void WithoutTactics_NoCounterattack()
    {
        var fight = Setup(tactics: false);
        fight.Run(1200);
        Assert.Null(fight.RedAttack);
    }

    [Fact]
    public void AfterAHalt_TheyWaitAMinuteBeforeTheNext()
    {
        var fight = Setup();
        fight.Run(900);
        var attack = fight.RedAttack!;
        foreach (var id in attack.StrikeTeam.Where(id => fight.Sim.FindUnit(id)!.IsLeader == false).Take(2).ToList())
            fight.Sim.FindUnit(id)!.Wound = WoundLevel.Dead; // losses: they call it off
        fight.Run(100);
        Assert.Contains(fight.Events.OfType<PlannedAttackHalted>(), h => h.Side == Side.Red);
        Assert.Null(fight.RedAttack);
        fight.Run(EnemyCommander.CooldownTicks - 300);
        Assert.Null(fight.RedAttack);
    }

    [Fact]
    public void Alertness_OnlyTheAwakeGoIntoTheCounterattack()
    {
        var fight = Setup();
        fight.Sim.EnableAlertness(Side.Red);
        fight.Run(900, () =>
        {
            for (int i = 0; i < fight.Reds.Count; i++)
                fight.Reds[i].Alertness = i == 4 ? Alertness.Calm : Alertness.Combat; // one of them has heard nothing
        });
        var attack = fight.RedAttack;
        Assert.True(attack is not null, string.Join("\n", fight.Events.Where(e => e is not Nmf.Sim.Events.UnitMoved && e is not Nmf.Sim.Events.ShotFired && e is not Nmf.Sim.Events.AlertnessChanged).Take(80)));
        var party = attack!.StrikeTeam.Concat(attack.FireTeam).ToList();
        Assert.Contains(fight.Reds[3].Id, party);
        Assert.DoesNotContain(fight.Reds[4].Id, party);
    }

    [Fact]
    public void Alertness_CalmMenAreNotSentToLook_WaryOnesAre()
    {
        var map = new GridMap(160, 200, ["none"]);
        for (int y = 100; y < 120; y++)
            for (int x = 0; x < 160; x++)
                map[new CellCoord(x, y)] = new CellData(0, 1500, 255, 26, 0);
        var fight = Setup(blueDistanceM: 60, map: map, counterattack: false);
        fight.Sim.EnableAlertness(Side.Red);
        var heard = fight.Sim.Knowledge(Side.Red).GetOrAdd(fight.Blues[0].Id);
        heard.Level = ContactLevel.Suspected;
        heard.Position = new Vec2(6500, 11_500);
        heard.LastUpdateTick = 0;
        fight.Run(45, () => fight.Reds.ForEach(r => r.Alertness = Alertness.Calm));
        Assert.All(fight.Reds, r => Assert.Null(r.MoveTarget));
        fight.Run(45, () => fight.Reds.ForEach(r => r.Alertness = r == fight.Reds[4] ? Alertness.Suspicious : Alertness.Calm));
        Assert.Equal([fight.Reds[4]], fight.Reds.Where(r => r.MoveTarget is not null).ToList());
    }
}

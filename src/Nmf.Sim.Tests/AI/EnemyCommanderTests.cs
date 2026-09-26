using Nmf.Sim.AI;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Mission;
using Nmf.Sim.Orders;
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

        public void Run(int ticks, Action? everyTick = null)
        {
            for (int i = 0; i < ticks; i++)
            {
                everyTick?.Invoke();
                Commander.Tick(Sim);
                Events.AddRange(Sim.Step());
            }
        }

        public AttackGroup? RedAttack => Sim.AttackGroups.FirstOrDefault(g => g.Side == Side.Red && !g.Ended);
    }

    private static readonly WeaponDef Lmg =
        new("test_lmg", "Test LMG", WeaponClass.Lmg, 20, 8, 5, 2, 6, 30, 12, 40_000, 65, 100, 40_000);

    /// <summary>
    /// A Soviet post in a row on open ground (leader first, then optionally a machine gunner) and unarmed Finns in a row
    /// <paramref name="blueDistanceM"/> south of it. Everyone holds fire, so nothing but the commander's orders moves anyone.
    /// </summary>
    private static Fight Setup(int reds = 5, int blues = 1, bool counterattack = true, bool investigate = true, int blueDistanceM = 60,
        bool lmg = true, GridMap? map = null, bool engaged = true, int patrolling = -1, int reserve = 0)
    {
        var sim = new Simulation(map ?? new GridMap(160, 200, ["none"]), 1);
        var redList = Enumerable.Range(0, reds).Select(i =>
            sim.SpawnUnit(Side.Red, new Vec2(6050 + i * 300, 5050), 8, lmg && i == 1 ? Lmg : TestWeapons.Rifle(), isLeader: i == 0)).ToList();
        // A reserve squad 30 m behind the post (away from the Finns): its leader first.
        redList.AddRange(Enumerable.Range(0, reserve).Select(i =>
        {
            var man = sim.SpawnUnit(Side.Red, new Vec2(6050 + i * 300, 2050), 8, TestWeapons.Rifle(), isLeader: i == 0);
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
            patrolling >= 0 ? [redList[patrolling].Id] : null);
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
        Assert.Equal(fight.Blues[0].Id, attack!.Target);
        Assert.DoesNotContain(fight.Reds[1].Id, attack.Members);
        Assert.Equal(4, attack.Members.Count);
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
        var fight = Setup(reds: 3, lmg: false, map: map); // 3 against the 1 they see: enough ...
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
        Assert.Equal(fight.Reds.Skip(5).Select(r => r.Id).OrderBy(id => id.Value), attack!.Members.OrderBy(id => id.Value));
        Assert.Equal(fight.Reds[5].Id, fight.Events.OfType<CounterattackStarted>().Single().Leader); // the reserve's leader calls it
    }

    [Fact]
    public void ThePost_FiresAtWhereAHiddenTargetWasLastSeen()
    {
        var map = new GridMap(160, 200, ["none"]);
        for (int y = 100; y < 130; y++)
            for (int x = 0; x < 160; x++)
                map[new CellCoord(x, y)] = new CellData(0, 1500, 255, 26, 0);
        var fight = Setup(reserve: 4, map: map, blueDistanceM: 60); // the Finn is in the thicket, 60 m south
        var spot = fight.Blues[0].Position;
        var contact = fight.Sim.Knowledge(Side.Red).GetOrAdd(fight.Blues[0].Id);
        contact.Level = ContactLevel.LastKnown;
        contact.Position = spot;
        fight.Run(EnemyCommander.WatchTicks + 2, () => contact.LastUpdateTick = fight.Sim.Tick); // glimpses of him
        Assert.NotNull(fight.RedAttack); // (their fire into the thicket may soon get him)
        Assert.All(fight.Reds.Take(5).Where(r => r.Magazines > 0), r => Assert.Equal(spot, r.AreaTarget));
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
        var fight = Setup(reds: 2, blues: 3, lmg: false);
        fight.Run(1200);
        Assert.Null(fight.RedAttack);
    }

    [Fact]
    public void EvenOdds_WithOneOfThemSeenDown_TheyGoForIt()
    {
        var fight = Setup(reds: 2, blues: 3, lmg: false);
        fight.Blues[2].Wound = WoundLevel.Dead;
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
        Assert.Equal(fight.Blues[1].Id, fight.RedAttack?.Target);
    }

    [Fact]
    public void AfterACounterattack_TheyWaitAMinuteBeforeTheNext()
    {
        var fight = Setup(blues: 2);
        fight.Blues[1].Position = new Vec2(13_050, 5050 + 6000); // 70 m east of the first: not part of his position
        fight.Blues[1].Wound = WoundLevel.Light;                  // so he is the first one they go for
        fight.Run(900);
        Assert.Equal(fight.Blues[1].Id, fight.RedAttack?.Target);
        fight.Blues[1].Wound = WoundLevel.Dead;
        fight.Run(100);
        Assert.Null(fight.RedAttack);
        fight.Run(EnemyCommander.CooldownTicks - 200);
        Assert.Null(fight.RedAttack);
        fight.Run(400);
        Assert.Equal(fight.Blues[0].Id, fight.RedAttack?.Target);
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
        fight.Sim.SpawnUnit(Side.Blue, new Vec2(9050, 8050), 8); // another Finn, in the open ahead of them
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
    }
}

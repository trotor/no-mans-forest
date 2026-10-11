using Nmf.Client.Campaign;
using Nmf.Content.Campaigns;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Mission;
using Nmf.Sim.Units;

namespace Nmf.Client.Tests.Campaign;

/// <summary>Experience points from what the men did in the mission (spec 2026-10-11-campaign-design §9a, "Tehtävän jälkeen").</summary>
public class CampaignTallyTests
{
    private static readonly UnitId Korpela = new(1), Makinen = new(2), Belov = new(10), Orlov = new(11);

    private static readonly Dictionary<UnitId, CampaignTally.Seen> Field = new()
    {
        [Korpela] = new(Side.Blue, "Alik. Korpela", new Vec2(0, 0)),
        [Makinen] = new(Side.Blue, "Sotm. Mäkinen", new Vec2(500, 0)),
        [Belov] = new(Side.Red, "Serž. Belov", new Vec2(10_000, 10_000)),
        [Orlov] = new(Side.Red, "Kr-ts Orlov", new Vec2(10_300, 10_000)),
    };

    private static void Step(CampaignTally tally, IReadOnlyList<SimEvent> events, params (int Id, UnitId Thrower, int BlastCm)[] live) =>
        tally.Observe(events, id => Field.TryGetValue(id, out var seen) ? seen : null, live);

    private static ShotFired Shot(UnitId shooter, UnitId? hit) => new(0, shooter, default, default, hit);

    [Fact]
    public void ShotHitAndKill_GoToTheShooter()
    {
        var tally = new CampaignTally(Side.Blue);
        Step(tally, [Shot(Korpela, Belov), new UnitWounded(0, Belov, WoundLevel.Light), Shot(Korpela, null)]);
        Step(tally, [Shot(Makinen, Belov), new UnitWounded(1, Belov, WoundLevel.Dead)]);
        Assert.Equal((1, 0), tally.Of("Alik. Korpela"));
        Assert.Equal((1, 1), tally.Of("Sotm. Mäkinen")); // the last to hit him
    }

    [Fact]
    public void Bleeding_KeepsTheLastHitter()
    {
        var tally = new CampaignTally(Side.Blue);
        Step(tally, [Shot(Korpela, Belov), new UnitWounded(0, Belov, WoundLevel.Serious)]);
        Step(tally, [new UnitWounded(900, Belov, WoundLevel.Incapacitated)]); // bled out of the fight
        Assert.Equal((1, 1), tally.Of("Alik. Korpela"));
    }

    [Fact]
    public void GrenadeKill_ToTheThrower_ByItsId()
    {
        var tally = new CampaignTally(Side.Blue);
        Step(tally, [], (7, Makinen, 1_000));
        Step(tally, [new GrenadeExploded(5, 7, new Vec2(10_100, 10_000)), new UnitWounded(5, Belov, WoundLevel.Dead), new UnitWounded(5, Orlov, WoundLevel.Light)]);
        Assert.Equal((2, 1), tally.Of("Sotm. Mäkinen"));
    }

    [Fact]
    public void Grenade_LaterWoundOutOfItsReach_IsNotItsDoing()
    {
        // A man bleeding out elsewhere later in the same step is not the grenade's.
        var tally = new CampaignTally(Side.Blue);
        Step(tally, [Shot(Korpela, Orlov), new UnitWounded(0, Orlov, WoundLevel.Serious)], (7, Makinen, 100));
        Step(tally, [new GrenadeExploded(5, 7, new Vec2(10_000, 10_000)), new UnitWounded(5, Belov, WoundLevel.Dead),
            new UnitWounded(5, Orlov, WoundLevel.Incapacitated)]);
        Assert.Equal((1, 1), tally.Of("Sotm. Mäkinen"));
        Assert.Equal((1, 1), tally.Of("Alik. Korpela"));
    }

    [Fact]
    public void MortarKill_NoCredit()
    {
        var tally = new CampaignTally(Side.Blue);
        Step(tally, [Shot(Korpela, Belov), new UnitWounded(0, Belov, WoundLevel.Light)]);
        Step(tally, [new ShellExploded(5, Side.Blue, new Vec2(10_000, 10_100), false), new UnitWounded(5, Belov, WoundLevel.Dead)]);
        Assert.Equal((1, 0), tally.Of("Alik. Korpela"));
    }

    [Fact]
    public void MeleeWinner_TakesTheKill()
    {
        // The loser's wound comes before MeleeEnded in the same step.
        var tally = new CampaignTally(Side.Blue);
        Step(tally, [Shot(Korpela, Belov), new UnitWounded(0, Belov, WoundLevel.Light)]);
        Step(tally, [new UnitWounded(3, Belov, WoundLevel.Dead), new MeleeEnded(3, Makinen, Belov)]);
        Assert.Equal((1, 0), tally.Of("Alik. Korpela"));
        Assert.Equal((0, 1), tally.Of("Sotm. Mäkinen"));
    }

    [Fact]
    public void NoDoubleCredit_NoHitsOnTheFallen_NoneForTheEnemy()
    {
        var tally = new CampaignTally(Side.Blue, alreadyDown: [Orlov]);
        Step(tally, [Shot(Korpela, Belov), new UnitWounded(0, Belov, WoundLevel.Incapacitated)]);
        Step(tally, [Shot(Makinen, Belov), new UnitWounded(1, Belov, WoundLevel.Dead), Shot(Makinen, Orlov), new UnitWounded(1, Orlov, WoundLevel.Dead)]);
        Step(tally, [Shot(Belov, Korpela), new UnitWounded(2, Korpela, WoundLevel.Dead)]);
        Assert.Equal((1, 1), tally.Of("Alik. Korpela"));
        Assert.Equal((0, 0), tally.Of("Sotm. Mäkinen"));
        Assert.Equal((0, 0), tally.Of("Serž. Belov"));
    }

    [Fact]
    public void Captured_CountsAsDown()
    {
        var tally = new CampaignTally(Side.Blue);
        Step(tally, [Shot(Korpela, Belov), new UnitWounded(0, Belov, WoundLevel.Light)]);
        Step(tally, [new UnitCaptured(4, Belov)]);
        Assert.Equal((1, 1), tally.Of("Alik. Korpela"));
    }
}

/// <summary>What a mission leaves on the platoon.</summary>
public class CampaignRecordTests
{
    private static readonly CampaignSpec Spec = new("c", new Localized("C"), new Localized("intro"),
        [CampaignTests.Man("Alik. Korpela", 90, 90), CampaignTests.Man("Sotm. Mäkinen"), CampaignTests.Man("Sotm. Laine"), CampaignTests.Man("Sotm. Aho")],
        [new CampaignChapter("m", new Localized("b"), new Localized("a")), new CampaignChapter("n", new Localized("b2"), new Localized("a2"))]);

    private static CampaignOutcome Outcome(bool success, params (string Name, WoundLevel Wound, bool Captured)[] men) =>
        new(success, 300, men.Select(m => new CampaignOutcome.Man(m.Name, m.Wound, m.Captured)).ToList(), new CampaignTally(Side.Blue));

    private static CampaignManState Man(CampaignState state, string name) => state.Men.Single(m => m.Name == name);

    [Fact]
    public void DeadFallen_IncapacitatedRecovers_LightPlays()
    {
        var state = CampaignState.New(Spec);
        var report = state.Record(Spec, Outcome(false, ("Alik. Korpela", WoundLevel.Dead, false), ("Sotm. Mäkinen", WoundLevel.Incapacitated, false),
            ("Sotm. Laine", WoundLevel.Light, false), ("Sotm. Aho", WoundLevel.Serious, false)));
        Assert.Equal(ManStatus.Fallen, Man(state, "Alik. Korpela").Status);
        Assert.Equal(ManStatus.Recovering, Man(state, "Sotm. Mäkinen").Status);
        Assert.Equal(ManStatus.Fit, Man(state, "Sotm. Laine").Status);
        Assert.Equal(ManStatus.Recovering, Man(state, "Sotm. Aho").Status); // serious: he sits one out too (ruling)
        Assert.Equal(["Alik. Korpela", "Sotm. Mäkinen", "Sotm. Laine", "Sotm. Aho"], report.Select(r => r.Name));
    }

    [Fact]
    public void Captured_IsGone()
    {
        var state = CampaignState.New(Spec);
        state.Record(Spec, Outcome(false, ("Sotm. Laine", WoundLevel.Serious, true)));
        Assert.Equal(ManStatus.Fallen, Man(state, "Sotm. Laine").Status);
        Assert.Equal(0, Man(state, "Sotm. Laine").Xp);
    }

    [Fact]
    public void RecoveringSitsOut_ThenReturns()
    {
        var state = CampaignState.New(Spec);
        Man(state, "Sotm. Aho").Status = ManStatus.Recovering;
        state.Record(Spec, Outcome(false, ("Alik. Korpela", WoundLevel.None, false)));
        Assert.Equal(ManStatus.Fit, Man(state, "Sotm. Aho").Status);
        Assert.Equal(0, Man(state, "Sotm. Aho").Missions);
    }

    [Fact]
    public void Success_Advances_AndPaysEveryoneWhoWent()
    {
        var state = CampaignState.New(Spec);
        var tally = new CampaignTally(Side.Blue);
        var korpela = new UnitId(1);
        var belov = new UnitId(9);
        tally.Observe([new ShotFired(0, korpela, default, default, belov), new UnitWounded(0, belov, WoundLevel.Dead)],
            id => id == korpela ? new CampaignTally.Seen(Side.Blue, "Alik. Korpela", default) : new CampaignTally.Seen(Side.Red, "B", default), []);
        var report = state.Record(Spec, new CampaignOutcome(true, 400,
            [new("Alik. Korpela", WoundLevel.Light, false), new("Sotm. Mäkinen", WoundLevel.Dead, false)], tally));
        Assert.Equal(1, state.Chapter);
        Assert.False(state.Completed);
        Assert.Equal(10 + 3 + 10 + 10, Man(state, "Alik. Korpela").Xp); // came through, a hit, a kill, the mission done
        Assert.Equal(10, Man(state, "Sotm. Mäkinen").Xp); // fell: only the mission done
        Assert.Equal((1, 1, 1), (Man(state, "Alik. Korpela").Missions, Man(state, "Alik. Korpela").Hits, Man(state, "Alik. Korpela").Kills));
        Assert.Equal(0, Man(state, "Sotm. Laine").Missions);
        Assert.Equal(new ManReport("Alik. Korpela", 33, ManStatus.Fit, 1, 1, WoundLevel.Light), report[0]);
        Assert.Equal(new CampaignHistoryEntry("m", true, 400), Assert.Single(state.History));
    }

    [Fact]
    public void Failure_Repeats_LossesKept()
    {
        var state = CampaignState.New(Spec);
        state.Record(Spec, Outcome(false, ("Alik. Korpela", WoundLevel.Dead, false), ("Sotm. Laine", WoundLevel.None, false)));
        Assert.Equal(0, state.Chapter);
        Assert.Equal(ManStatus.Fallen, Man(state, "Alik. Korpela").Status);
        Assert.Equal(10, Man(state, "Sotm. Laine").Xp);
        Assert.Equal(new CampaignHistoryEntry("m", false, 300), Assert.Single(state.History));
    }

    [Fact]
    public void LastChapter_Completes()
    {
        var state = CampaignState.New(Spec);
        state.Chapter = 1;
        state.Record(Spec, Outcome(true, ("Sotm. Laine", WoundLevel.None, false)));
        Assert.Equal(1, state.Chapter);
        Assert.True(state.Completed);
        Assert.Equal("n", state.History.Single().Mission);
    }
}

/// <summary>The session feeds the tally every step, whatever it keeps of the events itself.</summary>
public class CampaignSessionTests
{
    [Fact]
    public void Session_FeedsTheTally_EachStep()
    {
        var points = new List<Nmf.Sim.World.MapPoint>
        {
            new("b0", "blue", new Vec2(4050, 9050)), new("b1", "blue", new Vec2(5050, 9050)), new("b2", "blue", new Vec2(6050, 9050)),
            new("r0", "red", new Vec2(4550, 6550)), new("r1", "red", new Vec2(5550, 6550)),
        };
        var map = new Nmf.Sim.World.GridMap(100, 100, ["none"], new Nmf.Sim.World.MapFeatures([], points, []));
        string content = ContentLocator.FindContentRoot(AppContext.BaseDirectory)!;
        var session = new GameSession(Nmf.Sim.Scenarios.SkirmishScenario.Create(map, 7,
            Nmf.Content.Weapons.WeaponLoader.LoadDirectory(Path.Combine(content, "core", "weapons")),
            Nmf.Content.Weapons.GrenadeLoader.LoadDirectory(Path.Combine(content, "core", "grenades"))));
        int n = 0;
        foreach (var unit in session.Sim.Units)
            unit.Name = $"Man {n++}"; // a skirmish's men have no names; the tally keeps them by name
        session.Tally = CampaignTally.For(session.Sim, Side.Blue);
        var events = new List<SimEvent>();
        for (int i = 0; i < 90 * Nmf.Sim.SimConstants.TicksPerSecond; i++)
        {
            session.StepOnce();
            events.AddRange(session.TakeEvents());
        }
        // Shots of ours that struck an enemy still in the fight.
        var down = new HashSet<UnitId>();
        int hits = 0;
        foreach (var e in events)
        {
            if (e is ShotFired { Hit: { } hit } shot && session.Sim.FindUnit(shot.Shooter)!.Side == Side.Blue
                && session.Sim.FindUnit(hit)!.Side == Side.Red && !down.Contains(hit))
                hits++;
            if (e is UnitWounded { Level: >= WoundLevel.Incapacitated } w)
                down.Add(w.Unit);
        }
        Assert.True(hits > 0, "no fight on the field");
        var blues = session.OwnUnits.Select(u => u.Name!).ToList();
        Assert.Equal(hits, blues.Sum(n => session.Tally.Of(n).Hits));
    }
}

public class CampaignTallyMeleeTests
{
    [Fact]
    public void MeleeWinner_SeriousLoser_BleedsOut_StillHisKill()
    {
        // A melee leaves the loser seriously wounded one time in five; he bleeds out of the fight later.
        UnitId laine = new(1), makinen = new(2), belov = new(10);
        var field = new Dictionary<UnitId, CampaignTally.Seen>
        {
            [laine] = new(Side.Blue, "Sotm. Laine", default), [makinen] = new(Side.Blue, "Sotm. Mäkinen", default), [belov] = new(Side.Red, "B", default),
        };
        var tally = new CampaignTally(Side.Blue);
        void Step(IReadOnlyList<SimEvent> events) => tally.Observe(events, id => field.TryGetValue(id, out var s) ? s : null, []);
        Step([new ShotFired(0, laine, default, default, belov), new UnitWounded(0, belov, WoundLevel.Light)]);
        Step([new UnitWounded(3, belov, WoundLevel.Serious), new MeleeEnded(3, makinen, belov)]);
        Step([new UnitWounded(900, belov, WoundLevel.Incapacitated)]);
        Assert.Equal((0, 1), tally.Of("Sotm. Mäkinen"));
        Assert.Equal((1, 0), tally.Of("Sotm. Laine"));
    }
}

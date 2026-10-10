using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Tactics;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Tactics;

public class TacticPlannerTests
{
    internal static readonly TacticDef FireAndMovement = new("fire_and_movement", "Fire and movement", "Tuli ja liike", 3_500, 10_000,
        new TacticManeuver(60, 2_500, 4_500), new TacticAssault(250, 400, 1_200), 30, 200);
    internal static readonly TacticDef LongFire = new("long_fire", "Long fire", "Pitkä tuli", 5_000, 15_000, null, null, 30, 200);
    internal static readonly WeaponDef Lmg = new WeaponDef("test_lmg", "Test LMG", WeaponClass.Lmg, 20, 8, 5, 2, 6, 30, 12, 40_000, 0, 100, 40_000).Validated();

    internal static Vec2 M(int x, int y) => new(x * 100 + 50, y * 100 + 50);

    /// <summary>
    /// A 300 m field; reds (unarmed) round (150, 60) m with woods on both flanks of them; blues (rifles, lethality 0) in a
    /// row round (150, 210) m on the open ground; blue sees every red.
    /// </summary>
    internal sealed class Field
    {
        public Simulation Sim { get; } = new(new GridMap(300, 300, ["none"]), 1);
        public List<Unit> Blues { get; } = [];
        public List<Unit> Reds { get; } = [];

        public Field(int blues = 4, int reds = 1, int blueY = 210)
        {
            for (int i = 0; i < reds; i++)
                Reds.Add(Sim.SpawnUnit(Side.Red, M(150 + i * 8, 60), 8));
            for (int i = 0; i < blues; i++)
            {
                var u = Sim.SpawnUnit(Side.Blue, M(140 + i * 3, blueY), 8, TestWeapons.Rifle(lethality: 0));
                u.FirePolicy = FirePolicy.HoldFire;
                Blues.Add(u);
            }
            foreach (var red in Reds)
                See(red);
            // Woods on both flanks of the post, as round the knoll: somewhere for a strike team to form up unseen.
            Forest((x, y) => (x < 125 || x > 175) && y < 110);
        }

        /// <summary>A bare field: no woods anywhere.</summary>
        public void ClearAll() => Forest((_, _) => true, concealment: 0, height: 0);

        public void See(Unit red)
        {
            var c = Sim.Knowledge(Side.Blue).GetOrAdd(red.Id);
            c.Level = ContactLevel.Visible;
            c.Position = red.Position;
        }

        public void Forest(Func<int, int, bool> where, byte concealment = 60, short height = 1500)
        {
            for (int y = 0; y < 300; y++)
                for (int x = 0; x < 300; x++)
                    if (where(x, y))
                    {
                        ref var cell = ref Sim.Map[new CellCoord(x, y)];
                        cell.ConcealmentPerM = concealment;
                        cell.ObstacleHeightCm = height;
                    }
        }

        public TacticPlan? Plan(TacticDef tactic) =>
            TacticPlanner.Propose(Sim, Side.Blue, Blues.Select(b => b.Id).ToList(), Reds[0].Id, tactic);
    }

    private static int DistM(Vec2 a, Vec2 b) => (int)(IntMath.Isqrt((a - b).LengthSquared) / 100);

    private static Vec2 Centre(IEnumerable<Vec2> points)
    {
        var list = points.ToList();
        return new Vec2((int)list.Average(p => (long)p.X), (int)list.Average(p => (long)p.Y));
    }

    [Fact]
    public void Teams_TwoSquads_LmgSquadIsTheFireTeam()
    {
        var f = new Field(blues: 7);
        for (int i = 4; i < 7; i++)
            f.Blues[i].Squad = 1;
        f.Blues[5].Weapon = Lmg;
        var plan = f.Plan(FireAndMovement)!;
        Assert.Equal(f.Blues.Skip(4).Select(b => b.Id), plan.FireTeam.Select(p => p.Man).OrderBy(id => id.Value));
        Assert.Equal(f.Blues.Take(4).Select(b => b.Id), plan.StrikeTeam.OrderBy(id => id.Value));
    }

    [Fact]
    public void Teams_OneSquad_LmgAndBestShotsFire_SmgsStrike()
    {
        var f = new Field(blues: 6);
        f.Blues[0].Weapon = Lmg;
        f.Blues[3].Marksmanship = 90;
        f.Blues[1].Weapon = TestWeapons.Smg();
        f.Blues[1].Marksmanship = 95; // a good shot, but a submachine gunner goes in
        f.Blues[5].Marksmanship = 80;
        var plan = f.Plan(FireAndMovement)!;
        Assert.Equal(new[] { f.Blues[0].Id, f.Blues[3].Id, f.Blues[5].Id }, plan.FireTeam.Select(p => p.Man).OrderBy(id => id.Value));
        Assert.Contains(f.Blues[1].Id, plan.StrikeTeam);
    }

    [Fact]
    public void FireAndMovement_NeedsFourMen()
    {
        var f = new Field(blues: 3);
        Assert.Null(f.Plan(FireAndMovement));
        Assert.NotNull(f.Plan(LongFire));
    }

    [Fact]
    public void LongFire_EveryoneIsTheFireTeam()
    {
        var plan = new Field().Plan(LongFire)!;
        Assert.Equal(4, plan.FireTeam.Count);
        Assert.Empty(plan.StrikeTeam);
        Assert.Null(plan.FormUp.FirstOrDefault());
    }

    [Fact]
    public void FireBase_IsWithinTheBand_OnOurSide_AndSeesThePosition()
    {
        var f = new Field();
        var plan = f.Plan(FireAndMovement)!;
        var aim = f.Reds[0].Position;
        Assert.False(plan.FireBaseBlind);
        foreach (var spot in plan.FireTeam.Select(p => p.At))
        {
            Assert.InRange(DistM(spot, aim), 33, 102);
            Assert.True(spot.Y > aim.Y, $"fire base {spot} is not on our side of {aim}");
        }
    }

    [Fact]
    public void FireBase_PrefersCover()
    {
        var f = new Field();
        // A low wall 80 m south of the post, a little east; the rest of the field is bare.
        for (int x = 156; x <= 162; x++)
        {
            ref var cell = ref f.Sim.Map[new CellCoord(x, 139)];
            cell.ObstacleHeightCm = 60;
            cell.Cover = 220;
        }
        var plan = f.Plan(LongFire)!;
        var anchor = plan.FireTeam[0].At;
        Assert.InRange(anchor.ToCell().X, 155, 163);
        Assert.InRange(anchor.ToCell().Y, 140, 141);
    }

    [Fact]
    public void FireBase_NoSightInBand_ComesCloser()
    {
        var f = new Field();
        f.Forest((x, y) => (x - 150) * (x - 150) + (y - 60) * (y - 60) > 31 * 31);
        var plan = f.Plan(LongFire)!;
        Assert.False(plan.FireBaseBlind);
        Assert.All(plan.FireTeam, p => Assert.InRange(DistM(p.At, f.Reds[0].Position), 24, 35));
    }

    [Fact]
    public void FireBase_NoSightAnywhere_IsBlind()
    {
        var f = new Field();
        f.Forest((_, _) => true, concealment: 255);
        var plan = f.Plan(LongFire)!;
        Assert.True(plan.FireBaseBlind);
        Assert.True(plan.Reason.Blind);
    }

    [Fact]
    public void FormUp_IsOffTheFireLine_ByTheFlankAngle()
    {
        var f = new Field();
        var plan = f.Plan(FireAndMovement)!;
        var aim = f.Reds[0].Position;
        var u = Centre(plan.FireTeam.Select(p => p.At)) - aim;
        var v = plan.FormUp[0].At - aim;
        double cos = (double)u.Dot(v) / Math.Sqrt((double)u.LengthSquared * v.LengthSquared);
        Assert.True(cos <= 0.5 + 0.02, $"form-up only {Math.Acos(cos) * 180 / Math.PI:F0}° off the fire line");
        Assert.InRange(DistM(plan.FormUp[0].At, aim), 24, 46);
        Assert.NotEqual(0, plan.Reason.Side);
    }

    [Fact]
    public void Route_GoesRoundTheOpenGroundTheEnemySees()
    {
        var f = new Field();
        f.ClearAll();
        f.Forest((x, _) => x < 120); // only a forest belt west of the field; the post sees the whole field
        var plan = f.Plan(FireAndMovement)!;
        Assert.True(plan.FormUp[0].At.ToCell().X < 125, $"form-up {plan.FormUp[0].At} not on the forest side");
        var exposed = plan.Route.Where(p => p.ToCell().Y is > 90 and < 190 && p.ToCell().X >= 125).ToList();
        Assert.True(exposed.Count <= 1, $"route crosses the open field: {string.Join(" ", exposed)}");
    }

    [Fact]
    public void Route_DoesNotCrossTheFireLine()
    {
        var f = new Field();
        var plan = f.Plan(FireAndMovement)!;
        var aim = f.Reds[0].Position;
        var fireBase = Centre(plan.FireTeam.Select(p => p.At));
        var d = aim - fireBase;
        foreach (var p in plan.Route)
        {
            var rel = p - fireBase;
            double t = (double)rel.Dot(d) / d.LengthSquared;
            if (t is < 0.15 or > 0.85)
                continue;
            double off = Math.Abs((double)rel.X * d.Y - (double)rel.Y * d.X) / Math.Sqrt(d.LengthSquared);
            Assert.True(off >= 700, $"route point {p} only {off / 100:F1} m off the line of fire");
        }
    }

    [Fact]
    public void Route_EndsAtTheFormUp_InShortLegs()
    {
        var f = new Field();
        var plan = f.Plan(FireAndMovement)!;
        Assert.Equal(plan.FormUp[0].At, plan.Route[^1]);
        for (int i = 1; i < plan.Route.Count; i++)
            Assert.True((plan.Route[i] - plan.Route[i - 1]).LengthSquared <= 1_000L * 1_000 + 200L * 200, "route leg over 10 m");
    }

    [Fact]
    public void Position_GathersKnownEnemiesWithin30m()
    {
        var f = new Field(reds: 3); // at 150, 158, 166 m: all within 30 m of the first
        var far = f.Sim.SpawnUnit(Side.Red, M(150, 20), 8);
        f.See(far);
        var unseen = f.Sim.SpawnUnit(Side.Red, M(152, 62), 8);
        var plan = f.Plan(FireAndMovement)!;
        Assert.Equal(f.Reds.Select(r => r.Id), plan.Position);
        Assert.DoesNotContain(far.Id, plan.Position);
        Assert.DoesNotContain(unseen.Id, plan.Position);
    }

    [Fact]
    public void Suggest_PrefersLongFire_WhenTheEnemyOutnumbersUs()
    {
        var f = new Field(blues: 4, reds: 4);
        var plan = TacticPlanner.Suggest(f.Sim, Side.Blue, f.Blues.Select(b => b.Id).ToList(), f.Reds[0].Id, [FireAndMovement, LongFire])!;
        Assert.Equal("long_fire", plan.TacticId);
    }

    [Fact]
    public void Suggest_PrefersFireAndMovement_OtherwiseByOrder()
    {
        var f = new Field(blues: 6, reds: 2);
        var plan = TacticPlanner.Suggest(f.Sim, Side.Blue, f.Blues.Select(b => b.Id).ToList(), f.Reds[0].Id, [FireAndMovement, LongFire])!;
        Assert.Equal("fire_and_movement", plan.TacticId);
    }

    [Fact]
    public void SameInput_SamePlan()
    {
        var f = new Field(blues: 6, reds: 2);
        f.Forest((x, _) => x < 120);
        var a = f.Plan(FireAndMovement)!;
        var b = f.Plan(FireAndMovement)!;
        Assert.Equal(a.FireTeam, b.FireTeam);
        Assert.Equal(a.StrikeTeam, b.StrikeTeam);
        Assert.Equal(a.FormUp, b.FormUp);
        Assert.Equal(a.Route, b.Route);
        Assert.Equal(a with { FireTeam = b.FireTeam, StrikeTeam = b.StrikeTeam, FormUp = b.FormUp, Route = b.Route, Position = b.Position }, b);
    }

    [Fact]
    public void UnknownTarget_NoPlan()
    {
        var f = new Field();
        f.Sim.Knowledge(Side.Blue).GetOrAdd(f.Reds[0].Id).Level = ContactLevel.Unknown;
        Assert.Null(f.Plan(FireAndMovement));
    }

    [Fact]
    public void FormUp_IsWhereThePositionCannotSeeIt()
    {
        var f = new Field();
        var plan = f.Plan(FireAndMovement)!;
        var threat = ThreatMap.Build(f.Sim, Side.Blue, f.Reds[0].Position, 10_000, possibleShootersNear: f.Reds[0].Position);
        Assert.All(plan.FormUp, p => Assert.Equal(0, threat.ExposureAt(p.At)));
    }

    [Fact]
    public void NoHiddenFormUp_FireAndMovementDoesNotSuit_LongFireIsSuggested()
    {
        var f = new Field(blues: 6);
        f.ClearAll();
        Assert.Null(f.Plan(FireAndMovement));
        Assert.Equal("long_fire", TacticPlanner.Suggest(f.Sim, Side.Blue, f.Blues.Select(b => b.Id).ToList(), f.Reds[0].Id, [FireAndMovement, LongFire])!.TacticId);
    }

    [Fact]
    public void FormUp_AvoidsWhatAnEmptyFoxholeOverlooks()
    {
        var f = new Field();
        // A foxhole 20 m west of the post on the edge of the western woods: the woods' near edge is under its eye.
        f.Sim.Map[new CellCoord(130, 60)].GroundHeightCm = -100;
        var plan = f.Plan(FireAndMovement)!;
        var hole = new CellCoord(130, 60).CenterCm;
        Assert.All(plan.FormUp, p => Assert.True(
            LineOfSight.Clarity(f.Sim.Map, hole, f.Sim.Map[new CellCoord(130, 60)].GroundHeightCm + 150, p.At, f.Sim.Map.CellAt(p.At).GroundHeightCm + 100) == 0,
            $"form-up {p.At} in sight of the foxhole"));
    }
}

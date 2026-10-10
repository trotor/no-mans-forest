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
    internal static readonly TacticDef Infiltration = new("infiltration", "Infiltration", "Soluttautuminen", 1_800, 3_000, null, null, 30, 200,
        new TacticInfiltration(1_800, 3_000, 60));
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
        Assert.Equal(a.FireRoute, b.FireRoute);
        Assert.Equal(a.RouteExposed, b.RouteExposed);
        Assert.Equal(a with { FireTeam = b.FireTeam, StrikeTeam = b.StrikeTeam, FormUp = b.FormUp, Route = b.Route, Position = b.Position, FireRoute = b.FireRoute, RouteExposed = b.RouteExposed }, b);
    }

    [Fact]
    public void UnknownTarget_NoPlan()
    {
        var f = new Field();
        f.Sim.Knowledge(Side.Blue).GetOrAdd(f.Reds[0].Id).Level = ContactLevel.Unknown;
        Assert.Null(f.Plan(FireAndMovement));
    }

    /// <summary>Whether a man at <paramref name="eyeAt"/> with eyes this high sees a man kneeling at <paramref name="p"/>.</summary>
    private static bool Sees(Field f, Vec2 eyeAt, int eyeAboveGroundCm, Vec2 p) =>
        LineOfSight.Clarity(f.Sim.Map, eyeAt, f.Sim.Map.CellAt(eyeAt).GroundHeightCm + eyeAboveGroundCm, p, f.Sim.Map.CellAt(p).GroundHeightCm + 100) > 0;

    [Fact]
    public void FormUp_IsWhereThePositionCannotSeeIt_EvenWhenNearerOpenGroundWouldDo()
    {
        var f = new Field();
        f.ClearAll();
        f.Forest((x, _) => x < 110); // open ground off the post's flank, woods only further west
        var plan = f.Plan(FireAndMovement)!;
        Assert.All(plan.FormUp, p => Assert.False(Sees(f, f.Reds[0].Position, 100, p.At), $"form-up {p.At} in the post's sight"));
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
        static Field WestWoods()
        {
            var f = new Field();
            f.ClearAll();
            f.Forest((x, _) => x < 110);
            return f;
        }
        var hole = new CellCoord(126, 68); // 25 m west of the post, looking into the edge of the woods
        var without = WestWoods().Plan(FireAndMovement)!;
        var f = WestWoods();
        f.Sim.Map[hole].GroundHeightCm = -100;
        var plan = f.Plan(FireAndMovement)!;
        Assert.Contains(without.FormUp, p => Sees(f, hole.CenterCm, ThreatMap.PitEyeCm, p.At)); // the hole would see that one
        Assert.All(plan.FormUp, p => Assert.False(Sees(f, hole.CenterCm, ThreatMap.PitEyeCm, p.At), $"form-up {p.At} in sight of the foxhole"));
    }

    [Fact]
    public void Routes_MarkWhereTheEnemySees_AndTheFireTeamHasItsOwn()
    {
        var f = new Field(blueY: 150);
        var plan = f.Plan(FireAndMovement)!;
        Assert.Equal(plan.Route.Count, plan.RouteExposed!.Count);
        Assert.True(plan.RouteExposed[0], "the strike team starts on open ground the post sees");
        Assert.False(plan.RouteExposed[^1], "the form-up is hidden");
        Assert.NotNull(plan.FireRoute);
        Assert.Equal(plan.FireTeam[0].At, plan.FireRoute![^1]);
    }

    // --- Edits (spec 2026-10-10-plan-preview-design §4) ---

    private static TacticPlan? Edited(Field f, TacticDef tactic, PlanEdits edits) =>
        TacticPlanner.Propose(f.Sim, Side.Blue, f.Blues.Select(b => b.Id).ToList(), f.Reds[0].Id, tactic, edits);

    [Fact]
    public void Edits_FireBase_IsWhereItWasDragged()
    {
        var f = new Field();
        var dragged = M(110, 150);
        Assert.True(DistM(f.Plan(LongFire)!.FireTeam[0].At, dragged) > 20, "the suggestion is already there");
        var plan = Edited(f, LongFire, new PlanEdits(FireBase: dragged))!;
        Assert.False(plan.FireBaseBlind);
        Assert.True(DistM(plan.FireTeam[0].At, dragged) <= 12, $"fire base {plan.FireTeam[0].At} not near {dragged}");
        Assert.All(plan.FireTeam, p => Assert.True(DistM(p.At, dragged) <= 25));
    }

    [Fact]
    public void Edits_FireBaseThatCannotSee_IsBlind()
    {
        var f = new Field();
        var dragged = M(200, 60); // deep in the eastern wood
        var plan = Edited(f, LongFire, new PlanEdits(FireBase: dragged))!;
        Assert.True(plan.FireBaseBlind);
        Assert.True(DistM(plan.FireTeam[0].At, dragged) <= 12);
    }

    [Fact]
    public void Edits_FormUp_IsWhereItWasDragged()
    {
        var f = new Field();
        bool west = f.Plan(FireAndMovement)!.FormUp[0].At.ToCell().X < 150;
        var dragged = west ? M(185, 75) : M(115, 75); // the other flank
        var plan = Edited(f, FireAndMovement, new PlanEdits(FormUp: dragged))!;
        Assert.True(DistM(plan.FormUp[0].At, dragged) <= 6, $"form-up {plan.FormUp[0].At} not near {dragged}");
        Assert.Equal(plan.FormUp[0].At, plan.Route[^1]);
    }

    [Fact]
    public void Edits_Via_RoutePassesThroughIt()
    {
        var f = new Field();
        var via = M(95, 170);
        var plan = Edited(f, FireAndMovement, new PlanEdits(Via: [via]))!;
        Assert.Contains(plan.Route, p => (p - via).LengthSquared <= 150L * 150);
        Assert.Equal(plan.FormUp[0].At, plan.Route[^1]);
        Assert.Equal(plan.Route.Count, plan.RouteExposed!.Count);
    }

    [Fact]
    public void Edits_Unreachable_NoPlan()
    {
        var f = new Field();
        // A walled-in yard on the open ground west of the men, its middle more than 12 m from the wall.
        for (int y = 130; y <= 170; y++)
            for (int x = 80; x <= 120; x++)
                if (x is 80 or 120 || y is 130 or 170)
                    f.Sim.Map[new CellCoord(x, y)] = new CellData(0, 120, 255, 255, 0, CellData.Impassable);
        Assert.Null(Edited(f, LongFire, new PlanEdits(FireBase: M(100, 150))));
        Assert.Null(Edited(f, FireAndMovement, new PlanEdits(FormUp: M(100, 150))));
        Assert.Null(Edited(f, FireAndMovement, new PlanEdits(Via: [M(100, 150)])));
        Assert.Null(Edited(f, LongFire, new PlanEdits(FireBase: M(400, 400)))); // off the map
    }

    [Fact]
    public void Edits_Empty_SamePlan()
    {
        var f = new Field();
        var plain = f.Plan(FireAndMovement)!;
        var edited = Edited(f, FireAndMovement, new PlanEdits())!;
        Assert.True(new PlanEdits().IsEmpty);
        Assert.Equal(plain.FireTeam, edited.FireTeam);
        Assert.Equal(plain.FormUp, edited.FormUp);
        Assert.Equal(plain.Route, edited.Route);
        Assert.Equal(plain.FireRoute, edited.FireRoute);
        Assert.Equal(plain.Score, edited.Score);
    }

    // --- Infiltration (spec 2026-10-10-infiltration-enemy-design §1-2) ---

    [Fact]
    public void Infiltration_FormUp_InBand_Unseen()
    {
        var f = new Field();
        var plan = f.Plan(Infiltration)!;
        foreach (var spot in plan.FormUp.Select(p => p.At))
        {
            Assert.InRange(DistM(spot, f.Reds[0].Position), 17, 31);
            Assert.False(Sees(f, f.Reds[0].Position, 100, spot), $"form-up {spot} in the post's sight");
        }
        Assert.Equal(plan.FormUp[0].At, plan.Route[^1]);
    }

    [Fact]
    public void Infiltration_NoFireTeam()
    {
        var f = new Field();
        var plan = f.Plan(Infiltration)!;
        Assert.Empty(plan.FireTeam);
        Assert.Equal(f.Blues.Select(b => b.Id), plan.StrikeTeam);
        Assert.Equal(plan.StrikeTeam[0], plan.FirstMan);
        Assert.Equal(f.Blues.Count, plan.FormUp.Count);
        Assert.Null(plan.FireRoute);
        Assert.Equal(plan.Route.Count, plan.RouteExposed!.Count);
    }

    [Fact]
    public void Infiltration_Route_AvoidsSeenAndHeardGround()
    {
        var f = new Field();
        f.ClearAll();
        f.Forest((x, _) => x < 130); // a forest belt from 20 m west of the post; the post sees the whole field
        // A man heard in the belt: a sneaking man is heard within 15 m of him.
        var heard = f.Sim.SpawnUnit(Side.Red, M(122, 125), 8);
        var c = f.Sim.Knowledge(Side.Blue).GetOrAdd(heard.Id);
        c.Level = ContactLevel.Suspected;
        c.Position = heard.Position;
        var plan = f.Plan(Infiltration)!;
        var exposed = plan.Route.Where(p => p.ToCell().Y is > 90 and < 190 && p.ToCell().X >= 135).ToList();
        Assert.True(exposed.Count <= 1, $"route crosses the open field: {string.Join(" ", exposed)}");
        Assert.All(plan.Route, p => Assert.True(DistM(p, heard.Position) >= 15, $"route point {p} within earshot of the heard man"));
    }

    [Fact]
    public void Infiltration_NoHiddenFormUp_NoPlan()
    {
        var f = new Field();
        f.ClearAll();
        Assert.Null(f.Plan(Infiltration));
    }

    [Fact]
    public void Infiltration_Edits_FormUpAndVia_FireBaseIgnored()
    {
        var f = new Field();
        var plain = f.Plan(Infiltration)!;
        var plan = Edited(f, Infiltration, new PlanEdits(FireBase: M(60, 140), Via: [M(110, 170)]))!;
        Assert.Empty(plan.FireTeam);
        Assert.Contains(plan.Route, p => (p - M(110, 170)).LengthSquared <= 150L * 150);
        Assert.NotEqual(plain.Route, plan.Route);
    }

    private static TacticPlan? SuggestFor(Field f) =>
        TacticPlanner.Suggest(f.Sim, Side.Blue, f.Blues.Select(b => b.Id).ToList(), f.Reds[0].Id, [FireAndMovement, Infiltration, LongFire]);

    [Fact]
    public void Suggest_BlindFireBase_ExperiencedMen_Infiltrate()
    {
        var f = new Field();
        f.Forest((_, _) => true, concealment: 255); // nobody sees anyone: a fire base would be blind, a way in hidden
        foreach (var b in f.Blues)
            b.Experience = 80;
        Assert.Equal("infiltration", SuggestFor(f)!.TacticId);
    }

    [Fact]
    public void Suggest_BlindFireBase_GreenMen_LongFire()
    {
        var f = new Field();
        f.Forest((_, _) => true, concealment: 255);
        foreach (var b in f.Blues)
            b.Experience = 40;
        Assert.Equal("long_fire", SuggestFor(f)!.TacticId);
    }

    [Fact]
    public void Suggest_FireBaseSees_FireAndMovementStillFirst()
    {
        var f = new Field();
        foreach (var b in f.Blues)
            b.Experience = 90;
        Assert.Equal("fire_and_movement", SuggestFor(f)!.TacticId);
    }
}

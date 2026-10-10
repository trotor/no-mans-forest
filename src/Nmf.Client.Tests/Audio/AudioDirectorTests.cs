using Nmf.Client.Audio;
using Nmf.Content.Audio;
using Nmf.Sim;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests.Audio;

public class AudioDirectorTests
{
    private static readonly WeaponDef Rifle = new("mosin_m39", "M/39", WeaponClass.Rifle, 5, 30, 1, 0, 24, 80, 6, 30_000, 45, 80, 30_000);
    private static readonly WeaponDef Smg = new("suomi_kp31", "Suomi", WeaponClass.Smg, 71, 6, 5, 1, 6, 100, 25, 15_000, 30, 60, 25_000);

    private static readonly string[] AllCues =
    [
        "shot.mosin_m39", "shot.suomi_kp31", "shot.rifle", "shot.smg", "shot_far.rifle", "shot_far.smg", "bullet.crack",
        "impact.ground", "impact.wood", "impact.water", "impact.body", "reload.rifle", "reload.smg", "grenade.throw",
        "grenade.explode", "grenade.explode_far", "body.fall", "melee.scuffle", "loot.rustle", "footstep.forest",
        "footstep.grass", "footstep.road", "footstep.swamp", "footstep.water", "crawl.rustle", "ui.click", "ui.paper", "shell.incoming",
    ];

    private static AudioCatalog Catalog(params string[] ids) => new(
        ids.ToDictionary(id => id, id => new CueDef(id, id.StartsWith("ui.") ? AudioBus.Ui : AudioBus.Sfx, [id + ".ogg"],
            Loud: id.StartsWith("shot") || id.StartsWith("grenade.explode") || id == "bullet.crack")),
        new Dictionary<string, string> { ["none"] = "forest", ["forest"] = "forest", ["water"] = "water" });

    /// <summary>A 400 m square map, one own man near the bottom; the listener sees the whole map.</summary>
    private sealed class Field
    {
        public Simulation Sim { get; } = new(new GridMap(400, 400, ["none", "forest", "water"]), 1);
        public Unit Own { get; }
        public HashSet<Unit> Shown { get; } = [];
        public AudioDirector Director { get; }
        public double Speed { get; set; } = 1;
        public double Zoom { get; set; } = 1;

        public Field(AudioCatalog? catalog = null)
        {
            Own = Man(Side.Blue, 20_000, 35_000);
            Director = new AudioDirector(catalog ?? Catalog(AllCues));
        }

        public Unit Man(Side side, int x, int y, WeaponDef? weapon = null)
        {
            var u = Sim.SpawnUnit(side, new Vec2(x, y), 8, weapon ?? Rifle);
            u.FirePolicy = FirePolicy.HoldFire; // nobody fires on his own while the test steps the clock
            return u;
        }

        public AudioListener Listener => new(new Vec2(20_000, 20_000), 50_000, Zoom, Speed);

        public IReadOnlyList<SoundCue> Update(params SimEvent[] events) =>
            Director.Update(Sim, Side.Blue, events, u => Shown.Contains(u), Listener, 0.016);

        /// <summary>Steps the clock and collects what sounds meanwhile.</summary>
        public List<SoundCue> Run(int ticks, params SimEvent[] events)
        {
            var cues = Update(events).ToList();
            for (int i = 0; i < ticks; i++)
            {
                Sim.Step();
                cues.AddRange(Update());
            }
            return cues;
        }

        public ShotFired Shot(Unit shooter, Vec2 to, Unit? hit = null, long? tick = null) =>
            new(tick ?? Sim.Tick, shooter.Id, shooter.Position, to, hit?.Id);
    }

    [Fact]
    public void HiddenEnemyShot_BeyondEarshot_IsSilent()
    {
        var f = new Field();
        var enemy = f.Man(Side.Red, 20_000, 0); // 350 m from the own man; a rifle carries 300 m
        Assert.DoesNotContain(f.Run(40, f.Shot(enemy, new Vec2(20_000, 1_000))), c => c.CueId.StartsWith("shot"));
    }

    [Fact]
    public void HiddenEnemyShot_InEarshot_SoundsFromTheRoughSquare()
    {
        var f = new Field();
        var enemy = f.Man(Side.Red, 20_340, 10_070); // 250 m
        var shot = f.Shot(enemy, new Vec2(20_000, 12_000));
        Assert.DoesNotContain(f.Update(shot), c => c.CueId.StartsWith("shot"));
        var cue = Assert.Single(f.Run(20), c => c.CueId.StartsWith("shot"));
        Assert.Equal(AudioMix.Rough(enemy.Position), cue.At);
        Assert.NotEqual(enemy.Position, cue.At);
    }

    [Fact]
    public void ShownShooter_SoundsFromHisExactPlace()
    {
        var f = new Field();
        var enemy = f.Man(Side.Red, 20_340, 25_070);
        f.Shown.Add(enemy);
        var cue = Assert.Single(f.Run(20, f.Shot(enemy, new Vec2(20_000, 30_000))), c => c.CueId.StartsWith("shot"));
        Assert.Equal(enemy.Position, cue.At);
    }

    [Fact]
    public void FarShot_UsesTheFarCue_NearShot_TheNearCue()
    {
        var f = new Field();
        var far = f.Man(Side.Red, 20_000, 19_000);  // 160 m
        var near = f.Man(Side.Red, 20_000, 25_000); // 100 m
        var cues = f.Run(20, f.Shot(far, new Vec2(0, 0)), f.Shot(near, new Vec2(0, 0))).Select(c => c.CueId).ToList();
        Assert.Contains("shot_far.rifle", cues);
        Assert.Contains("shot.mosin_m39", cues);
    }

    [Fact]
    public void UnknownWeapon_FallsBackToItsClass()
    {
        var f = new Field(Catalog("shot.rifle"));
        var cue = Assert.Single(f.Run(1, f.Shot(f.Own, new Vec2(20_000, 30_000))));
        Assert.Equal("shot.rifle", cue.CueId);
    }

    [Fact]
    public void DelayedShot_WaitsOutAPause()
    {
        var f = new Field();
        var enemy = f.Man(Side.Red, 20_000, 15_000); // 200 m: 11 ticks
        f.Shown.Add(enemy);
        Assert.DoesNotContain(f.Update(f.Shot(enemy, new Vec2(0, 0))), c => c.CueId.StartsWith("shot"));
        Assert.Empty(f.Update()); // paused: the clock stands
        Assert.Empty(f.Update());
        Assert.Single(f.Run(13), c => c.CueId.StartsWith("shot"));
    }

    [Fact]
    public void BulletPassingClose_Cracks_EvenWhenItEndsFarBehind()
    {
        var f = new Field();
        var enemy = f.Man(Side.Red, 20_200, 5_000);
        // From (202, 50) m past the own man at (200, 350) m to 60 m behind him: it passes him about 2 m off.
        var cues = f.Run(0, f.Shot(enemy, new Vec2(20_200, 41_000)));
        var crack = Assert.Single(cues, c => c.CueId == "bullet.crack");
        Assert.Equal(f.Own.Position, crack.At);
    }

    [Fact]
    public void OwnShot_DoesNotCrack()
    {
        var f = new Field();
        var mate = f.Man(Side.Blue, 20_000, 30_000);
        Assert.DoesNotContain(f.Run(0, f.Shot(mate, new Vec2(20_000, 40_000))), c => c.CueId == "bullet.crack");
    }

    [Fact]
    public void Impacts_FollowTheSurface()
    {
        var f = new Field();
        f.Sim.Map[new CellCoord(200, 300)] = new CellData(0, 0, 0, 0, TerrainId: 2);
        f.Sim.Map[new CellCoord(210, 300)] = new CellData(0, 1500, 0, 0, TerrainId: 1);
        var enemy = f.Man(Side.Red, 22_050, 30_050);
        var mates = Enumerable.Range(0, 3).Select(i => f.Man(Side.Blue, 19_000 + i * 1_000, 34_000)).ToList(); // one round each
        var cues = f.Run(0,
            f.Shot(f.Own, new Vec2(20_050, 30_050)),
            f.Shot(mates[0], new Vec2(21_050, 30_050)),
            f.Shot(mates[1], enemy.Position, hit: enemy),
            f.Shot(mates[2], new Vec2(19_050, 30_050))).Select(c => c.CueId).ToList();
        Assert.Contains("impact.water", cues);
        Assert.Contains("impact.wood", cues);
        Assert.Contains("impact.body", cues);
        Assert.Contains("impact.ground", cues);
    }

    [Fact]
    public void ShotEndingOutsideTheMap_PlaysNoImpact_AndDoesNotThrow()
    {
        var f = new Field();
        var cues = f.Run(0, f.Shot(f.Own, new Vec2(-5_000, 35_000)));
        Assert.DoesNotContain(cues, c => c.CueId.StartsWith("impact"));
        Assert.Contains(cues, c => c.CueId.StartsWith("shot"));
    }

    [Fact]
    public void NoOwnMenLeft_HiddenShotsAreSilent()
    {
        var f = new Field();
        f.Own.Wound = WoundLevel.Dead;
        var enemy = f.Man(Side.Red, 20_000, 30_000);
        Assert.Empty(f.Run(20, f.Shot(enemy, f.Own.Position)));
    }

    [Fact]
    public void AManFallsOnce_EvenWhenHeLaterDies()
    {
        var f = new Field();
        var cues = f.Run(1, new UnitWounded(0, f.Own.Id, WoundLevel.Incapacitated));
        cues.AddRange(f.Run(1, new UnitWounded(1, f.Own.Id, WoundLevel.Dead)));
        Assert.Single(cues, c => c.CueId == "body.fall");
    }

    [Fact]
    public void HiddenEnemyLooting_IsSilent_OwnLooting_IsHeard()
    {
        var f = new Field();
        var enemy = f.Man(Side.Red, 20_000, 33_000);
        var body = f.Man(Side.Red, 20_000, 34_000);
        Assert.Empty(f.Run(0, new UnitLooted(0, enemy.Id, body.Id, 1, 0, null, [])));
        Assert.Single(f.Run(0, new UnitLooted(0, f.Own.Id, body.Id, 1, 0, null, [])), c => c.CueId == "loot.rustle");
    }

    [Fact]
    public void GrenadeFarAway_UsesTheFarExplosion()
    {
        var f = new Field();
        var cues = f.Run(20, new GrenadeExploded(0, 1, new Vec2(20_000, 15_000))); // 200 m
        Assert.Single(cues, c => c.CueId == "grenade.explode_far");
        Assert.Empty(f.Run(60, new GrenadeExploded(f.Sim.Tick, 2, new Vec2(20_000, -55_000)))); // 900 m: not heard
    }

    [Fact]
    public void NearGrenade_UsesTheNearExplosion_AndAShownThrowIsHeard()
    {
        var f = new Field();
        var enemy = f.Man(Side.Red, 20_000, 32_000);
        f.Shown.Add(enemy);
        var cues = f.Run(5,
            new GrenadeThrown(0, enemy.Id, enemy.Position, f.Own.Position, 80),
            new GrenadeExploded(0, 1, new Vec2(20_000, 34_000))).Select(c => c.CueId).ToList();
        Assert.Contains("grenade.throw", cues);
        Assert.Contains("grenade.explode", cues);
    }

    [Fact]
    public void MeleeWithAnOwnMan_IsHeard()
    {
        var f = new Field();
        var enemy = f.Man(Side.Red, 20_000, 35_100);
        Assert.Single(f.Run(0, new MeleeStarted(0, enemy.Id, f.Own.Id)), c => c.CueId == "melee.scuffle");
    }

    [Fact]
    public void SmgBurstAtNormalSpeed_PlaysEveryRound()
    {
        var f = new Field();
        var gunner = f.Man(Side.Blue, 20_000, 30_000, Smg);
        f.Sim.Step();
        f.Sim.Step();
        var to = new Vec2(20_000, 20_000);
        var cues = f.Run(0, f.Shot(gunner, to, tick: 0), f.Shot(gunner, to, tick: 1), f.Shot(gunner, to, tick: 2));
        Assert.Equal(3, cues.Count(c => c.CueId == "shot.suomi_kp31"));
    }

    [Fact]
    public void EightTimesSpeed_ThrottlesOneShootersShots_AndDropsFootsteps()
    {
        var f = new Field { Speed = 8 };
        var gunner = f.Man(Side.Blue, 20_000, 30_000, Smg);
        f.Sim.Step();
        f.Sim.Step();
        var to = new Vec2(20_000, 20_000);
        var cues = f.Run(0, f.Shot(gunner, to, tick: 0), f.Shot(gunner, to, tick: 1), f.Shot(gunner, to, tick: 2));
        Assert.Single(cues, c => c.CueId == "shot.suomi_kp31");

        gunner.MoveMode = MoveMode.Walk;
        Assert.DoesNotContain(Walk(f, gunner, 30), c => c.CueId.StartsWith("footstep"));
    }

    /// <summary>Moves the man 10 cm north every tick for <paramref name="ticks"/> ticks, collecting the sounds.</summary>
    private static List<SoundCue> Walk(Field f, Unit man, int ticks, params Unit[] others)
    {
        var cues = f.Update().ToList();
        for (int i = 0; i < ticks; i++)
        {
            f.Sim.Step();
            foreach (var u in others.Prepend(man))
                u.Position += new Vec2(0, -10);
            cues.AddRange(f.Update());
        }
        return cues;
    }

    [Fact]
    public void Walking_KeepsItsPace_WithSeveralFramesPerTick()
    {
        var f = new Field();
        f.Own.MoveMode = MoveMode.Walk;
        var cues = f.Update().ToList();
        for (int i = 0; i < 22; i++)
        {
            f.Sim.Step();
            f.Own.Position += new Vec2(0, -10);
            cues.AddRange(f.Update());
            cues.AddRange(f.Update()); // a fast screen draws frames between ticks
            cues.AddRange(f.Update());
        }
        Assert.Equal(2, cues.Count(c => c.CueId.StartsWith("footstep")));
    }

    [Fact]
    public void EightTimesSpeed_ThrottlesCracksAndImpacts_LikeTheShots()
    {
        var f = new Field { Speed = 8 };
        var enemy = f.Man(Side.Red, 20_000, 33_000, Smg);
        f.Shown.Add(enemy);
        var cues = new List<SoundCue>();
        for (int i = 0; i < 24; i++)
        {
            cues.AddRange(f.Update(f.Shot(enemy, new Vec2(20_000, 36_000))));
            f.Sim.Step();
        }
        int shots = cues.Count(c => c.CueId.StartsWith("shot"));
        Assert.InRange(shots, 1, 4);
        Assert.InRange(cues.Count(c => c.CueId == "bullet.crack"), 1, shots);
        Assert.InRange(cues.Count(c => c.CueId.StartsWith("impact")), 1, shots);
    }

    [Fact]
    public void Reload_SoundsOnce_WhenItStarts()
    {
        var f = new Field();
        Assert.Empty(f.Update());
        f.Own.Action = CombatAction.Reloading;
        Assert.Single(f.Update(), c => c.CueId == "reload.rifle");
        Assert.Empty(f.Update());
        f.Own.Action = CombatAction.None;
        f.Update();
        f.Own.Action = CombatAction.Reloading;
        Assert.Single(f.Update(), c => c.CueId == "reload.rifle");
    }

    [Fact]
    public void HiddenEnemyReload_IsSilent()
    {
        var f = new Field();
        var enemy = f.Man(Side.Red, 20_000, 34_000);
        f.Update();
        enemy.Action = CombatAction.Reloading;
        Assert.Empty(f.Update());
    }

    [Fact]
    public void Walking_StepsEveryElevenTicks_OnTheCellsSurface()
    {
        var f = new Field();
        for (int y = 340; y <= 350; y++)
            f.Sim.Map[new CellCoord(200, y)] = new CellData(0, 0, 0, 0, TerrainId: 1);
        f.Own.MoveMode = MoveMode.Walk;
        var steps = Walk(f, f.Own, 22).Where(c => c.CueId.StartsWith("footstep")).ToList();
        Assert.Equal(2, steps.Count);
        Assert.All(steps, c => Assert.Equal("footstep.forest", c.CueId));
    }

    [Fact]
    public void Crawling_Rustles()
    {
        var f = new Field();
        f.Own.MoveMode = MoveMode.Crawl;
        Assert.Contains(Walk(f, f.Own, 5), c => c.CueId == "crawl.rustle");
    }

    [Fact]
    public void OnlyTheFourMenNearestTheCamera_Step()
    {
        var f = new Field();
        var men = Enumerable.Range(0, 5).Select(i => f.Man(Side.Blue, 20_000 + (i + 1) * 3_000, 20_000)).ToList();
        f.Own.Position = new Vec2(20_000, 20_000); // the camera centre
        var cues = Walk(f, f.Own, 3, men.ToArray()).Where(c => c.CueId.StartsWith("footstep")).ToList();
        Assert.Equal(4, cues.Count);
        Assert.DoesNotContain(cues, c => c.At!.Value.X == men[4].Position.X);
    }

    [Fact]
    public void Versions_NeverRepeatBackToBack()
    {
        var catalog = new AudioCatalog(
            new Dictionary<string, CueDef> { ["ui.click"] = new("ui.click", AudioBus.Ui, ["a.ogg", "b.ogg"]) },
            new Dictionary<string, string>());
        var director = new AudioDirector(catalog);
        var played = Enumerable.Range(0, 20).Select(_ => director.Ui("ui.click")!).ToList();
        for (int i = 1; i < played.Count; i++)
            Assert.NotEqual(played[i - 1].File, played[i].File);
        Assert.All(played, c => Assert.InRange(c.Pitch, 0.96, 1.04));
    }

    [Fact]
    public void Ui_HasNoPlace_AndIsSilentWithoutFiles()
    {
        var director = new AudioDirector(Catalog("ui.click"));
        var cue = director.Ui("ui.click")!;
        Assert.Null(cue.At);
        Assert.Equal(AudioBus.Ui, cue.Bus);
        Assert.Null(new AudioDirector(AudioCatalog.Empty).Ui("ui.click"));
    }

    [Fact]
    public void HeardSounds_ComeWithoutFiles()
    {
        var f = new Field(AudioCatalog.Empty);
        var enemy = f.Man(Side.Red, 20_340, 25_070); // 100 m, hidden; his bullet passes our man
        f.Update(f.Shot(enemy, new Vec2(20_000, 40_000)));
        var heard = new List<HeardSound>(f.Director.Heard);
        for (int i = 0; i < 10; i++)
        {
            f.Sim.Step();
            f.Update();
            heard.AddRange(f.Director.Heard);
        }
        Assert.Contains(heard, h => h.Kind == HeardKind.Shot && h.At == AudioMix.Rough(enemy.Position));
        Assert.Contains(heard, h => h.Kind == HeardKind.Crack);
    }

    [Fact]
    public void HeardSounds_NotForOwnShotsNorQuietSounds()
    {
        var f = new Field(AudioCatalog.Empty);
        f.Update(f.Shot(f.Own, new Vec2(20_000, 20_000)), new UnitLooted(0, f.Own.Id, f.Own.Id, 0, 0, null, []));
        f.Own.Action = CombatAction.Reloading;
        f.Update();
        Assert.Empty(f.Director.Heard);
    }

    [Fact]
    public void HeardSounds_NotWhenNotHeard()
    {
        var f = new Field(AudioCatalog.Empty);
        var enemy = f.Man(Side.Red, 20_000, 0); // 350 m: beyond earshot
        f.Update(f.Shot(enemy, new Vec2(20_000, 1_000)));
        for (int i = 0; i < 30; i++)
        {
            f.Sim.Step();
            f.Update();
            Assert.Empty(f.Director.Heard);
        }
    }

    [Fact]
    public void AStrandedManOfOurs_HearsNothing_AndIsNotMarked()
    {
        var f = new Field();
        f.Own.Position = new Vec2(1_000, 1_000); // the searchers far off in the corner
        var lost = f.Man(Side.Blue, 20_000, 35_000);
        lost.Stranded = true;
        var enemy = f.Man(Side.Red, 20_200, 5_000);
        var cues = f.Run(0, f.Shot(enemy, new Vec2(20_200, 41_000)),
            new Nmf.Sim.Events.UnitWounded(f.Sim.Tick, lost.Id, WoundLevel.Incapacitated));
        Assert.DoesNotContain(cues, c => c.CueId == "bullet.crack" || c.CueId == "body.fall");
        Assert.DoesNotContain(f.Director.Heard, h => (h.At - lost.Position).LengthSquared < 1_000L * 1_000);
    }

    [Fact]
    public void AMortarBomb_IsHeardAndMarked()
    {
        var f = new Field();
        var at = new Vec2(20_000, 10_000);
        var cues = f.Run(0, new ShellExploded(f.Sim.Tick, Side.Blue, at, Ranging: true));
        bool marked = false;
        for (int i = 0; i < 60; i++)
        {
            cues.AddRange(f.Run(1));
            marked |= f.Director.Heard.Any(h => h.At == at); // a mark for a moment, once the sound has come (250 m: under a second)
        }
        Assert.Contains(cues, c => c.CueId.StartsWith("grenade.explode") && c.At == at);
        Assert.True(marked);
    }

    [Fact]
    public void ShellIncoming_WhistlesWhereItWillLand_WhenItLandsNearOurMen()
    {
        var f = new Field();
        var near = new Vec2(20_000, 30_000); // 50 m from our man
        var cues = f.Run(0, new ShellIncoming(f.Sim.Tick, Side.Blue, near, f.Sim.Tick + 40, Ranging: true));
        Assert.Contains(cues, c => c.CueId == "shell.incoming" && c.At == near);
        var farAway = new Field();
        var far = farAway.Run(0, new ShellIncoming(farAway.Sim.Tick, Side.Blue, new Vec2(20_000, 500), farAway.Sim.Tick + 40, Ranging: true));
        Assert.DoesNotContain(far, c => c.CueId == "shell.incoming"); // 345 m off: the bang is heard, the whistle is not
    }
}

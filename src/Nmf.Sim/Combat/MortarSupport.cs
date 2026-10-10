using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Sim.Combat;

public enum MortarPhase : byte { Idle, Calling, Waiting, Ranging, Effect }

/// <summary>
/// A side's 81 mm mortar by radio (spec 2026-10-10-mortar-design): the call (it may not get through), the delay, ranging
/// rounds corrected when one of the side sees them land, fire for effect, limited rounds. Inside the step, by the
/// simulation's own random numbers: deterministic.
/// </summary>
public sealed class MortarSupport
{
    public const int CallTicks = 10 * SimConstants.TicksPerSecond;
    public const int RangingIntervalTicks = 20 * SimConstants.TicksPerSecond, EffectIntervalTicks = 3 * SimConstants.TicksPerSecond;
    /// <summary>A shell is heard coming this long before it lands.</summary>
    public const int FlightWarnTicks = 2 * SimConstants.TicksPerSecond;
    public const int MaxRangingRounds = 4, AcceptErrorCm = 1_500, FirstErrorMinCm = 4_000, FirstErrorMaxCm = 6_000, DispersionCm = 800;
    /// <summary>Beyond this nobody can tell where a shell landed.</summary>
    public const int ObserveRangeCm = 40_000;

    /// <summary>An 81 mm bomb: a hand grenade's burst, bigger.</summary>
    public static readonly GrenadeDef Round = new("mortar_81", "81 mm", 1, 0, 0, 3_000, 1_200, 900, 80);

    private readonly int _delayMin, _delayMax, _callSuccessPct;
    private readonly List<(Vec2 At, long Impact, bool Ranging)> _inFlight = [];
    /// <summary>The rounds ran out before all those asked for were fired.</summary>
    private bool _ranOut;

    public MortarSupport(Side side, UnitId radio, int rounds, int delayMinTicks, int delayMaxTicks, int callSuccessPct = 85)
    {
        Side = side;
        Radio = radio;
        Rounds = rounds;
        _delayMin = delayMinTicks;
        _delayMax = Math.Max(delayMinTicks, delayMaxTicks);
        _callSuccessPct = callSuccessPct;
    }

    public Side Side { get; }
    public UnitId Radio { get; }
    public int Rounds { get; private set; }
    public MortarPhase Phase { get; private set; }
    public Vec2 Target { get; private set; }
    /// <summary>When the next thing happens: the call answered, the first round, the next round.</summary>
    public long NextTick { get; private set; }
    /// <summary>Where the rounds fall off the target now (ranging brings it down).</summary>
    public Vec2 Offset { get; private set; }
    public int RangingFired { get; private set; }
    public int EffectLeft { get; private set; }
    public int InFlight => _inFlight.Count;

    /// <summary>Fire can be called: rounds left and the radioman fit, not pinned nor broken.</summary>
    public bool Available(Simulation sim) =>
        Rounds > 0 && sim.FindUnit(Radio) is { IsOutOfAction: false, MoraleState: MoraleState.Steady };

    /// <summary>A new fire mission; null when taken, else why not.</summary>
    internal string? Order(Simulation sim, FireMissionOrder order, long tick)
    {
        if (order.Unit != Radio)
            return "only the radioman calls the mortar";
        if (!Available(sim))
            return "no mortar to call (no rounds, or the radioman cannot)";
        if (!sim.Map.Contains(order.Target))
            return "target outside map";
        if (Phase is MortarPhase.Ranging or MortarPhase.Effect)
            return "the mortar is firing";
        Target = order.Target;
        _ranOut = false;
        EffectLeft = Math.Clamp(order.Rounds, 1, 12);
        RangingFired = 0;
        Offset = Vec2.Zero;
        Phase = MortarPhase.Calling;
        NextTick = tick + CallTicks;
        return null;
    }

    internal void Update(Simulation sim, long tick, List<SimEvent> events)
    {
        Land(sim, tick, events);
        switch (Phase)
        {
            case MortarPhase.Calling when tick >= NextTick:
                if (sim.Rng.NextInt(100) < _callSuccessPct)
                {
                    Phase = MortarPhase.Waiting;
                    // The first burst this long after the call: the round is fired a flight before.
                    NextTick = tick + sim.Rng.NextInt(_delayMin, _delayMax + 1) - FlightWarnTicks;
                    events.Add(new FireMissionCalled(tick, Side));
                }
                else
                {
                    Phase = MortarPhase.Idle;
                    events.Add(new FireMissionFailed(tick, Side));
                }
                break;
            case MortarPhase.Waiting when tick >= NextTick:
                int degrees = sim.Rng.NextInt(360);
                int error = sim.Rng.NextInt(FirstErrorMinCm, FirstErrorMaxCm + 1);
                Offset = Polar(degrees, error);
                Enter(MortarPhase.Ranging, tick, events);
                Fire(sim, tick, ranging: true, events);
                break;
            case MortarPhase.Effect when tick >= NextTick && EffectLeft > 0:
                Fire(sim, tick, ranging: false, events);
                EffectLeft--;
                NextTick = tick + EffectIntervalTicks;
                break;
            case MortarPhase.Effect when EffectLeft == 0 && _inFlight.Count == 0:
                End(tick, events, outOfRounds: _ranOut);
                break;
        }
    }

    /// <summary>Shells landing now: the burst, and for a ranging round the correction it allows.</summary>
    private void Land(Simulation sim, long tick, List<SimEvent> events)
    {
        for (int i = 0; i < _inFlight.Count; i++)
        {
            var (at, impact, ranging) = _inFlight[i];
            if (impact != tick)
                continue;
            events.Add(new ShellExploded(tick, Side, at, ranging));
            GrenadeSystem.Burst(sim, at, Round, at, tick, events);
            if (!ranging || Phase != MortarPhase.Ranging)
                continue;
            // Seen to land: the observer's correction halves the error; unseen, the next is as far off, somewhere else.
            Offset = Observed(sim, at) ? new Vec2(Offset.X / 2, Offset.Y / 2) : Polar(sim.Rng.NextInt(360), (int)IntMath.Isqrt(Offset.LengthSquared));
            if (Offset.LengthSquared < (long)AcceptErrorCm * AcceptErrorCm || RangingFired >= MaxRangingRounds)
            {
                Enter(MortarPhase.Effect, tick, events);
                NextTick = tick + EffectIntervalTicks;
            }
            else
            {
                NextTick = tick + RangingIntervalTicks;
            }
        }
        _inFlight.RemoveAll(s => s.Impact <= tick);
        if (Phase == MortarPhase.Ranging && _inFlight.Count == 0 && tick >= NextTick)
            Fire(sim, tick, ranging: true, events);
    }

    private void Fire(Simulation sim, long tick, bool ranging, List<SimEvent> events)
    {
        if (Rounds <= 0)
        {
            if (_inFlight.Count == 0)
                End(tick, events, outOfRounds: true);
            else
                EffectLeft = 0;
            return;
        }
        Rounds--;
        var at = Target + Offset;
        if (!ranging)
            at += new Vec2(sim.Rng.NextInt(-DispersionCm, DispersionCm + 1), sim.Rng.NextInt(-DispersionCm, DispersionCm + 1));
        at = new Vec2(Math.Clamp(at.X, 0, sim.Map.WidthCm - 1), Math.Clamp(at.Y, 0, sim.Map.HeightCm - 1));
        if (ranging)
        {
            RangingFired++;
            NextTick = long.MaxValue; // the next ranging round waits for this one to land
        }
        _inFlight.Add((at, tick + FlightWarnTicks, ranging));
        events.Add(new ShellIncoming(tick, Side, at, tick + FlightWarnTicks, ranging));
        if (Rounds == 0 && !ranging && EffectLeft > 1)
        {
            EffectLeft = 1; // this was the last one
            _ranOut = true;
        }
    }

    private void Enter(MortarPhase phase, long tick, List<SimEvent> events)
    {
        Phase = phase;
        events.Add(new FireMissionPhaseChanged(tick, Side, phase));
    }

    private void End(long tick, List<SimEvent> events, bool outOfRounds)
    {
        Phase = MortarPhase.Idle;
        EffectLeft = 0;
        events.Add(new FireMissionEnded(tick, Side, outOfRounds));
    }

    /// <summary>One of the side, in action, sees the ground where it landed.</summary>
    private bool Observed(Simulation sim, Vec2 at)
    {
        int ground = sim.Map.CellAt(at).GroundHeightCm + 50;
        long rangeSq = (long)ObserveRangeCm * ObserveRangeCm;
        return sim.Units.Any(u => u.Side == Side && !u.IsOutOfAction && !u.Stranded && (u.Position - at).LengthSquared <= rangeSq
                                  && LineOfSight.Clarity(sim.Map, u.Position, VisionRules.EyeHeightAbsCm(sim.Map, u), at, ground) > 0);
    }

    private static readonly int[] Sin = Enumerable.Range(0, 360).Select(d => (int)Math.Round(Math.Sin(d * Math.PI / 180) * 1000)).ToArray();

    private static Vec2 Polar(int degrees, int lengthCm) =>
        new((int)((long)Sin[(degrees + 90) % 360] * lengthCm / 1000), (int)((long)Sin[degrees % 360] * lengthCm / 1000));

    internal bool RanOut => _ranOut;

    /// <summary>Shells in the air: where and when they land.</summary>
    internal IReadOnlyList<(Vec2 At, long Impact, bool Ranging)> Shells => _inFlight;
}

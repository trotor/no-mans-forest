using Nmf.Sim.Units;

namespace Nmf.Client.Art;

public readonly record struct AnimationFrame(string Animation, int Direction, int Frame);

/// <summary>Per-unit facing and walk-cycle state, driven by interpolated positions each rendered frame.</summary>
public sealed class UnitAnimator
{
    // Movement is accumulated until it passes this, so slow crawling (a fraction of a cm per frame) still turns the soldier.
    private const double TurnThresholdCm = 3.0;

    private readonly Dictionary<UnitId, State> _states = [];

    public void Update(Unit unit, (double X, double Y) positionCm, bool paused)
    {
        if (!_states.TryGetValue(unit.Id, out var state))
        {
            state = new State
            {
                Direction = unit.Side == Side.Red ? Facing.South : Facing.North,
                X = positionCm.X,
                Y = positionCm.Y,
            };
            _states[unit.Id] = state;
            return;
        }
        if (paused)
            return;

        double dx = positionCm.X - state.X, dy = positionCm.Y - state.Y;
        double moved = Math.Sqrt(dx * dx + dy * dy);
        state.TurnX += dx;
        state.TurnY += dy;
        if (state.TurnX * state.TurnX + state.TurnY * state.TurnY > TurnThresholdCm * TurnThresholdCm)
        {
            state.Direction = Facing.FromDelta(state.TurnX, state.TurnY);
            state.TurnX = 0;
            state.TurnY = 0;
        }
        state.DistanceCm += moved;
        state.Moving = unit.IsMoving || moved > 0.01;
        state.X = positionCm.X;
        state.Y = positionCm.Y;
    }

    /// <summary>Last facing of a unit, or <paramref name="fallback"/> if it was never animated.</summary>
    public int DirectionOf(UnitId id, int fallback) => _states.TryGetValue(id, out var s) ? s.Direction : fallback;

    public AnimationFrame Current(Unit unit, SpriteSheet sheet)
    {
        var state = _states.TryGetValue(unit.Id, out var s) ? s : new State { Direction = unit.Side == Side.Red ? Facing.South : Facing.North };
        string animation = unit.Stance switch
        {
            Stance.Prone => state.Moving ? "crawl" : "prone",
            Stance.Crouching => "crouch",
            _ => !state.Moving ? "idle" : unit.MoveMode == MoveMode.Run ? "run" : "walk",
        };
        var info = sheet.Animations[animation];
        int frame = info.StrideCm <= 0 ? 0 : (int)(state.DistanceCm / (info.StrideCm / (double)info.Frames)) % info.Frames;
        return new AnimationFrame(animation, state.Direction, frame);
    }

    private sealed class State
    {
        public int Direction;
        public double X;
        public double Y;
        public double DistanceCm;
        public double TurnX;
        public double TurnY;
        public bool Moving;
    }
}

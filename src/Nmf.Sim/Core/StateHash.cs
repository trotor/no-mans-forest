using Nmf.Sim.Units;

namespace Nmf.Sim.Core;

/// <summary>Order-sensitive 64-bit fingerprint of the simulation state, used for determinism checks.</summary>
public static class StateHash
{
    public static ulong Compute(Simulation sim)
    {
        var h = new Fnv1a64();
        h.Add((ulong)sim.Tick);
        var rng = sim.Rng.State;
        h.Add(rng.State);
        h.Add(rng.Increment);
        foreach (var unit in sim.Units)
        {
            h.Add(unit.Id.Value);
            h.Add((int)unit.Side);
            h.Add(unit.SpeedCmPerTick);
            h.Add(unit.Position.X);
            h.Add(unit.Position.Y);
            h.Add((int)unit.Stance);
            h.Add(unit.TargetStance is { } targetStance ? (int)targetStance + 1 : 0);
            h.Add(unit.StanceTicksLeft);
            h.Add((int)unit.MoveMode);
            h.Add(unit.IsMoving ? 1 : 0);
            if (unit.MoveTarget is { } target)
            {
                h.Add(1);
                h.Add(target.X);
                h.Add(target.Y);
            }
            else
            {
                h.Add(0);
            }
            h.Add(unit.PathIndex);
            h.Add(unit.Path.Count);
            foreach (var waypoint in unit.Path)
            {
                h.Add(waypoint.X);
                h.Add(waypoint.Y);
            }
        }
        foreach (var side in new[] { Side.Blue, Side.Red })
        {
            foreach (var contact in sim.Knowledge(side).Contacts)
            {
                h.Add((int)side);
                h.Add(contact.Target.Value);
                h.Add((int)contact.Level);
                h.Add(contact.Position.X);
                h.Add(contact.Position.Y);
                h.Add(contact.Progress);
                h.Add((ulong)contact.LastUpdateTick);
            }
        }
        return h.Value;
    }

    private sealed class Fnv1a64
    {
        private const ulong Prime = 1099511628211UL;
        public ulong Value { get; private set; } = 14695981039346656037UL;

        public void Add(int value) => Add((ulong)(uint)value);

        public void Add(ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                Value ^= (byte)(value >> (i * 8));
                Value = unchecked(Value * Prime);
            }
        }
    }
}

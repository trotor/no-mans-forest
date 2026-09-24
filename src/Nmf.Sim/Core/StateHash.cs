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
            h.Add(unit.Position.X);
            h.Add(unit.Position.Y);
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

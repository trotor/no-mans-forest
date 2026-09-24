namespace Nmf.Sim.Core;

public readonly record struct RngState(ulong State, ulong Increment);

/// <summary>
/// PCG32 (XSH-RR, 64-bit state). Platform independent and fully deterministic;
/// the only source of randomness inside the simulation.
/// </summary>
public sealed class Rng
{
    public const ulong DefaultStream = 54;
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _increment;

    public Rng(ulong seed, ulong stream = DefaultStream)
    {
        _state = 0;
        _increment = (stream << 1) | 1UL;
        NextUInt();
        _state = unchecked(_state + seed);
        NextUInt();
    }

    private Rng(RngState state)
    {
        _state = state.State;
        _increment = state.Increment;
    }

    public RngState State => new(_state, _increment);

    public static Rng FromState(RngState state)
    {
        if ((state.Increment & 1UL) == 0)
            throw new ArgumentException("RNG increment must be odd.", nameof(state));
        return new Rng(state);
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * Multiplier + _increment);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
    }

    /// <summary>Unbiased integer in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Bound must be positive.");
        uint bound = (uint)maxExclusive;
        uint threshold = unchecked(0u - bound) % bound;
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold)
                return (int)(r % bound);
        }
    }

    /// <summary>Unbiased integer in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        long span = (long)maxExclusive - minInclusive;
        if (span <= 0 || span > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), $"Invalid range [{minInclusive}, {maxExclusive}).");
        return minInclusive + NextInt((int)span);
    }

    /// <summary>True with probability permille/1000. Always consumes exactly one NextInt(1000) draw.</summary>
    public bool Chance(int permille) => NextInt(1000) < permille;
}

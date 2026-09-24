namespace Nmf.Sim.Time;

/// <summary>
/// Converts variable real frame time into whole simulation steps plus an interpolation factor for rendering.
/// Presentation-side only: it is not part of the deterministic simulation state.
/// </summary>
public sealed class FixedStepClock
{
    public const double StepSeconds = 1.0 / SimConstants.TicksPerSecond;

    private double _accumulator;
    private double _timeScale = 1.0;

    public FixedStepClock(int maxStepsPerFrame = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStepsPerFrame, 1);
        MaxStepsPerFrame = maxStepsPerFrame;
    }

    public int MaxStepsPerFrame { get; }

    public bool Paused { get; set; }

    public double TimeScale
    {
        get => _timeScale;
        set
        {
            if (!(value >= 0) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Time scale must be a finite value >= 0.");
            _timeScale = value;
        }
    }

    /// <summary>Fraction (0..1) of the next step already elapsed; use it to interpolate sprites.</summary>
    public double Alpha => _accumulator / StepSeconds;

    /// <summary>Returns how many simulation steps to run for a frame that took <paramref name="realDeltaSeconds"/>.</summary>
    public int Advance(double realDeltaSeconds)
    {
        if (!(realDeltaSeconds >= 0) || double.IsInfinity(realDeltaSeconds))
            throw new ArgumentOutOfRangeException(nameof(realDeltaSeconds), realDeltaSeconds, "Frame time must be a finite value >= 0.");
        if (Paused)
            return 0;

        _accumulator += realDeltaSeconds * _timeScale;
        int steps = (int)Math.Min(Math.Floor(_accumulator / StepSeconds), MaxStepsPerFrame + 1);
        if (steps > MaxStepsPerFrame)
        {
            // After a long hitch, drop the backlog instead of trying to catch up (avoids a spiral of slow frames).
            _accumulator = 0;
            return MaxStepsPerFrame;
        }

        _accumulator = Math.Max(0, _accumulator - steps * StepSeconds);
        return steps;
    }
}

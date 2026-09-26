using Nmf.Sim.Time;

namespace Nmf.Client.Mission;

/// <summary>
/// Full-screen papers (orders, map) stop the war while any of them is open; when the last one closes the game
/// goes back to how it was before the first opened (paused by the player, or running).
/// </summary>
public sealed class OverlayPause(FixedStepClock clock)
{
    private readonly HashSet<string> _open = [];
    private bool _pausedBefore;

    public bool AnyOpen => _open.Count > 0;

    public void Opened(string overlay)
    {
        if (_open.Count == 0)
            _pausedBefore = clock.Paused;
        _open.Add(overlay);
        clock.Paused = true;
    }

    public void Closed(string overlay)
    {
        if (!_open.Remove(overlay) || _open.Count > 0)
            return;
        clock.Paused = _pausedBefore;
    }
}

using Nmf.Sim.Core;
using Nmf.Sim.Events;

namespace Nmf.Client.Mission;

/// <summary>Messages for things our men hear rather than see.</summary>
public static class Alerts
{
    /// <summary>A leader's "Urraa!" carries this far.</summary>
    public const int ShoutHeardCm = 30_000;

    /// <summary>The enemy's call to counterattack, if one of our men (still in action) is near enough to hear it.</summary>
    public static string? Counterattack(CounterattackStarted shout, IEnumerable<Vec2> ownMen, string language)
    {
        long heardSq = (long)ShoutHeardCm * ShoutHeardCm;
        if (!ownMen.Any(p => (p - shout.At).LengthSquared <= heardSq))
            return null;
        return language == "fi" ? "Kuuluu huuto: \"Urraa!\" — vihollinen hyökkää!" : "A shout: \"Urraa!\" — the enemy is attacking!";
    }
}

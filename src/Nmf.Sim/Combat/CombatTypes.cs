namespace Nmf.Sim.Combat;

public enum WoundLevel : byte
{
    None,
    Light,
    Serious,
    Incapacitated,
    Dead,
}

public enum MoraleState : byte
{
    Steady,
    Pinned,
    Broken,
}

public enum FirePolicy : byte
{
    FireAtWill,
    ReturnFire,
    HoldFire,
}

public enum CombatAction : byte
{
    None,
    Aiming,
    Firing,
    Recovering,
    Reloading,
    Throwing,
    Melee,
    Looting,
    /// <summary>Getting a man who cannot move up onto his back (spec 2026-10-10-carrying-design §2).</summary>
    Lifting,
    /// <summary>Binding a man's wound (spec 2026-10-11-campaign-design §9b).</summary>
    FirstAid,
}

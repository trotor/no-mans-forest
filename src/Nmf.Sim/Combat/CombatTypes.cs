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
}

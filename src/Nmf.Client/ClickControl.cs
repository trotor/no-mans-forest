using Nmf.Sim.Core;

namespace Nmf.Client;

public enum ClickResult
{
    None,
    Selected,
    SelectedSquad,
    FireOrdered,
    AssaultOrdered,
    LootOrdered,
    MoveOrdered,
    Cleared,
}

/// <summary>What a click did, and where (for the on-screen marker).</summary>
public readonly record struct ClickOutcome(ClickResult Result, Vec2 Point);

using Nmf.Sim.Core;

namespace Nmf.Client;

public enum ClickResult
{
    None,
    Selected,
    SelectedSquad,
    FireOrdered,
    AssaultOrdered,
    AttackOrdered,
    AreaFireOrdered,
    LootOrdered,
    MoveOrdered,
    AttackForwardOrdered,
    /// <summary>The attack preview opened: nothing ordered yet (spec 2026-10-10-plan-preview-design).</summary>
    AttackPreviewOpened,
    /// <summary>A man sent to carry one of ours who cannot move (spec 2026-10-10-carrying-design §3).</summary>
    CarryOrdered,
    Queued,
    Cleared,
}

public enum ClickTargetKind { None, Own, Enemy, Body, Mark, Ground, Wounded }

/// <summary>What a click lands on (spec 2026-10-10-controls-design §2): the same for the preview and the click itself.</summary>
public readonly record struct ClickTarget(ClickTargetKind Kind, Vec2 Point, Nmf.Sim.Units.Unit? Unit, Nmf.Sim.Vision.Contact? Contact);

/// <summary>What a click did, and where (for the on-screen marker).</summary>
public readonly record struct ClickOutcome(ClickResult Result, Vec2 Point);

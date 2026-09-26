using Nmf.Sim.Units;

namespace Nmf.Sim.Core;

/// <summary>Order-sensitive 64-bit fingerprint of the simulation state, used for determinism checks.</summary>
public static class StateHash
{
    /// <summary>Stable (not randomized per process) FNV-1a of a string; 0 for null.</summary>
    private static ulong Text(string? text)
    {
        if (text is null)
            return 0;
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }

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
            foreach (char c in unit.Weapon?.Id ?? "")
                h.Add(c);
            h.Add(unit.Ammo);
            h.Add(unit.Magazines);
            h.Add(Text(unit.Weapon?.Id));
            h.Add(Text(unit.GrenadeType?.Id));
            h.Add(unit.Looted ? 1 : 0);
            h.Add(unit.LootTarget is { } lootTarget ? lootTarget.Value + 1 : 0);
            h.Add(unit.Items.Count);
            foreach (var item in unit.Items)
                h.Add(Text(item.Id));
            h.Add(unit.IsLeader ? 1 : 0);
            h.Add(unit.LeaderQualityPct);
            h.Add((int)unit.Wound);
            h.Add((ulong)unit.WoundTick);
            h.Add(unit.Suppression);
            h.Add(unit.Morale);
            h.Add((int)unit.MoraleState);
            h.Add((int)unit.FirePolicy);
            h.Add((int)unit.Action);
            h.Add(unit.ActionTicksLeft);
            h.Add(unit.RoundsLeftInBurst);
            h.Add(unit.Target?.Value ?? 0);
            h.Add(unit.OrderedTarget?.Value ?? 0);
            h.Add((ulong)unit.LastShotTick);
            h.Add(unit.Retreated ? 1 : 0);
            h.Add(unit.MovedSinceVisionUpdate ? 1 : 0);
            h.Add(unit.Grenades);
            h.Add((ulong)unit.LastThrowTick);
            h.Add(unit.ThrowTarget?.Value ?? 0);
            h.Add(unit.MeleeOpponent?.Value ?? 0);
            h.Add(unit.IsCaptured ? 1 : 0);
            h.Add(unit.AssaultTarget?.Value ?? 0);
            h.Add(unit.AutoPace ? 1 : 0);
            h.Add(unit.StanceOrdered ? 1 : 0);
            h.Add((ulong)unit.LastSuppressedTick);
            h.Add(unit.Nerve);
            h.Add(unit.BaseMorale);
            h.Add(unit.Marksmanship);
            h.Add(Text(unit.Name));
            h.Add(unit.TakingCover ? 1 : 0);
            h.Add(unit.CoverReactionPending ? 1 : 0);
            h.Add(unit.HoldsCoverStance ? 1 : 0);
            h.Add(unit.AttackGroupId ?? -1);
            h.Add((int)unit.AttackRole);
            h.Add((unit.BoundIssued ? 1 : 0) | (unit.BoundSettled ? 2 : 0));
            h.Add(unit.CoverThreat is { } threat ? threat.X + 1 : 0);
            h.Add(unit.CoverThreat?.Y ?? 0);
            h.Add(unit.MeleeSurprise ? 1 : 0);
            h.Add(unit.AssaultGoal.X);
            h.Add(unit.AssaultGoal.Y);
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
        foreach (var group in sim.AttackGroups)
        {
            h.Add(group.Id);
            h.Add(group.Target.Value);
            h.Add(group.BoundingTeam);
            h.Add((ulong)group.BoundStartTick);
            h.Add(group.LastKnown is { } known ? known.X + 1 : 0);
            h.Add(group.LastKnown?.Y ?? 0);
            h.Add((ulong)(group.CloseSince ?? -1));
            h.Add(group.StalledSwaps);
            foreach (var member in group.Members)
            {
                h.Add(member.Value);
                h.Add(group.Team.TryGetValue(member, out int team) ? team : -1);
            }
        }
        foreach (var grenade in sim.Grenades)
        {
            h.Add(grenade.Id);
            h.Add(grenade.Thrower.Value);
            h.Add(grenade.From.X);
            h.Add(grenade.From.Y);
            h.Add(grenade.Landing.X);
            h.Add(grenade.Landing.Y);
            h.Add((ulong)grenade.ThrowTick);
            h.Add(grenade.Exploded ? 1 : 0);
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

using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.Vision;

public enum ContactLevel : byte
{
    Unknown = 0,
    Suspected = 1,
    LastKnown = 2,
    Visible = 3,
}

/// <summary>What one side believes about one enemy unit.</summary>
public sealed class Contact
{
    internal Contact(UnitId target) => Target = target;

    public UnitId Target { get; }
    public ContactLevel Level { get; internal set; }

    /// <summary>Visible: current position. LastKnown: where last seen. Suspected: centre of the 10 m square it was heard in.</summary>
    public Vec2 Position { get; internal set; }

    public long LastUpdateTick { get; internal set; }

    /// <summary>Spotting progress toward <see cref="VisionRules.SpottedThreshold"/>.</summary>
    public int Progress { get; internal set; }
}

public sealed class SideKnowledge
{
    private readonly SortedDictionary<int, Contact> _contacts = [];

    internal SideKnowledge()
    {
    }

    /// <summary>All contacts in unit-id order.</summary>
    public IEnumerable<Contact> Contacts => _contacts.Values;

    public Contact? Get(UnitId id) => _contacts.TryGetValue(id.Value, out var contact) ? contact : null;

    public ContactLevel LevelOf(UnitId id) => Get(id)?.Level ?? ContactLevel.Unknown;

    internal Contact GetOrAdd(UnitId id)
    {
        if (!_contacts.TryGetValue(id.Value, out var contact))
        {
            contact = new Contact(id);
            _contacts.Add(id.Value, contact);
        }
        return contact;
    }
}

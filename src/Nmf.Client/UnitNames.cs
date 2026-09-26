using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>Display names for soldiers: the first is the squad leader.</summary>
public static class UnitNames
{
    private static readonly string[] Finnish =
        ["Alik. Korpela", "Kpl. Virtanen", "Sotm. Mäkinen", "Sotm. Laine", "Sotm. Heikkinen", "Sotm. Nieminen", "Sotm. Järvinen", "Sotm. Salonen"];

    private static readonly string[] Soviet =
        ["Ml. serž. Petrov", "Efr. Ivanov", "Kr-ts Smirnov", "Kr-ts Kuznetsov", "Kr-ts Popov", "Kr-ts Sokolov", "Kr-ts Lebedev", "Kr-ts Kozlov"];

    public static string For(Side side, int index)
    {
        var names = side == Side.Blue ? Finnish : Soviet;
        string name = names[index % names.Length];
        return index < names.Length ? name : $"{name} {index / names.Length + 1}";
    }

    /// <summary>The man's roster name if the mission gave him one, else the default name for his place.</summary>
    public static string Of(Unit unit, int index) => unit.Name ?? For(unit.Side, index);

    public static int PortraitIndex(int index) => index % 8;
}

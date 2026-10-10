using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>The modifier keys held (Ctrl means Cmd on macOS; Forward is E held, attack forward).</summary>
public readonly record struct Modifiers(bool Shift = false, bool Alt = false, bool Ctrl = false, bool Forward = false);

public enum CursorKind { Arrow, Move, Cross, Select, Search }

/// <summary>
/// What a click and a double click at the point would do (spec 2026-10-10-controls-design §2), for the cursor and its
/// hint. Uses the click's own target choice (<see cref="GameSession.Resolve"/>), so the two cannot disagree.
/// </summary>
public static class ClickPreview
{
    public sealed record Result(CursorKind Cursor, string Click, string? Double, ClickResult ClickResult, ClickResult DoubleResult);

    public static Result Describe(GameSession session, Nmf.Sim.Core.Vec2 point, Modifiers mods, int radiusCm, string language)
    {
        bool fi = language == "fi";
        string T(string f, string e) => fi ? f : e;
        var single = session.Resolve(point, radiusCm, doubleClick: false);
        var twice = session.Resolve(point, radiusCm, doubleClick: true);
        bool canOrder = session.CommandedIds.Count > 0;

        if (single.Kind == ClickTargetKind.Own)
            return new Result(CursorKind.Select, T("valitse", "select"), T("koko ryhmä", "whole squad"), ClickResult.Selected, ClickResult.SelectedSquad);
        if (single.Kind == ClickTargetKind.None || !canOrder)
            return new Result(CursorKind.Arrow, "", null, ClickResult.None, ClickResult.None);

        (CursorKind Cursor, string Click, string? Double, ClickResult One, ClickResult Two) what = single.Kind switch
        {
            ClickTargetKind.Enemy when mods.Ctrl => (CursorKind.Cross, T("aluetuli", "area fire"), T("suora rynnäkkö", "straight assault"),
                AreaFire(session, single.Unit!.Position), ClickResult.AssaultOrdered),
            ClickTargetKind.Enemy => (CursorKind.Cross, T("ammu", "fire"), mods.Alt ? T("pitkä tuli", "long fire") : T("hyökkää", "attack"),
                ClickResult.FireOrdered, ClickResult.AttackOrdered),
            ClickTargetKind.Mark => (CursorKind.Cross, T("aluetuli", "area fire"), mods.Ctrl ? null : mods.Alt ? T("pitkä tuli", "long fire") : T("hyökkää", "attack"),
                session.AreaFirers(single.Contact!.Position).Count > 0 ? ClickResult.AreaFireOrdered : ClickResult.MoveOrdered,
                mods.Ctrl ? AreaFire(session, single.Contact.Position) : ClickResult.AttackOrdered),
            _ when mods.Ctrl => (CursorKind.Cross, T("aluetuli", "area fire"), null, AreaFire(session, point), AreaFire(session, point)),
            _ when mods.Forward => (CursorKind.Move, T("hyökkää eteenpäin", "attack forward"), null, ClickResult.AttackForwardOrdered, ClickResult.AttackForwardOrdered),
            _ when mods.Alt => (CursorKind.Move, T("hiivi", "sneak"), T("ryömi", "crawl"), ClickResult.MoveOrdered, ClickResult.MoveOrdered),
            _ => (CursorKind.Move, T("mene", "go"), T("juokse", "run"), ClickResult.MoveOrdered, ClickResult.MoveOrdered),
        };
        if (twice.Kind == ClickTargetKind.Body && single.Kind == ClickTargetKind.Ground)
            what = (CursorKind.Search, what.Click, T("tutki", "search"), what.One, ClickResult.LootOrdered);

        if (mods.Shift)
        {
            int place = QueuePlace(session);
            string prefix = T($"jonoon ({place}.): ", $"queue ({place}.): ");
            return new Result(what.Cursor, prefix + what.Click, what.Double, ClickResult.Queued, ClickResult.Queued);
        }
        return new Result(what.Cursor, what.Click, what.Double, what.One, what.Two);
    }

    private static ClickResult AreaFire(GameSession session, Nmf.Sim.Core.Vec2 place) =>
        session.AreaFirers(place).Count > 0 ? ClickResult.AreaFireOrdered : ClickResult.None;

    /// <summary>Which place in their queue a Shift order of the commanded men would take.</summary>
    private static int QueuePlace(GameSession session)
    {
        var men = session.CommandedIds.OrderBy(id => id.Value).ToList();
        var group = session.Queue.Groups.FirstOrDefault(g => g.Men.SequenceEqual(men));
        return (group?.Items.Count ?? 0) + 1;
    }
}

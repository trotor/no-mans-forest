using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Client;

public enum MenuAction { Order, Select, SelectSquad, ClearSelection, PlanAttack }

/// <summary>One row of the right-click menu: an order (now, or queued), or a selection.</summary>
public sealed record MenuItem(string Text, MenuAction Action, QueuedOrder? Order = null, bool Queue = false, UnitId? Unit = null);

/// <summary>
/// The right-click menu (spec 2026-10-10-controls-design §3): the alternatives for what is under the cursor, each order
/// also as "+ to the queue". Its orders are the very orders the clicks give (<see cref="GameSession.QueuedFor"/>).
/// </summary>
public static class ContextMenu
{
    public static IReadOnlyList<MenuItem> Items(GameSession session, Vec2 point, int radiusCm, string language)
    {
        bool fi = language == "fi";
        string T(string f, string e) => fi ? f : e;
        var single = session.Resolve(point, radiusCm, doubleClick: false);
        var twice = session.Resolve(point, radiusCm, doubleClick: true);
        var items = new List<MenuItem>();
        void Order(string text, QueuedOrder? order)
        {
            if (order is null)
                return;
            items.Add(new MenuItem(text, MenuAction.Order, order));
            items.Add(new MenuItem("   + " + T("jonoon", "to the queue"), MenuAction.Order, order, Queue: true));
        }

        switch (single.Kind)
        {
            case ClickTargetKind.Own:
                items.Add(new MenuItem(T("Valitse", "Select"), MenuAction.Select, Unit: single.Unit!.Id));
                items.Add(new MenuItem(T("Koko ryhmä", "Whole squad"), MenuAction.SelectSquad, Unit: single.Unit.Id));
                break;
            case ClickTargetKind.Enemy:
                Order(T("Ammu", "Fire"), session.QueuedFor(single, point, false, false, false, false));
                Attacks(session, single, point, Order, items.Add, T);
                Order(T("Suora rynnäkkö", "Straight assault"), session.QueuedFor(single, point, true, false, true, false));
                Order(T("Aluetuli", "Area fire"), session.QueuedFor(single, point, false, false, true, false));
                break;
            case ClickTargetKind.Mark:
                Attacks(session, single, point, Order, items.Add, T);
                Order(T("Aluetuli", "Area fire"), session.QueuedFor(single, point, false, false, true, false));
                break;
            case ClickTargetKind.Ground when twice.Kind == ClickTargetKind.Body:
                Order(T("Tutki", "Search"), session.QueuedFor(twice, point, true, false, false, false));
                goto case ClickTargetKind.Ground;
            case ClickTargetKind.Ground:
                Order(T("Mene tänne", "Go here"), session.QueuedFor(single, point, false, false, false, false));
                Order(T("Juokse tänne", "Run here"), session.QueuedFor(single, point, true, false, false, false));
                Order(T("Hiivi tänne", "Sneak here"), session.QueuedFor(single, point, false, true, false, false));
                Order(T("Ryömi tänne", "Crawl here"), session.QueuedFor(single, point, true, true, false, false));
                Order(T("Hyökkää eteenpäin tänne", "Attack forward to here"), session.QueuedFor(single, point, false, false, false, true));
                Order(T("Aluetuli tänne", "Area fire here"), session.QueuedFor(single, point, false, false, true, false));
                Order(T("Ota asema (katse tänne)", "Take position (facing here)"), new QueuedOrder(QueuedKind.TakePosition, point));
                break;
        }
        if (session.CommandedIds.Count == 0)
            items.RemoveAll(i => i.Action is MenuAction.Order or MenuAction.PlanAttack);
        if (single.Kind == ClickTargetKind.Own || session.Selection.Ids.Count > 0 || items.Count == 0)
            items.Add(new MenuItem(T("Tyhjennä valinta", "Clear the selection"), MenuAction.ClearSelection));
        return items;
    }

    private static void Attacks(GameSession session, ClickTarget target, Vec2 point, Action<string, QueuedOrder?> order, Action<MenuItem> add,
        Func<string, string, string> t)
    {
        var best = session.QueuedFor(target, point, true, false, false, false);
        if (best?.Target is { } enemy && session.Tactics.Count > 0)
            add(new MenuItem(t("Suunnittele hyökkäys…", "Plan the attack…"), MenuAction.PlanAttack, Unit: enemy));
        order(t("Hyökkää (paras tapa)", "Attack (best way)"), best);
        foreach (var tactic in session.Tactics)
            order(t("Hyökkää: ", "Attack: ") + tactic.Name(t("fi", "en")), best is null ? null : best with { Tactic = tactic.Id });
    }

    /// <summary>Does the row: an order to the commanded men now (their queue is over) or added to their queue; or a selection.</summary>
    public static void Run(GameSession session, MenuItem item)
    {
        switch (item.Action)
        {
            case MenuAction.Select when item.Unit is { } id:
                session.Selection.Clear();
                session.Selection.Add(id);
                return;
            case MenuAction.SelectSquad when item.Unit is { } id && session.Sim.FindUnit(id) is { } man:
                session.SelectSquad(man.Squad);
                return;
            case MenuAction.ClearSelection:
                session.Selection.Clear();
                return;
            case MenuAction.PlanAttack when item.Unit is { } target:
                session.OpenPreview(target);
                return;
        }
        if (item.Order is not { } order || session.CommandedIds.Count == 0)
            return;
        if (item.Queue)
        {
            session.Queue.Add(session, session.CommandedIds, order);
            return;
        }
        if (order.Kind == QueuedKind.AttackForward)
        {
            session.OrderAttackForward(order.Point);
            return;
        }
        session.Queue.ClearFor(session.CommandedIds);
        OrderQueue.Issue(session, order);
    }
}

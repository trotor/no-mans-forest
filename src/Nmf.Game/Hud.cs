using System;
using System.Globalization;
using System.Linq;
using Godot;
using Nmf.Client;
using Nmf.Sim.Vision;

namespace Nmf.Game;

public partial class Hud : CanvasLayer
{
    private const string Help =
        "LMB select / drag box · Shift adds · RMB move · Shift+RMB run · Alt+RMB crawl · 1/2/3 stand/crouch/prone · H halt\n" +
        "Space pause · +/- speed · WASD/arrows/middle drag/two-finger pan · wheel/pinch zoom · Tab all · Esc none · F reveal (debug)";

    private Label _label = null!;

    public GameSession Session { get; set; } = null!;

    public override void _Ready()
    {
        _label = new Label { Position = new Vector2(12, 8) };
        _label.AddThemeFontSizeOverride("font_size", 15);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 5);
        AddChild(_label);
        Refresh();
    }

    public void Refresh()
    {
        var contacts = Session.Knowledge.Contacts.ToList();
        int seen = contacts.Count(c => c.Level == ContactLevel.Visible);
        int heard = contacts.Count(c => c.Level == ContactLevel.Suspected);
        int lastKnown = contacts.Count(c => c.Level == ContactLevel.LastKnown);
        var t = Session.GameTime;
        string paused = Session.Clock.Paused ? "   [PAUSED]" : "";
        _label.Text = string.Create(CultureInfo.InvariantCulture,
            $"No Man's Forest – test skirmish   {(int)t.TotalMinutes:00}:{t.Seconds:00}   x{Session.Clock.TimeScale:0.##}{paused}\nSelected {Session.Selection.Count}   Contacts: {seen} seen, {heard} heard, {lastKnown} last known\n{Help}");
    }
}

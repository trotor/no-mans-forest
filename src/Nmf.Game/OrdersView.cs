using System.Linq;
using System.Text;
using Godot;
using Nmf.Client;
using Nmf.Client.Mission;

namespace Nmf.Game;

/// <summary>The mission orders on a sheet of paper: situation, task, the section with each man's qualities, objectives (B).</summary>
public partial class OrdersView : Control
{
    private static readonly Color Paper = new(0.92f, 0.88f, 0.77f);
    private static readonly Color Ink = new(0.16f, 0.12f, 0.08f);
    private static readonly Color PaperEdge = new(0.36f, 0.27f, 0.17f);

    private RichTextLabel _text = null!;
    private MissionMapPanel? _missionMap;
    private double _refresh;

    public GameSession Session { get; set; } = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        OffsetTop = Hud.TopBarHeight; // the top bar stays usable over the paper
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        var sheet = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        sheet.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Paper, BorderColor = PaperEdge, BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            ContentMarginLeft = 36, ContentMarginRight = 36, ContentMarginTop = 28, ContentMarginBottom = 24, ShadowSize = 12,
            ShadowColor = new Color(0, 0, 0, 0.5f),
        });
        // Scrolls when the orders are longer than the window is high.
        _text = new RichTextLabel { BbcodeEnabled = true, FitContent = false, ScrollActive = true, MouseFilter = MouseFilterEnum.Pass };
        _text.AddThemeColorOverride("default_color", Ink);
        _text.AddThemeFontSizeOverride("normal_font_size", 17);
        _text.AddThemeFontSizeOverride("bold_font_size", 17);
        // The orders on the left, the mission map beside them.
        var columns = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        columns.AddThemeConstantOverride("separation", 28);
        columns.AddChild(_text);
        if (Session.Mission is not null)
        {
            _missionMap = new MissionMapPanel { Session = Session };
            columns.AddChild(_missionMap);
        }
        sheet.AddChild(columns);
        centre.AddChild(sheet);
    }

    public void Open()
    {
        if (Session.Mission is null)
            return;
        var view = GetViewportRect().Size;
        float height = view.Y - Hud.TopBarHeight - 110;
        float mapSide = Mathf.Clamp(Mathf.Min(height - 40, view.X * 0.42f), 280, 640);
        _text.CustomMinimumSize = new Vector2(Mathf.Min(760, view.X - mapSide - 160), height);
        if (_missionMap is not null)
            _missionMap.CustomMinimumSize = new Vector2(mapSide, mapSide + 40);
        _text.Text = Build();
        _built = Signature();
        Visible = true;
    }

    public void Close()
    {
        if (!Visible)
            return;
        Visible = false;
    }

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    public override void _Process(double delta)
    {
        if (!Visible || (_refresh -= delta) > 0)
            return;
        _refresh = 0.5;
        // Rebuilt only when something on it changed, so the reader's scroll position stays put.
        var signature = Signature();
        if (signature == _built)
            return;
        _built = signature;
        _text.Text = Build();
    }

    private string _built = "";

    private string Signature() =>
        $"{Session.Tracker?.DoneCount}/{Session.Tracker?.Result}/{Session.OwnUnits.Count(u => u.IsOutOfAction)}";

    private string Build()
    {
        var mission = Session.Mission!;
        string lang = Session.Language;
        bool fi = lang == "fi";
        var sb = new StringBuilder();
        sb.Append("[font_size=32][b]").Append(Escape(mission.Title.In(lang))).Append("[/b][/font_size]\n");
        sb.Append("[i]").Append(Escape(mission.Date.In(lang))).Append("[/i]\n\n");
        sb.Append("[font_size=19][b]").Append(fi ? "Tavoitteet" : "Objectives").Append("[/b][/font_size]\n");
        foreach (var (text, done) in MissionPaper.Objectives(Session.Tracker!, lang))
            sb.Append("  ").Append(done ? "☑ " : "☐ ").Append(Escape(text)).Append('\n');
        if (Session.Tracker!.Result is { } success)
            sb.Append("[b]").Append(success ? (fi ? "TEHTÄVÄ SUORITETTU" : "MISSION ACCOMPLISHED") : (fi ? "TEHTÄVÄ EPÄONNISTUI" : "MISSION FAILED")).Append("[/b]\n");
        sb.Append('\n').Append(MarkdownLite.ToBbcode(mission.Briefing.In(lang))).Append("\n\n");
        sb.Append("[font_size=19][b]").Append(fi ? "Joukkue" : "Platoon").Append("[/b][/font_size]\n");
        foreach (var squad in Session.OwnUnits.GroupBy(u => u.Squad).OrderBy(g => g.Key))
        {
            sb.Append("[b]").Append(Escape(Session.SquadName(squad.Key, lang))).Append("[/b]\n");
            foreach (var unit in squad)
            {
                string line = Escape(MissionPaper.RosterLine(unit, w => w.Name, lang));
                sb.Append("  ").Append(unit.IsOutOfAction ? "[s]" + line + "[/s]" : line).Append('\n');
            }
        }
        sb.Append("\n[right][i]").Append(fi ? "B / Esc — sulje" : "B / Esc — close").Append("[/i][/right]");
        return sb.ToString();
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}

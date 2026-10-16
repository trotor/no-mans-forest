using Nmf.Content.Campaigns;
using Nmf.Sim.Mission;

namespace Nmf.Client.Campaign;

/// <summary>What the menu, the campaign view and the end screen say of the campaign (spec 2026-10-11-campaign-design §9a, "Pelaajalle").</summary>
public static class CampaignText
{
    public static string StatusText(ManStatus status, string language) => (status, language == "fi") switch
    {
        (ManStatus.Fit, true) => "kunnossa",
        (ManStatus.Fit, false) => "fit",
        (ManStatus.Recovering, true) => "toipuu",
        (ManStatus.Recovering, false) => "recovering",
        (_, true) => "kaatunut",
        _ => "fallen",
    };

    /// <summary>"Luku 2/2: Hylky".</summary>
    public static string Chapter(CampaignSpec spec, CampaignState state, Func<string, Localized?> missionTitle, string language)
    {
        string mission = spec.Chapters[state.Chapter].Mission;
        string title = missionTitle(mission)?.In(language) ?? mission;
        return $"{(language == "fi" ? "Luku" : "Chapter")} {state.Chapter + 1}/{spec.Chapters.Count}: {title}";
    }

    /// <summary>The menu's campaign button: start it, go on with it, or a word that it is over.</summary>
    public static string MenuButton(CampaignSpec spec, CampaignState? saved, Func<string, Localized?> missionTitle, string language)
    {
        bool fi = language == "fi";
        string title = spec.Title.In(language);
        var state = saved ?? CampaignState.New(spec);
        string head = state.Completed ? fi ? $"{title}: kampanja on päättynyt" : $"{title}: the campaign is over"
            : state.History.Count == 0 ? fi ? $"Aloita kampanja: {title}" : $"Start the campaign: {title}"
            : fi ? $"Jatka kampanjaa: {title}" : $"Continue the campaign: {title}";
        return $"{head}\n{Chapter(spec, state, missionTitle, language)}";
    }

    /// <summary>The story to read before the next mission: the intro at the start, what the last chapter came to, then this one.</summary>
    public static string Story(CampaignSpec spec, CampaignState state, string language)
    {
        var chapter = spec.Chapters[state.Chapter];
        if (state.Completed)
            return chapter.After.In(language);
        var parts = new List<string>();
        if (state.History.LastOrDefault() is { Success: false })
            parts.Add(language == "fi" ? "Edellinen yritys epäonnistui. Tappiot jäävät, ja luku pelataan uudelleen."
                                       : "The last attempt failed. The losses stand, and the chapter is played again.");
        else if (state.Chapter > 0)
            parts.Add(spec.Chapters[state.Chapter - 1].After.In(language));
        else
            parts.Add(spec.Intro.In(language));
        parts.Add(chapter.Before.In(language));
        return string.Join("\n\n", parts);
    }

    /// <summary>Alokas, Kokenut, Veteraani, Konkari (spec §9b).</summary>
    public static string LevelName(int level, string language) => (level, language == "fi") switch
    {
        (<= 1, true) => "Alokas",
        (<= 1, false) => "Recruit",
        (2, true) => "Kokenut",
        (2, false) => "Seasoned",
        (3, true) => "Veteraani",
        (3, false) => "Veteran",
        (_, true) => "Konkari",
        _ => "Old hand",
    };

    /// <summary>"Kokenut (33 p), tarkka-ampuja": his level, his points, his specialty if any.</summary>
    public static string Standing(CampaignManState man, string language)
    {
        string level = $"{LevelName(CampaignGrowth.Level(man.Xp), language)} ({man.Xp} {(language == "fi" ? "p" : "pts")})";
        return man.Specialty == Nmf.Sim.Units.Specialty.None ? level : $"{level}, {SpecialtyText.Name(man.Specialty, language)}";
    }

    /// <summary>The men who may take a specialty now, in roster order.</summary>
    public static IReadOnlyList<CampaignManState> Specialisable(CampaignState state) => state.Men.Where(CampaignGrowth.CanSpecialise).ToList();

    /// <summary>"Alik. Korpela — Kokenut (30 p): valitse erikoistuminen".</summary>
    public static string SpecialiseLine(CampaignManState man, string language) =>
        $"{man.Name} — {Standing(man, language)}: {(language == "fi" ? "valitse erikoistuminen" : "choose a specialty")}";

    /// <summary>Who goes, slot by slot: "Alik. Korpela (johtaja) — Suomi KP/-31 — Kokenut (33 p), tarkka-ampuja".</summary>
    public static IReadOnlyList<string> Lineup(MissionSpec mission, CampaignState state, Func<string, string> weaponName, string language)
    {
        bool fi = language == "fi";
        var fill = CampaignMission.Fill(mission, state);
        var weapons = CampaignLineup.Weapons(mission, state);
        var lines = new List<string>();
        for (int i = 0; i < fill.Count; i++)
        {
            if (fill[i] is not { } man)
                continue;
            string leader = mission.Player[i].Leader ? fi ? " (johtaja)" : " (leader)" : "";
            lines.Add($"{man.Name}{leader} — {weaponName(weapons[i] ?? mission.Player[i].Weapon)} — {Standing(man, language)}");
        }
        return lines;
    }

    /// <summary>The men who stay behind: those not needed, the recovering, the fallen.</summary>
    public static IReadOnlyList<string> Others(MissionSpec mission, CampaignState state, string language)
    {
        bool fi = language == "fi";
        var going = CampaignMission.Fill(mission, state).OfType<CampaignManState>().ToHashSet();
        var lines = new List<string>();
        Add(fi ? "Odottaa" : "Waiting", state.Men.Where(m => m.Status == ManStatus.Fit && !going.Contains(m)));
        Add(fi ? "Toipuu" : "Recovering", state.Men.Where(m => m.Status == ManStatus.Recovering && !going.Contains(m)));
        Add(fi ? "Kaatuneet" : "Fallen", state.Men.Where(m => m.Status == ManStatus.Fallen));
        return lines;

        void Add(string label, IEnumerable<CampaignManState> men)
        {
            var names = men.Select(m => m.Name).ToList();
            if (names.Count > 0)
                lines.Add($"{label}: {string.Join(", ", names)}");
        }
    }

    /// <summary>The end screen's lines: what each man earned and where he stands.</summary>
    public static IReadOnlyList<string> Report(IReadOnlyList<ManReport> reports, string language)
    {
        bool fi = language == "fi";
        return reports.Select(r =>
        {
            if (r.Status == ManStatus.Fallen)
                return $"{r.Name}: {(fi ? "kaatui" : "fell")}";
            string deeds = r.Hits > 0 || r.Kills > 0 ? fi ? $" (osumia {r.Hits}, kaatoja {r.Kills})" : $" (hits {r.Hits}, kills {r.Kills})" : "";
            string stands = r.Status == ManStatus.Recovering
                ? fi ? "toipuu, ei mukana seuraavassa tehtävässä" : "recovering, out of the next mission"
                : StatusText(r.Status, language);
            string up = r.LevelAfter > r.LevelBefore ? $" — {(fi ? "nyt" : "now")} {LevelName(r.LevelAfter, language)}" : "";
            return $"{r.Name}: +{r.Xp} {(fi ? "kokemusta" : "XP")}{deeds} — {stands}{up}";
        }).ToList();
    }

    public static string NobodyGoes(string language) => language == "fi"
        ? "Kaikki paikat on jätetty tyhjiksi. Valitse ainakin yksi lähtijä."
        : "Every slot is left empty. Pick at least one man to go.";

    public static string NobodyLeft(string language) => language == "fi"
        ? "Joukkueesta ei ole ketään jäljellä. Aloita kampanja alusta."
        : "Nobody of the platoon is left. Start the campaign again.";

    /// <summary>What a slot is (spec §9c): its squad, and leader, radio or ahkio when it is one: "Iskuryhmä, johtaja".</summary>
    public static string SlotLabel(MissionSpec mission, int slot, string language)
    {
        bool fi = language == "fi";
        var s = mission.Player[slot];
        string? squadId = s.Squad ?? mission.SquadNames.Keys.FirstOrDefault();
        var parts = new List<string>();
        if (squadId is not null && mission.SquadNames.TryGetValue(squadId, out var squad))
            parts.Add(squad.In(language));
        if (s.Leader)
            parts.Add(fi ? "johtaja" : "leader");
        if (mission.Support?.Mortar?.Radio == s.Name)
            parts.Add("radio");
        if (s.Ahkio)
            parts.Add("ahkio");
        return string.Join(", ", parts);
    }

    /// <summary>"Joukkueessa on 2 avointa paikkaa. Valitse täydennysmies:".</summary>
    public static string Vacancies(int open, string language) => language == "fi"
        ? $"Joukkueessa on {open} {(open == 1 ? "avoin paikka" : "avointa paikkaa")}. Valitse täydennysmies:"
        : $"The platoon has {open} {(open == 1 ? "place" : "places")} open. Choose a replacement:";

    /// <summary>A replacement on offer: "Sotm. Heikkinen: hermo 62, ammunta 70, johtaminen 20, kokemus 41".</summary>
    public static string CandidateLine(CampaignMan man, string language) => language == "fi"
        ? $"{man.Name}: hermo {man.Nerve}, ammunta {man.Marksmanship}, johtaminen {man.Leadership}, kokemus {man.Experience}"
        : $"{man.Name}: nerve {man.Nerve}, shooting {man.Marksmanship}, leading {man.Leadership}, experience {man.Experience}";

    /// <summary>"Asevarasto: Suomi KP/-31 ×1"; null with nothing spare.</summary>
    public static string? Armoury(CampaignState state, Func<string, string> weaponName, string language) =>
        Counted(state.Armoury.Where(a => a.Value > 0).OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => (a.Key, a.Value)),
            language == "fi" ? "Asevarasto" : "Spare weapons", weaponName);

    /// <summary>The end screen's line of weapons brought back: "Sotasaalis: PPSh-41 ×2"; null when none.</summary>
    public static string? Captured(IReadOnlyList<string> weapons, Func<string, string> weaponName, string language) =>
        Counted(weapons.GroupBy(w => w).Select(g => (g.Key, g.Count())), language == "fi" ? "Sotasaalis" : "Taken from the enemy", weaponName);

    private static string? Counted(IEnumerable<(string Weapon, int Count)> weapons, string label, Func<string, string> weaponName)
    {
        var parts = weapons.Select(w => $"{weaponName(w.Weapon)} ×{w.Count}").ToList();
        return parts.Count == 0 ? null : $"{label}: {string.Join(", ", parts)}";
    }
}

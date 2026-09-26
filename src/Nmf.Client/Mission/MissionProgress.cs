using System.Text.Json;
using System.Text.Json.Serialization;
using Nmf.Sim.Combat;
using Nmf.Sim.Vision;

namespace Nmf.Client.Mission;

/// <summary>How a mission went (spec 2026-09-26-mission-menu-design §2).</summary>
public sealed record MissionResult(bool Success, int Seconds, int OwnKilled, int OwnWounded, int EnemyDown, int ObjectivesDone, int Objectives)
{
    public static MissionResult From(GameSession session)
    {
        var tracker = session.Tracker;
        return new MissionResult(
            tracker?.Result == true,
            (int)session.GameTime.TotalSeconds,
            session.OwnUnits.Count(u => u.Wound == WoundLevel.Dead),
            session.OwnUnits.Count(u => u.Wound is > WoundLevel.None and < WoundLevel.Dead),
            // Seen to fall: in sight now, or last seen (walking on does not bring them back to life).
            session.Sim.Units.Count(u => u.Side != session.PlayerSide && u.IsOutOfAction && session.Knowledge.LevelOf(u.Id) >= ContactLevel.LastKnown),
            tracker?.DoneCount ?? 0,
            tracker?.Spec.Objectives.Count ?? 0);
    }

    /// <summary>A better success: fewer men lost (killed first, then wounded), then quicker.</summary>
    public bool BetterThan(MissionResult other) =>
        (OwnKilled, OwnWounded, Seconds).CompareTo((other.OwnKilled, other.OwnWounded, other.Seconds)) < 0;

    [JsonIgnore]
    public string TimeText => $"{Seconds / 60}:{Seconds % 60:00}";
}

public sealed class MissionRecord
{
    public int Attempts { get; set; }
    public bool Completed { get; set; }
    public MissionResult? Best { get; set; }
    public MissionResult? Last { get; set; }
}

/// <summary>What the player has done in each mission; kept between games as JSON.</summary>
public sealed class MissionProgress
{
    public Dictionary<string, MissionRecord> Missions { get; set; } = [];

    public MissionRecord? Of(string missionId) => Missions.GetValueOrDefault(missionId);

    public void Record(string missionId, MissionResult result)
    {
        if (!Missions.TryGetValue(missionId, out var record))
            Missions[missionId] = record = new MissionRecord();
        record.Attempts++;
        record.Last = result;
        if (!result.Success)
            return;
        record.Completed = true;
        if (record.Best is null || result.BetterThan(record.Best))
            record.Best = result;
    }

    public string StatusText(string missionId, string language)
    {
        bool fi = language == "fi";
        if (Of(missionId) is not { } record)
            return fi ? "Uusi tehtävä" : "New mission";
        if (record is { Completed: true, Best: { } best })
            return fi ? $"✓ Suoritettu — paras: {LossesText(best.OwnKilled, best.OwnWounded, language)}, {best.TimeText}"
                      : $"✓ Accomplished — best: {LossesText(best.OwnKilled, best.OwnWounded, language)}, {best.TimeText}";
        return fi ? $"{record.Attempts} {(record.Attempts == 1 ? "yritys" : "yritystä")} — ei vielä suoritettu"
                  : $"{record.Attempts} attempt{(record.Attempts == 1 ? "" : "s")} — not yet accomplished";
    }

    /// <summary>"1 kaatunut, 2 haavoittunutta" / "1 killed, 2 wounded".</summary>
    public static string LossesText(int killed, int wounded, string language) => language == "fi"
        ? $"{killed} kaatunut{(killed == 1 ? "" : "ta")}, {wounded} haavoittunut{(wounded == 1 ? "" : "ta")}"
        : $"{killed} killed, {wounded} wounded";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>The saved progress; a missing or broken file is a fresh start.</summary>
    public static MissionProgress FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new MissionProgress();
        try
        {
            var progress = JsonSerializer.Deserialize<MissionProgress>(json, Options) ?? new MissionProgress();
            // A hand-edited or half-written file may hold nulls: drop them.
            progress.Missions = (progress.Missions ?? []).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
            return progress;
        }
        catch (JsonException)
        {
            return new MissionProgress();
        }
    }
}

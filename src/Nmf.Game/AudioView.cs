using System.Collections.Generic;
using System.IO;
using Godot;
using Nmf.Client;
using Nmf.Client.Audio;
using Nmf.Content;
using Nmf.Content.Audio;
using Nmf.Sim.Events;

namespace Nmf.Game;

/// <summary>
/// Plays what <see cref="AudioDirector"/> decides (spec 2026-10-09-sound-design): a pool of positional players on the Sfx
/// and Voice buses, one player for interface sounds. The director sets the volume; Godot only pans by screen position.
/// </summary>
public partial class AudioView : Node
{
    private static readonly string[] Buses = ["Sfx", "Voice", "Ambience", "Music", "Ui"];

    private readonly VoicePool _pool = new();
    private readonly Dictionary<string, AudioStream?> _streams = [];
    private readonly HashSet<string> _warned = [];
    private AudioDirector _director = null!;
    private string _audioRoot = "";
    private AudioStreamPlayer2D[] _players = [];
    private AudioStreamPlayer _ui = null!;
    private bool _paused;

    /// <summary>The sound player, or null when the catalogue cannot be read (the game then runs silent).</summary>
    public static AudioView? Create(string contentRoot)
    {
        string root = Path.Combine(contentRoot, "core", "audio");
        AudioCatalog catalog;
        try
        {
            catalog = AudioCatalogLoader.Load(Path.Combine(root, "sounds.yaml"));
        }
        catch (ContentLoadException ex)
        {
            GD.PrintErr($"[NMF] audio off: {ex.Message}");
            return null;
        }
        int withFiles = 0;
        foreach (var cue in catalog.Cues.Values)
            if (cue.Files.Count > 0)
                withFiles++;
        GD.Print($"[NMF] audio: {withFiles} cues with files, {AudioCatalogLoader.MissingFiles(catalog, root).Count} files missing");
        return new AudioView { _director = new AudioDirector(catalog), _audioRoot = root };
    }

    public override void _Ready()
    {
        EnsureBuses();
        _players = new AudioStreamPlayer2D[_pool.Slots];
        for (int i = 0; i < _players.Length; i++)
        {
            // No distance fade of Godot's own: the director has set the volume already.
            _players[i] = new AudioStreamPlayer2D { Attenuation = 0, MaxDistance = 1e7f, PanningStrength = 1 };
            AddChild(_players[i]);
        }
        _ui = new AudioStreamPlayer { Bus = "Ui" };
        AddChild(_ui);
    }

    public static void EnsureBuses()
    {
        foreach (var name in Buses)
        {
            if (AudioServer.GetBusIndex(name) >= 0)
                continue;
            AudioServer.AddBus();
            int index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
    }

    /// <summary>One frame: pause or resume with the game, then start this frame's sounds.</summary>
    public void Frame(GameSession session, IReadOnlyList<SimEvent> events, bool revealAll, AudioListener listener, double delta)
    {
        bool paused = session.Clock.Paused;
        if (paused != _paused)
        {
            _paused = paused;
            foreach (var player in _players)
                player.StreamPaused = paused;
        }
        if (!paused)
            _pool.Advance(delta);
        foreach (var cue in _director.Update(session.Sim, session.PlayerSide, events, u => session.IsShownToPlayer(u, revealAll), listener, delta))
            Play(cue);
    }

    /// <summary>What our side heard in the last frame, for sounds shown as marks.</summary>
    public IReadOnlyList<HeardSound> LastHeard => _director.Heard;

    public void PlayUi(string cueId)
    {
        if (_director.Ui(cueId) is { } cue)
            Play(cue);
    }

    private void Play(SoundCue cue)
    {
        if (Load(cue.File) is not { } stream)
            return;
        if (cue.At is not { } at)
        {
            _ui.Stream = stream;
            _ui.VolumeDb = (float)cue.VolumeDb;
            _ui.PitchScale = (float)cue.Pitch;
            _ui.Play();
            return;
        }
        int slot = _pool.Claim(cue, stream.GetLength() / cue.Pitch);
        if (slot < 0)
            return;
        var player = _players[slot];
        player.Stop();
        player.Stream = stream;
        player.Bus = cue.Bus.ToString();
        player.VolumeDb = (float)cue.VolumeDb;
        player.PitchScale = (float)cue.Pitch;
        player.GlobalPosition = Coords.ToPixels(at);
        player.StreamPaused = false;
        player.Play();
    }

    private AudioStream? Load(string file)
    {
        if (_streams.TryGetValue(file, out var cached))
            return cached;
        string path = Path.Combine(_audioRoot, file);
        AudioStream? stream = File.Exists(path) ? AudioStreamOggVorbis.LoadFromFile(path) : null;
        if (stream is null && _warned.Add(file))
            GD.PushWarning($"[NMF] audio: cannot play {path}");
        _streams[file] = stream;
        return stream;
    }
}

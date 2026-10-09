using Nmf.Content.Audio;
using Nmf.Sim.Core;

namespace Nmf.Client.Audio;

/// <summary>One sound to play now: which file, where in the world (null for interface sounds), how loud and how high.</summary>
public sealed record SoundCue(string CueId, string File, AudioBus Bus, Vec2? At, double VolumeDb, double Pitch, int Priority, int MaxVoices);

/// <summary>Who hears: the camera centre, half the view's diagonal (world cm), the zoom and the game speed.</summary>
public readonly record struct AudioListener(Vec2 CenterCm, long HalfDiagonalCm, double Zoom, double Speed);

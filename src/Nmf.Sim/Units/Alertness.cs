namespace Nmf.Sim.Units;

/// <summary>How awake a man is to the enemy (spec 2026-10-10-alertness-design §2); it only rises, but suspicion fades.</summary>
public enum Alertness : byte { Calm, Suspicious, Alerted, Combat }

/// <summary>How the alarm was given.</summary>
public enum AlarmKind : byte { Shout, Flare }

/// <summary>A sound the alertness pass listens for (spec §3): a burst, a scuffle.</summary>
public enum NoiseKind : byte { Explosion, Melee }

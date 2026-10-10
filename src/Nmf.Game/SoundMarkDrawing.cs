using Godot;
using Nmf.Client.Audio;
using Nmf.Client.Effects;

namespace Nmf.Game;

/// <summary>
/// How a heard sound looks when sounds are shown as marks (spec 2026-10-10-settings-hints-design §2): sound waves for a
/// shot, a star for a blast, a slash for a bullet passing close, a cross for a man falling, a small star for a scuffle.
/// </summary>
public static class SoundMarkDrawing
{
    private static readonly Color ShotColour = new(1f, 0.92f, 0.55f);
    private static readonly Color CrackColour = new(1f, 0.4f, 0.3f);
    private static readonly Color BlastColour = new(1f, 0.55f, 0.15f);
    private static readonly Color FallColour = new(0.85f, 0.85f, 0.8f);

    /// <param name="r">Mark radius in the canvas item's units; <paramref name="w"/> the line width.</param>
    public static void Draw(CanvasItem c, SoundMark mark, Vector2 at, float r, float w)
    {
        float t = (float)(mark.Age / mark.Life);
        float fade = 1f - t * t;
        var shadow = new Color(0, 0, 0, 0.5f * fade);
        switch (mark.Kind)
        {
            case HeardKind.Shot:
                for (int i = 0; i < 2; i++)
                {
                    float wave = r * (0.45f + 0.4f * i + 0.25f * t);
                    foreach (float side in new[] { 0f, Mathf.Pi })
                    {
                        c.DrawArc(at, wave, side - 0.75f, side + 0.75f, 12, shadow, w * 2.2f, true);
                        c.DrawArc(at, wave, side - 0.75f, side + 0.75f, 12, ShotColour with { A = fade * (1f - 0.3f * i) }, w, true);
                    }
                }
                c.DrawCircle(at, w * 1.3f, ShotColour with { A = fade });
                break;
            case HeardKind.Crack:
                var s = new Vector2(r, -r * 0.35f);
                c.DrawLine(at - s, at + s, shadow, w * 2.4f, true);
                c.DrawLine(at - s, at + s, CrackColour with { A = fade }, w * 1.2f, true);
                break;
            case HeardKind.Explosion:
                for (int i = 0; i < 8; i++)
                {
                    var dir = Vector2.FromAngle(i * Mathf.Tau / 8 + 0.2f);
                    c.DrawLine(at + dir * r * 0.3f, at + dir * r * (0.9f + 0.4f * t), shadow, w * 2.2f, true);
                    c.DrawLine(at + dir * r * 0.3f, at + dir * r * (0.9f + 0.4f * t), BlastColour with { A = fade }, w, true);
                }
                break;
            case HeardKind.Fall:
                var d = new Vector2(r, r) * 0.4f;
                var e = new Vector2(r, -r) * 0.4f;
                c.DrawLine(at - d, at + d, FallColour with { A = fade }, w, true);
                c.DrawLine(at - e, at + e, FallColour with { A = fade }, w, true);
                break;
            case HeardKind.Melee:
                for (int i = 0; i < 4; i++)
                {
                    var dir = Vector2.FromAngle(i * Mathf.Tau / 4 + 0.785f);
                    c.DrawLine(at - dir * r * 0.45f, at + dir * r * 0.45f, FallColour with { A = fade }, w, true);
                }
                break;
        }
    }
}

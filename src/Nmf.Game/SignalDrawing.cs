using Godot;
using Nmf.Client.Effects;
using Signal = Nmf.Client.Effects.Signal;

namespace Nmf.Game;

/// <summary>How an event signal looks: a pulsing ring for fire, a burst for explosions, a cross for our wounded, an X for a fallen enemy.</summary>
public static class SignalDrawing
{
    private static readonly Color Fire = new(1f, 0.86f, 0.2f);
    private static readonly Color Blast = new(1f, 0.52f, 0.12f);
    private static readonly Color Hurt = new(0.95f, 0.15f, 0.12f);
    private static readonly Color ShoutColour = new(1f, 0.35f, 0.15f);

    /// <param name="r">Signal radius in the canvas item's units; <paramref name="w"/> the line width in the same units.</param>
    public static void Draw(CanvasItem c, Signal signal, Vector2 at, float r, float w)
    {
        float t = (float)(signal.Age / signal.Lifetime);
        float fade = 1f - t * t;
        var shadow = new Color(0, 0, 0, 0.55f * fade);
        switch (signal.Kind)
        {
            case SignalKind.Gunfire:
                float ring = r * (0.55f + 0.6f * Mathf.PosMod((float)signal.Pulse * 2.1f, 1f));
                c.DrawArc(at, ring, 0, Mathf.Tau, 32, shadow, w * 2.2f, true);
                c.DrawArc(at, ring, 0, Mathf.Tau, 32, Fire with { A = fade }, w, true);
                c.DrawCircle(at, w * 1.2f, Fire with { A = fade });
                break;
            case SignalKind.Explosion:
                for (int i = 0; i < 8; i++)
                {
                    var dir = Vector2.FromAngle(i * Mathf.Tau / 8);
                    c.DrawLine(at + dir * r * 0.3f, at + dir * r * (0.8f + 0.4f * t), shadow, w * 2.2f, true);
                    c.DrawLine(at + dir * r * 0.3f, at + dir * r * (0.8f + 0.4f * t), Blast with { A = fade }, w, true);
                }
                c.DrawArc(at, r * (0.4f + 0.8f * t), 0, Mathf.Tau, 32, Blast with { A = fade * 0.7f }, w, true);
                break;
            case SignalKind.OwnHit:
                c.DrawCircle(at, r * 0.75f, new Color(1, 1, 1, 0.85f * fade));
                c.DrawRect(new Rect2(at - new Vector2(r * 0.14f, r * 0.5f), new Vector2(r * 0.28f, r)), Hurt with { A = fade });
                c.DrawRect(new Rect2(at - new Vector2(r * 0.5f, r * 0.14f), new Vector2(r, r * 0.28f)), Hurt with { A = fade });
                break;
            case SignalKind.Shout:
                // Sound waves spreading out from a red dot: a call heard, not a man seen.
                c.DrawCircle(at, r * 0.35f, shadow);
                c.DrawCircle(at, r * 0.25f, ShoutColour with { A = fade });
                for (int i = 0; i < 3; i++)
                {
                    float wave = r * (0.5f + 0.35f * i + 0.35f * Mathf.PosMod((float)signal.Pulse * 1.5f, 1f));
                    c.DrawArc(at, wave, -0.7f, 0.7f, 12, ShoutColour with { A = fade * (1f - i * 0.25f) }, w, true);
                    c.DrawArc(at, wave, Mathf.Pi - 0.7f, Mathf.Pi + 0.7f, 12, ShoutColour with { A = fade * (1f - i * 0.25f) }, w, true);
                }
                break;
            case SignalKind.EnemyDown:
                var d = new Vector2(r, r) * 0.55f;
                var e = new Vector2(r, -r) * 0.55f;
                c.DrawLine(at - d, at + d, shadow, w * 2.6f, true);
                c.DrawLine(at - e, at + e, shadow, w * 2.6f, true);
                c.DrawLine(at - d, at + d, Hurt with { A = fade }, w * 1.4f, true);
                c.DrawLine(at - e, at + e, Hurt with { A = fade }, w * 1.4f, true);
                break;
        }
    }
}

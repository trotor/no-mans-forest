using Godot;
using Nmf.Client;

namespace Nmf.Game;

/// <summary>
/// The dark of dusk and night over the battlefield (spec 2026-10-10-winter-design §4): a shade by the mission's light,
/// cleared round each burning flare, under the HUD.
/// </summary>
public partial class NightView : Node2D
{
    private const int MaxFlares = 4;

    private ColorRect _rect = null!;
    private ShaderMaterial _material = null!;

    public GameSession Session { get; set; } = null!;
    public Vector2 WorldSize { get; set; }

    public override void _Ready()
    {
        _material = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type canvas_item;
                    uniform float darkness = 0.0;
                    uniform vec2 flares[4];
                    uniform int flare_count = 0;
                    uniform float flare_px = 3200.0;
                    uniform vec2 world_size = vec2(16000.0);
                    void fragment() {
                        vec2 p = UV * world_size;
                        float shade = darkness;
                        for (int i = 0; i < flare_count; i++)
                            shade *= smoothstep(flare_px * 0.9, flare_px * 1.05, distance(p, flares[i]));
                        COLOR = vec4(0.02, 0.03, 0.07, shade);
                    }
                    """,
            },
        };
        _rect = new ColorRect { Size = WorldSize, Material = _material, MouseFilter = Control.MouseFilterEnum.Ignore };
        _material.SetShaderParameter("world_size", WorldSize);
        _material.SetShaderParameter("flare_px", Nmf.Sim.Simulation.FlareLightCm / 100f * Coords.PixelsPerCell);
        AddChild(_rect);
    }

    public override void _Process(double delta)
    {
        float darkness = LightOverlay.Darkness(Session.Sim.LightPct);
        Visible = darkness > 0;
        if (!Visible)
            return;
        var flares = new Vector2[MaxFlares];
        int n = 0;
        // Where it went up only to the 10 m square, as the flare itself is drawn: no unseen leader marked to the metre.
        foreach (var (at, until) in Session.Sim.FlareLights)
            if (until > Session.Sim.Tick && n < MaxFlares)
                flares[n++] = Coords.ToPixels(new Nmf.Sim.Core.Vec2(at.X / 1000 * 1000 + 500, at.Y / 1000 * 1000 + 500));
        _material.SetShaderParameter("darkness", darkness);
        _material.SetShaderParameter("flares", flares);
        _material.SetShaderParameter("flare_count", n);
    }
}

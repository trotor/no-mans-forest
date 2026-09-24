shader_type canvas_item;

// One texel per 1 m cell. R = terrain slot / 255 (0 grass, 1 forest, 2 swamp, 3 road).
uniform sampler2D terrain_map : filter_nearest;
// One texel per cell. R = ground height / 3 m.
uniform sampler2D height_map : filter_linear;
uniform sampler2D tex_grass : filter_nearest, repeat_enable;
uniform sampler2D tex_forest : filter_nearest, repeat_enable;
uniform sampler2D tex_swamp : filter_nearest, repeat_enable;
uniform sampler2D tex_road : filter_nearest, repeat_enable;
uniform vec2 map_cells = vec2(128.0, 96.0);
uniform float texture_cells = 4.0;   // one 128 px texture tile covers 4 x 4 cells
uniform float edge_noise = 0.45;     // border wobble in cells
uniform float relief = 3.0;          // hillshade strength

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float value_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

vec3 terrain_color(int slot, vec2 uv) {
    if (slot == 1) return texture(tex_forest, uv).rgb;
    if (slot == 2) return texture(tex_swamp, uv).rgb;
    if (slot == 3) return texture(tex_road, uv).rgb;
    return texture(tex_grass, uv).rgb;
}

void fragment() {
    vec2 cell = UV * map_cells;
    // Snap to the 32 px art grid so borders stay pixel-crisp at any zoom.
    vec2 px = (floor(cell * 32.0) + 0.5) / 32.0;
    vec2 wobble = vec2(value_noise(px * 1.3), value_noise(px * 1.3 + 17.0)) - 0.5;
    vec2 wobble_fine = vec2(value_noise(px * 5.0 + 3.0), value_noise(px * 5.0 + 41.0)) - 0.5;
    vec2 lookup = clamp(px + (wobble + 0.35 * wobble_fine) * edge_noise * 2.0, vec2(0.01), map_cells - 0.01);
    int slot = int(texture(terrain_map, lookup / map_cells).r * 255.0 + 0.5);
    vec3 col = terrain_color(slot, px / texture_cells);
    // Large-scale brightness and tint variation hides the 4 m texture repeat.
    float macro = value_noise(px * 0.07) * 0.6 + value_noise(px * 0.19 + 7.0) * 0.4;
    col *= mix(vec3(0.86, 0.9, 0.84), vec3(1.1, 1.06, 1.0), macro);

    // Hillshade: light from the north-west.
    vec2 texel = 1.0 / map_cells;
    vec2 huv = px / map_cells;
    float hl = texture(height_map, huv - vec2(texel.x, 0.0)).r;
    float hr = texture(height_map, huv + vec2(texel.x, 0.0)).r;
    float hu = texture(height_map, huv - vec2(0.0, texel.y)).r;
    float hd = texture(height_map, huv + vec2(0.0, texel.y)).r;
    vec3 normal = normalize(vec3((hl - hr) * relief, (hu - hd) * relief, 1.0));
    vec3 light_dir = normalize(vec3(-0.55, -0.7, 0.9));
    float shade = clamp(dot(normal, light_dir) / dot(vec3(0.0, 0.0, 1.0), light_dir), 0.7, 1.25);
    float h = texture(height_map, huv).r;
    col *= shade * (0.94 + 0.1 * h);
    COLOR = vec4(col, 1.0);
}

# No Man's Forest

A 2D top-down WW2 tactics game set in the Finnish Continuation War (1941–44):
Close Combat style real-time squad fighting, Jagged Alliance 2 style turn-based contact mode,
and Squad Leader style leadership and morale. Hobby project, open source.

Design: [`docs/superpowers/specs/2026-09-24-no-mans-forest-design.md`](docs/superpowers/specs/2026-09-24-no-mans-forest-design.md)

## Build and test

Requires the .NET 10 SDK.

```bash
dotnet build NoMansForest.slnx
dotnet test NoMansForest.slnx
```

## Tools

```bash
dotnet run --project src/Nmf.Cli -- map-info content/core/maps/sandbox.tmx
```

## Making maps with Tiled

Maps are made with [Tiled](https://www.mapeditor.org). One tile is one 1 m × 1 m cell.

- Orthogonal map, square tiles, **Infinite** off, **Tile Layer Format: CSV**.
- Tile layers (exact names):
  - `terrain` (required, every cell filled): tile property `terrain` (name), optional `concealment_per_m` (0–1), `cover` (0–1), `obstacle_height_cm`, `move_cost` (1–3.54, time multiplier), `impassable` (true/false).
  - `height` (optional): tile property `height_cm`.
  - `obstacles` (optional): `obstacle_height_cm`, `concealment_per_m`, `cover`, `move_cost`, `impassable`, combined with the terrain using the larger value.
- Object layers: rectangles become zones, points become points, polylines become paths. Every object needs a unique name; the object *type/class* is kept as its type.
- Use the tilesets in `content/core/tilesets/`. `python3 tools/make_placeholder_tiles.py` regenerates their placeholder images.

## License

Code: MIT. Art and sound: CC BY-SA 4.0.

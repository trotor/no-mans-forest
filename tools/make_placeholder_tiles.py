"""Writes flat-colour placeholder tileset images for content/core/tilesets (stdlib only).

Run from the repository root: python3 tools/make_placeholder_tiles.py
"""
import pathlib
import struct
import zlib

TILE = 16
OUT = pathlib.Path("content/core/tilesets")

TILESETS = {
    "terrain.png": [(96, 140, 60), (34, 85, 40), (90, 110, 90), (150, 120, 80)],  # grass, forest, swamp, road
    "heights.png": [(40, 40, 40), (90, 90, 90), (150, 150, 150), (210, 210, 210)],  # 0, 1, 2, 3 m
    "obstacles.png": [(128, 128, 128), (60, 120, 50)],  # rock, bush
}


def chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def write_strip(path: pathlib.Path, colours: list[tuple[int, int, int]]) -> None:
    width = TILE * len(colours)
    rows = []
    for y in range(TILE):
        row = bytearray([0])  # PNG filter type: none
        for colour in colours:
            for x in range(TILE):
                edge = x == 0 or y == 0
                row.extend(max(0, c - 30) if edge else c for c in colour)
        rows.append(bytes(row))
    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, TILE, 8, 2, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(b"".join(rows)))
        + chunk(b"IEND", b"")
    )
    path.write_bytes(png)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, colours in TILESETS.items():
        write_strip(OUT / name, colours)
        print(f"wrote {OUT / name}")

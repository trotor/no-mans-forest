"""Map areas taken from real terrain. Each is a square around a centre point."""

AREAS = {
    # South of Karhumäki (Medvezhyegorsk) on the Lake Onega shore: the rear of the Maaselkä front in 1942.
    "karhumaki": {
        "lat": 62.880,
        "lon": 34.440,
        "size_m": 1000,
        "lake_relation": 1308279,  # Lake Onega; its full outline is far too large, only the shore ways in the box are fetched
        "lake_seed": (999, 0),     # a cell known to be lake water (north-east corner)
        "title": "Karhumäki, Lake Onega shore",
    },
}

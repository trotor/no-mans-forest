"""python3 -m tools.audio <folder> --service "<service and plan>" --licence "CC BY-SA 4.0" """
import argparse
import datetime
import pathlib
import sys

from tools.audio.import_sounds import import_folder

AUDIO_ROOT = pathlib.Path(__file__).resolve().parents[2] / "content" / "core" / "audio"


def main():
    parser = argparse.ArgumentParser(prog="python3 -m tools.audio", description="Import sound files into the game.")
    parser.add_argument("folder", type=pathlib.Path, help="folder of <cue>.<version>.<ext> files")
    parser.add_argument("--service", required=True, help="where the sounds came from, e.g. 'ElevenLabs Creator'")
    parser.add_argument("--licence", default="CC BY-SA 4.0")
    parser.add_argument("--audio-root", type=pathlib.Path, default=AUDIO_ROOT, help=argparse.SUPPRESS)
    args = parser.parse_args()
    try:
        written = import_folder(args.folder, args.audio_root, args.service, args.licence, datetime.date.today().isoformat())
    except ValueError as error:
        sys.exit(f"error: {error}")
    for path in written:
        print(f"imported {path.relative_to(args.audio_root)}")
    print(f"{len(written)} file(s) imported into {args.audio_root}")


if __name__ == "__main__":
    main()

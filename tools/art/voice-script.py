#!/usr/bin/env python3
"""Prints a voice's script, one line per line, straight from game/data/presentation.jsonc: "callouts" (every line of
hud.callouts, for the bots' voices in audio.cast) or "referee" (hud.referee, for audio.refereeVoice). The same text goes
to the text-to-speech generator and to tools/art/import.sh voice, so the take is cut into exactly the lines the game shows:
    tools/art/import.sh voice callout_a1 <job> <generator> <url> "$(tools/art/voice-script.py callouts)"
"""
import json
import re
import sys
from pathlib import Path

ORDER = {
    "callouts": ["spotted", "lost", "underFire", "refill", "hit", "flanking", "pushing", "moving", "manDown", "caseTaken",
                 "caseDown", "caseAlarm", "roomAlarm", "flagTaken", "flagDown", "flagAlarm"],
    "referee": ["start", "oneMinute", "thirtySeconds", "timeUp", "youreOut", "won", "lost", "lastStanding", "caseOut",
                "roomHeld", "roomTaken", "roomContested", "buzzer", "pointWon", "pointLost", "noPoint", "flagTaken", "flagDown",
                "flagCaptured"],
}


def load(path):
    text = path.read_text(encoding="utf-8")
    # Drop // comments outside strings, then trailing commas.
    out, in_string, i = [], False, 0
    while i < len(text):
        c = text[i]
        if in_string:
            out.append(c)
            if c == "\\":
                out.append(text[i + 1])
                i += 1
            elif c == '"':
                in_string = False
        elif c == '"':
            in_string = True
            out.append(c)
        elif text.startswith("//", i):
            while i < len(text) and text[i] != "\n":
                i += 1
            continue
        else:
            out.append(c)
        i += 1
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", "".join(out)))


def main():
    if len(sys.argv) != 2 or sys.argv[1] not in ORDER:
        sys.exit("usage: voice-script.py callouts|referee")
    which = sys.argv[1]
    hud = load(Path(__file__).resolve().parents[2] / "game" / "data" / "presentation.jsonc")["hud"][which]
    missing = set(hud) - set(ORDER[which])
    if missing:
        sys.exit(f"voice-script.py doesn't know {', '.join(sorted(missing))}: add them to ORDER")
    print("\n".join(line for key in ORDER[which] for line in hud[key]))


if __name__ == "__main__":
    main()

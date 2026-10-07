#!/usr/bin/env python3
"""Listens to every line of the given voices (game/art/voices, as tools/art/import.sh voice cut them) with a speech
recogniser and lists each whose words aren't its script line's, so a take that says a line twice, runs another line's
words into one or swallows a word is caught before it ships. Fix those by importing the take again with
--keep-first="LINE" or --keep-last="LINE" (several lines: "LINE|LINE"), which keep only the first or last part of a
line, cut at the longest pause inside it.
    tools/art/voice-check.py callout_a1 callout_b1 referee
Needs ffmpeg and faster-whisper (pip install faster-whisper, in a virtual environment); its models (base.en for a
first listen, small.en for a second to lines that didn't match, about 650 MB) are downloaded the first time. Lines are
compared letter by letter, so spacing and homophones ("your" for "you're") pass and a doubled, stray or missing word
doesn't. A line listed is one to listen to: the recogniser still mishears a word the voice said clearly now and then.
"""
import difflib
import re
import subprocess
import sys
from pathlib import Path

import numpy as np
from faster_whisper import WhisperModel

ROOT = Path(__file__).resolve().parents[2]
NUMBERS = {"0": "zero", "1": "one", "2": "two", "3": "three", "4": "four", "5": "five", "6": "six", "7": "seven",
           "8": "eight", "9": "nine", "10": "ten", "20": "twenty", "30": "thirty", "40": "forty", "50": "fifty",
           "60": "sixty", "90": "ninety"}
APOSTROPHES = str.maketrans("", "", "'’")


def slug(line):
    """The line's file name, as game/audio/VoiceBank.cs Slug makes it."""
    return re.sub(r"[^a-z0-9]+", "-", line.lower().translate(APOSTROPHES)).strip("-")


def letters(text):
    """The text's letters, numbers spelled out, without spaces or punctuation."""
    found = re.sub(r"[^a-z0-9 ]+", " ", text.lower().translate(APOSTROPHES)).split()
    return "".join(NUMBERS.get(w, w) for w in found)


def matches(line, text):
    """Whether what was heard is the line, letter for letter but for the odd one (95 %)."""
    return difflib.SequenceMatcher(None, letters(line), letters(text)).ratio() >= 0.95


def load(path):
    """The file as 16 kHz mono samples, decoded by ffmpeg."""
    raw = subprocess.run(["ffmpeg", "-loglevel", "error", "-i", str(path), "-ac", "1", "-ar", "16000", "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32)


def heard(model, path):
    segments, _ = model.transcribe(load(path), language="en", beam_size=5, vad_filter=False, condition_on_previous_text=False)
    return " ".join(s.text.strip() for s in segments)


def main(voices):
    if not voices:
        sys.exit(__doc__)
    quick, careful = WhisperModel("base.en", device="cpu", compute_type="int8"), None
    problems = 0
    for voice in voices:
        kind = "referee" if voice == "referee" else "callouts"
        script = subprocess.run([sys.executable, str(ROOT / "tools/art/voice-script.py"), kind],
                                capture_output=True, text=True, check=True).stdout.splitlines()
        for line in script:
            path = ROOT / "game/art/voices" / f"{voice}_{slug(line)}.ogg"
            if not path.exists():
                print(f"MISSING {voice}: {line!r} ({path.name})")
                problems += 1
                continue
            text = heard(quick, path)
            if not matches(line, text):
                careful = careful or WhisperModel("small.en", device="cpu", compute_type="int8")
                text = heard(careful, path)
            if not matches(line, text):
                problems += 1
                print(f"LISTEN {voice}: {line!r} heard as {text!r}")
        print(f"{voice}: {len(script)} lines checked")
    print(f"{problems} line(s) to listen to")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

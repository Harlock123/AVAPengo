#!/usr/bin/env python3
"""Synthesises AVAPengo's sound effects and tunes into ../src/AVAPengo/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPengo", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The ice: a push and its slide, a block coming to rest, a block breaking, the crush.
    write("Push.wav", mix(tone(0.3, 3000, 600, "noise", release=0.9, gain=0.6), tone(0.25, 700, 250, "square", release=1.0, gain=0.4)), 0.3)
    write("Thud.wav", mix(tone(0.14, 140, 55, "sine", release=1.2), tone(0.06, 800, 300, "noise", release=1.0, gain=0.3)), 0.5)
    write("Shatter.wav", cat(tone(0.06, 6000, 3000, "noise", release=0.8), *[tone(0.04, f, kind="tri", release=0.5) for f in [2600, 2100, 2900, 1800, 2400]]), 0.32)
    write("Crush.wav", mix(tone(0.4, 500, 50, "square", release=0.8), tone(0.3, 1800, 200, "noise", release=0.9, gain=0.6),
                           cat(silence(0.12), tone(0.2, 900, 1500, "tri", release=0.6, gain=0.5))), 0.45)
    # Kicking the wall: a long, buzzing wobble.
    write("Shake.wav", mix(tone(0.5, 160, 120, "saw", release=0.7, vibrato=0.25, vib_rate=28), tone(0.3, 400, 200, "noise", release=0.9, gain=0.3)), 0.4)
    write("Stamp.wav", cat(tone(0.06, 900, 300, "square", release=0.6), tone(0.1, 1300, 1700, "pulse", release=0.6)), 0.34)
    # Eggs: broken in the ice, or hatching out of it.
    write("EggBreak.wav", cat(tone(0.05, 5000, 2000, "noise", release=0.7), *[tone(0.06, hz(n), kind="tri", release=0.4) for n in ["G6", "D6", "B5", "G5"]]), 0.38)
    write("Hatch.wav", cat(tone(0.05, 4000, 1500, "noise", release=0.7), tone(0.25, 400, 1600, "pulse", release=0.5, vibrato=0.05, vib_rate=25)), 0.3)
    write("Chew.wav", cat(tone(0.05, 900, 400, "noise", release=0.9), silence(0.03), tone(0.05, 800, 350, "noise", release=0.9)), 0.25)
    write("StarBonus.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7", "E6", "G6", "C7", "E7"]],
                               mix(tone(0.6, hz("C7"), kind="tri", release=0.6, vibrato=0.01), tone(0.6, hz("G6"), kind="tri", release=0.6, gain=0.6))), 0.42)
    write("Flee.wav", tone(0.6, 500, 1400, "tri", release=0.4, vibrato=0.08, vib_rate=16), 0.3)
    write("Escape.wav", cat(tone(0.15, 800, 400, "tri", release=0.5), tone(0.5, 600, 3200, "tri", release=0.8, vibrato=0.05, vib_rate=20)), 0.3)
    write("Hurry.wav", cat(*[tone(0.11, f, kind="square", release=0.2) for f in [1320, 990, 1320, 990, 1320, 990]]), 0.3)
    write("Death.wav", cat(mix(tone(0.3, 1200, 300, "square", release=0.6), tone(0.3, 3000, 400, "noise", release=0.8, gain=0.4)),
                           *[tone(0.16, hz(n), kind="tri", release=0.4) for n in ["G5", "F#5", "F5", "E5", "D#5", "D5"]], tone(0.5, hz("C5"), kind="tri", release=0.8)), 0.45)
    write("RoundStart.wav", tune(160, [("G5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("E6", 0.5), ("D6", 0.5), ("C6", 1.5)],
                                 [("C3", 1), ("G3", 1), ("C3", 1), ("G2", 0.5), ("C3", 1)]), 0.42)
    write("Clear.wav", tune(176, [("C6", 0.5), ("G5", 0.5), ("E5", 0.5), ("G5", 0.5), ("A5", 0.5), ("B5", 0.5), ("C6", 0.5), ("D6", 0.5), ("E6", 0.5), ("D6", 0.5), ("E6", 0.5), ("G6", 0.5), ("C7", 2)],
                            [("C3", 1), ("E3", 1), ("F3", 1), ("G3", 1), ("C3", 1), ("G2", 1), ("C3", 2)]), 0.44)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7", "G6", "C7"]]), 0.4)

    # The round's tune: a bouncy new one in C, staccato over an oom-pah bass. Loops.
    lead = [("E5", .5), ("G5", .5), ("C6", .5), ("G5", .5), ("E5", .5), ("G5", .5), ("C6", 1),
            ("D6", .5), ("C6", .5), ("B5", .5), ("A5", .5), ("G5", 2),
            ("F5", .5), ("A5", .5), ("D6", .5), ("A5", .5), ("F5", .5), ("A5", .5), ("D6", 1),
            ("E6", .5), ("D6", .5), ("C6", .5), ("B5", .5), ("C6", 2),
            ("A5", .5), ("C6", .5), ("E6", .5), ("C6", .5), ("G5", .5), ("B5", .5), ("D6", 1),
            ("C6", .5), ("B5", .5), ("A5", .5), ("G5", .5), ("F5", 1), ("E5", 1),
            ("D5", .5), ("E5", .5), ("F5", .5), ("G5", .5), ("A5", .5), ("B5", .5), ("C6", .5), ("D6", .5),
            ("E6", 1), ("C6", .5), ("G5", .5), ("C6", 1), ("-", 1)]
    bass = [("C3", 1), ("G3", 1), ("C3", 1), ("G3", 1), ("G2", 1), ("D3", 1), ("G2", 1), ("D3", 1),
            ("D3", 1), ("A3", 1), ("D3", 1), ("A3", 1), ("G2", 1), ("D3", 1), ("C3", 1), ("G3", 1),
            ("A2", 1), ("E3", 1), ("G2", 1), ("D3", 1), ("F2", 1), ("C3", 1), ("C3", 1), ("G3", 1),
            ("G2", 1), ("D3", 1), ("G2", 1), ("B2", 1), ("C3", 1), ("G3", 1), ("C3", 1), ("G2", 1)]
    write("Music.wav", tune(138, lead, bass, kind="pulse"), 0.4)

    # The title: a gentle waltz in F, like a music box on the ice (new).
    lead = [("C5", 1), ("F5", 1), ("A5", 1), ("G5", 2), ("F5", 1), ("E5", 1), ("G5", 1), ("C6", 1), ("A5", 3),
            ("Bb5", 1), ("A5", 1), ("G5", 1), ("F5", 2), ("D5", 1), ("C5", 1), ("E5", 1), ("G5", 1), ("F5", 3)]
    bass = [("F3", 1), ("A3", 1), ("C4", 1), ("C3", 1), ("G3", 1), ("Bb3", 1), ("C3", 1), ("E3", 1), ("G3", 1), ("F3", 1), ("A3", 1), ("C4", 1),
            ("Bb2", 1), ("D3", 1), ("F3", 1), ("F3", 1), ("A3", 1), ("C4", 1), ("C3", 1), ("E3", 1), ("Bb3", 1), ("F3", 1), ("C3", 1), ("F2", 1)]
    write("TitleTune.wav", tune(132, lead, bass, kind="tri"), 0.45)
    write("StartTune.wav", tune(160, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("B5", 0.5), ("C6", 0.5), ("E6", 2)], [("C3", 1), ("G2", 1), ("C3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(80, [("G5", 1), ("E5", 1), ("C5", 1), ("D5", 1), ("B4", 1), ("C5", 3)], [("C3", 2), ("A2", 1), ("G2", 2), ("C3", 3)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()

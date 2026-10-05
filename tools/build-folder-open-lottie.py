"""Morph the real E8B7/E838 outlines without polygonal resampling.

Run: uv run --with fonttools python tools/build-folder-open-lottie.py
Optional argument: official Skottie player root (exports a new preview scene).
"""
from __future__ import annotations

import json
import sys
from fractions import Fraction
from pathlib import Path

from fontTools.pens.qu2cuPen import Qu2CuPen
from fontTools.pens.recordingPen import RecordingPen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src/Tonarink.App/Assets/Lottie/FolderOpenIcon.json"
FONT = Path("C:/Windows/Fonts/SegoeIcons.ttf")


def mix(a, b, t):
    return tuple(a[j] + (b[j] - a[j]) * t for j in (0, 1))


def line(a, b):
    return (a, mix(a, b, 1 / 3), mix(a, b, 2 / 3), b)


def split(curve, t):
    a, b, c, d = curve
    ab, bc, cd = mix(a, b, t), mix(b, c, t), mix(c, d, t)
    abc, bcd = mix(ab, bc, t), mix(bc, cd, t)
    middle = mix(abc, bcd, t)
    return (a, ab, abc, middle), (middle, bcd, cd, d)


def extract(font, codepoint):
    """Keep original drawing operations as semantic sections of each contour."""
    raw = RecordingPen()
    font.getGlyphSet()[font.getBestCmap()[codepoint]].draw(raw)
    result = []
    for operation, args in raw.value:
        if operation == "moveTo":
            sections = []
            result.append(sections)
            current = start = args[0]
        elif operation == "lineTo":
            sections.append([line(current, args[0])])
            current = args[0]
        elif operation == "qCurveTo":
            cubic = RecordingPen()
            converter = Qu2CuPen(cubic, 0.1, all_cubic=True)
            converter.moveTo(current)
            converter.qCurveTo(*args)
            converter.endPath()
            section = []
            for command, points in cubic.value[1:]:
                if command == "endPath":
                    continue
                if command != "curveTo":
                    raise ValueError(command)
                section.append((current, *points))
                current = points[-1]
            sections.append(section)
        elif operation == "closePath":
            sections.append([line(current, start)])
        else:
            raise ValueError(operation)
    return result


def join(sections, *indices):
    return [curve for i in indices for curve in sections[i]]


def align(a, b):
    """Split at the union of segment boundaries; never flatten either curve.

    This makes unequal curve counts compatible while preserving both exact
    outlines. Alignment is local to corresponding edges/corners, not arbitrary
    distances around an entire contour (which makes the tab slide and twist).
    """
    boundaries = sorted({Fraction(i, len(curves))
                         for curves in (a, b) for i in range(len(curves) + 1)})

    def partition(curves):
        pieces = []
        for lo, hi in zip(boundaries, boundaries[1:]):
            index = min(int(lo * len(curves)), len(curves) - 1)
            t0 = float(lo * len(curves) - index)
            t1 = float(hi * len(curves) - index)
            piece = curves[index]
            if t1 < 1:
                piece = split(piece, t1)[0]
            if t0 > 0:
                piece = split(piece, t0 / t1)[1]
            pieces.append(piece)
        return pieces

    return partition(a), partition(b)


def point(p):
    # Retain the existing shared 40 px glyph scale and placement.
    return [round(4 + p[0] * 40 / 2048, 6),
            round(42 - p[1] * 40 / 2048, 6)]


def path(curves):
    vertices, incoming, outgoing = [], [], []
    for j, curve in enumerate(curves):
        a, b, _, _ = map(point, curve)
        previous_control = point(curves[j - 1][2])
        vertices.append(a)
        incoming.append([round(previous_control[k] - a[k], 6) for k in (0, 1)])
        outgoing.append([round(b[k] - a[k], 6) for k in (0, 1)])
    return dict(v=vertices, i=incoming, o=outgoing, c=True)


def paired_path(pairs):
    closed, opened = [], []
    for a, b in pairs:
        first, second = align(a, b)
        closed.extend(first)
        opened.extend(second)
    return path(closed), path(opened)


def animated(a, b):
    return dict(a=1, k=[
        dict(t=0, s=[a], e=[b], o=dict(x=[0.2], y=[0.8]),
             i=dict(x=[0.3], y=[1])),
        dict(t=14, s=[b], h=1),
        dict(t=20, s=[b], e=[a], o=dict(x=[0.35], y=[0]),
             i=dict(x=[0.75], y=[1])),
        dict(t=34, s=[a], h=1),
    ])


def generate():
    font = TTFont(FONT)
    closed = extract(font, 0xE8B7)
    opened = extract(font, 0xE838)
    a, b = closed[0], opened[0]
    outer = paired_path([
        *[(a[j], b[j]) for j in range(7)],
        (a[7], join(b, 7, 8, 9)),
        (a[8], b[10]), (a[9], b[11]),
    ])

    a, b = closed[1], opened[1]
    # Start at the same top-left tab edge. The rear panel's new right-hand
    # corner emerges at the tab junction, rather than borrowing vertices from
    # the unchanged left corner. Its lower edge opens into the front lip.
    junction = [line((932, 1616), (932, 1616))]
    rear = paired_path([
        (a[2], b[4]), (a[3], b[5]),
        (a[4], join(b, 6, 7, 8)),
        ([line((128, 1536), (128, 1536))], join(b, 9, 10)),
        (a[5], b[11]), (a[0], b[12]),
        (junction, join(b, 0, 1)),
        (a[1], join(b, 2, 3)),
    ])

    a, b = closed[2], opened[2]
    # The old diagonal seam straightens into the new horizontal lip. Do not
    # map that seam to the new left corner: that leaves a moving notch there.
    top = b[4][0]
    first_top, rest_top = split(top, (1728 - 1043) / (1728 - 128))
    middle_top, last_top = split(rest_top, (1043 - 754) / (1043 - 128))
    front = paired_path([
        (a[8], b[8]), (a[0], b[0]),
        (a[1], join(b, 1, 2)), (a[2], b[3]),
        (a[3], [first_top]), (a[4], [middle_top]),
        (a[5], [last_top]),
        ([line((128, 1408), (128, 1408))], b[5]),
        (a[6], b[6]), (a[7], b[7]),
    ])

    # Preserve the asset's player sizing, color binding, and animation timing.
    document = json.loads(OUTPUT.read_text(encoding="utf-8"))
    shapes = document["layers"][0]["shapes"][0]["it"]
    geometry = [shape for shape in shapes if shape["ty"] == "sh"]
    for shape, name, (first, second) in zip(
            geometry, ("Folder outer outline", "Folder rear opening", "Folder front opening"),
            (outer, rear, front)):
        shape.update(nm=name, ks=animated(first, second))
    return document


if __name__ == "__main__":
    document = generate()
    encoded = json.dumps(document, separators=(",", ":")) + "\n"
    OUTPUT.write_text(encoded, encoding="utf-8")
    print(OUTPUT)
    if len(sys.argv) > 1:
        scene = Path(sys.argv[1]) / "public/projects/tonarink-folder-outline-fix/scene-1"
        scene.mkdir(parents=True, exist_ok=True)
        destination = scene / "lottie.json"
        if destination.exists():
            # Read the current scene before replacing a previously exported copy.
            json.loads(destination.read_text(encoding="utf-8"))
        destination.write_text(encoded, encoding="utf-8")
        print(destination)

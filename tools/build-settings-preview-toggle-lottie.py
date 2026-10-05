"""Extract E890/ED1A and draw/retract the official PreviewOff slash.

Run with: uv run --with fonttools python tools/build-settings-preview-toggle-lottie.py
An optional player root exports a copy to public/projects/tonarink-settings-preview/scene-1.
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

from fontTools.pens.qu2cuPen import Qu2CuPen
from fontTools.pens.recordingPen import RecordingPen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
FONT = Path("C:/Windows/Fonts/SegoeIcons.ttf")
OUTPUT = ROOT / "src/Tonarink.App/Assets/Lottie/SettingsPreviewToggleIcon.json"
SIZE = 48
SCALE = 46 / 2048
END = 32
REVERSE_START = 36
REVERSE_END = 68
OUT = 76
TRAVEL = (1.0, 0.49, 0.0, 0.55)


def point(p):
    # Both glyphs share the font coordinate system. Never fit each separately:
    # ED1A's diagonal has taller bounds, but its eye must not shrink or move.
    return [round(1 + p[0] * SCALE, 5), round(47 - p[1] * SCALE, 5)]


def difference(a, b):
    return [round(b[0] - a[0], 5), round(b[1] - a[1], 5)]


def contours(recording):
    cubic = RecordingPen()
    recording.replay(Qu2CuPen(cubic, 0.1, all_cubic=True))
    result = []
    vertices, incoming, outgoing = [], [], []

    def finish():
        nonlocal vertices, incoming, outgoing
        if vertices:
            result.append(dict(v=vertices, i=incoming, o=outgoing, c=True))
        vertices, incoming, outgoing = [], [], []

    for operation, args in cubic.value:
        if operation == "moveTo":
            finish()
            vertices.append(point(args[0]))
            incoming.append([0, 0])
            outgoing.append([0, 0])
        elif operation == "lineTo":
            vertices.append(point(args[0]))
            incoming.append([0, 0])
            outgoing.append([0, 0])
        elif operation == "curveTo":
            first, second, destination = map(point, args)
            outgoing[-1] = difference(vertices[-1], first)
            vertices.append(destination)
            incoming.append(difference(destination, second))
            outgoing.append([0, 0])
        elif operation in ("closePath", "endPath"):
            finish()
        else:
            raise ValueError(operation)
    finish()
    return result


def static(value):
    return dict(a=0, k=value)


def frame(time, value, end=None):
    k = dict(t=time, s=[value])
    if end is not None:
        k.update(e=[end], o=dict(x=[TRAVEL[0]], y=[TRAVEL[1]]),
                 i=dict(x=[TRAVEL[2]], y=[TRAVEL[3]]))
    return k


def state_path(open_path, closed_path):
    return dict(a=1, k=[frame(0, open_path, closed_path), frame(END, closed_path),
                         frame(REVERSE_START, closed_path, open_path),
                         frame(REVERSE_END, open_path)])


def visibility(beats):
    return dict(a=1, k=[dict(t=t, s=[value], h=1) for t, value in beats])


def layer(index, name, shapes, opacity=None, mask=None):
    result = dict(ddd=0, ind=index, ty=4, nm=name, sr=1, ao=0,
                  ks=dict(o=opacity or static(100), r=static(0),
                          p=static([0, 0, 0]), a=static([0, 0, 0]),
                          s=static([100, 100, 100])),
                  shapes=[*[
                      dict(ty="sh", nm=f"{name} contour {i + 1}",
                           ks=shape if "a" in shape else static(shape))
                      for i, shape in enumerate(shapes)],
                      dict(ty="fl", nm="Foreground {Color:var(Foreground)}",
                           c=static([0, 0, 0, 1]), o=static(100), r=1)],
                  ip=0, op=OUT, st=0, bm=0)
    if mask is not None:
        result.update(hasMask=True, masksProperties=[
            dict(inv=False, mode="s", pt=mask, o=static(100), x=static(0),
                 nm="Gap travels with the official diagonal")])
    return result


def generate():
    font = TTFont(FONT)
    glyphs = font.getGlyphSet()
    cmap = font.getBestCmap()
    recordings = {}
    for codepoint in (0xE890, 0xED1A):
        recording = RecordingPen()
        glyphs[cmap[codepoint]].draw(recording)
        recordings[codepoint] = recording

    eye = contours(recordings[0xE890])
    off = contours(recordings[0xED1A])

    # ED1A joins the slash to the left eye outline in its first contour.
    # Keep its real caps/curves, and bypass only the attached eye outline.
    first = recordings[0xED1A].value
    close = next(i for i, (op, _) in enumerate(first) if op == "closePath")
    first = first[:close + 1]
    return_edge = next(i for i, (op, args) in enumerate(first)
                       if op == "lineTo" and args == ((19, 1939),))
    slash_recording = RecordingPen()
    slash_recording.value = first[:6] + first[return_edge:]
    slash = contours(slash_recording)[0]

    # Shorten just the far cap, not the stroke width: the line draws from
    # upper-left to lower-right instead of scaling the entire glyph to a dot.
    start, end = point((64, 1984)), point((1984, 64))
    axis = difference(start, end)
    length_squared = sum(v * v for v in axis)
    short_slash = json.loads(json.dumps(slash))
    for i, vertex in enumerate(slash["v"]):
        projection = sum((vertex[j] - start[j]) * axis[j] for j in (0, 1))
        if projection > length_squared / 2:
            short_slash["v"][i] = [round(vertex[j] - axis[j], 5) for j in (0, 1)]

    # Source ED1A's opening boundaries are x+y=1958 and x+y=2348.
    # The strip is deliberately asymmetric around the slash (x+y=2048),
    # reproducing the real glyph's clearance on its upper-right side.
    def gap(progress):
        d = 1 / math.sqrt(2)
        back = (64 - 64 * d, 1984 + 64 * d)
        front = (64 + 1920 * progress + 64 * d,
                 1984 - 1920 * progress - 64 * d)
        vertices = [point((base[0] + offset / 2, base[1] + offset / 2))
                    for base, offset in ((back, -90), (front, -90),
                                         (front, 300), (back, 300))]
        return dict(v=vertices, i=[[0, 0]] * 4, o=[[0, 0]] * 4, c=True)

    # Use the untouched ED1A at its resting frames. No faded overlaps or
    # opaque background-coloured masks; all transitions remain transparent.
    animation = dict(v="5.12.2", fr=60, ip=0, op=OUT, w=SIZE, h=SIZE,
                     nm="Tonarink settings preview toggle icon", ddd=0, assets=[],
                     meta=dict(font=str(FONT), codepoints=["E890", "ED1A"],
                               glyphs=[cmap[0xE890], cmap[0xED1A]]),
                     layers=[
                         layer(1, "Official PreviewOff U+ED1A", off,
                               visibility([(0, 0), (END, 100), (REVERSE_START, 0)])),
                         layer(2, "Draw and retract extracted PreviewOff slash",
                               [state_path(short_slash, slash)],
                               visibility([(0, 0), (1, 100), (END, 0),
                                           (REVERSE_START, 100), (REVERSE_END, 0)])),
                         layer(3, "Official Preview U+E890 with travelling gap", eye,
                               visibility([(0, 100), (END, 0), (REVERSE_START, 100)]),
                               state_path(gap(0), gap(1)))],
                     markers=[
                         dict(tm=0, cm="On", dr=0), dict(tm=END, cm="Off", dr=0),
                         dict(tm=0, cm="OnToOff_Start", dr=0),
                         dict(tm=END, cm="OnToOff_End", dr=0),
                         dict(tm=REVERSE_START, cm="OffToOn_Start", dr=0),
                         dict(tm=REVERSE_END, cm="OffToOn_End", dr=0)])
    return animation


if __name__ == "__main__":
    serialized = json.dumps(generate(), ensure_ascii=False, separators=(",", ":")) + "\n"
    OUTPUT.write_text(serialized, encoding="utf-8")
    print(OUTPUT)
    if len(sys.argv) > 1:
        destination = (Path(sys.argv[1]) / "public/projects/tonarink-settings-preview/scene-1/lottie.json")
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(serialized, encoding="utf-8")
        print(destination)

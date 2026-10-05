"""Extract E7C3 and unfold only its corner, keeping the paper body fixed.

uv run --with fonttools python tools/build-document-unfold-lottie.py [player-root]
"""
from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src/Tonarink.App/Assets/Lottie/DocumentUnfoldIcon.json"
spec = importlib.util.spec_from_file_location(
    "folder_geometry", Path(__file__).with_name("build-folder-open-lottie.py"))
geometry = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(geometry)


def move(curves, x=0, y=0):
    return [tuple((p[0] + x, p[1] + y) for p in curve) for curve in curves]


def path(curves):
    # E7C3 uses the existing 40 px font frame, centered vertically at 24 px.
    vertices, incoming, outgoing = [], [], []
    def point(p):
        return [round(4 + p[0] * 40 / 2048, 6), round(44 - p[1] * 40 / 2048, 6)]
    for i, curve in enumerate(curves):
        a, b, _, _ = map(point, curve)
        previous = point(curves[i - 1][2])
        vertices.append(a)
        incoming.append([round(previous[j] - a[j], 6) for j in (0, 1)])
        outgoing.append([round(b[j] - a[j], 6) for j in (0, 1)])
    return dict(v=vertices, i=incoming, o=outgoing, c=True)


def pair(sections):
    folded, unfolded = [], []
    for a, b in sections:
        first, second = geometry.align(a, b)
        folded.extend(first)
        unfolded.extend(second)
    return path(folded), path(unfolded)


def animated(a, b):
    return dict(a=1, k=[
        dict(t=0, s=[a], e=[b], o=dict(x=[0.2], y=[0.75]), i=dict(x=[0.34], y=[0.94])),
        dict(t=13, s=[b], h=1),
        dict(t=17, s=[b], e=[a], o=dict(x=[0.35], y=[0]), i=dict(x=[0.75], y=[1])),
        dict(t=30, s=[a], h=1),
    ])


def generate():
    font = TTFont(geometry.FONT)
    contours = geometry.extract(font, 0xE7C3)
    line = geometry.line
    a = contours[0]
    # The two 45-degree outer arcs keep their radii; only the diagonal between
    # them shortens to zero. Do not resample/rephase the stationary body.
    outer = pair([
        (a[0], [line((1792, 1792), (1792, 256))]),
        *[(a[i], a[i]) for i in range(1, 6)],
        (a[6], [line((512, 2048), (1536, 2048))]),
        (a[7], move(a[7], x=528)),
        (a[8], [line((1717, 1973), (1717, 1973))]),
        (a[9], move(a[9], y=528)),
        (a[10], [line((1792, 1792), (1792, 1792))]),
    ])

    # The inner top-right arc comes from the glyph's actual 128-unit corner,
    # reflected vertically. The horizontal/vertical crease edges shorten to
    # zero; do not turn either straight edge into a moving diagonal.
    a = contours[1]
    top_right = [tuple((p[0], 2048 - p[1]) for p in curve) for curve in a[6]]
    top_right = [tuple(reversed(curve)) for curve in reversed(top_right)]
    inner = pair([
        (a[0], [line((1536, 1920), (1536, 1920))]),
        (a[1], [line((1536, 1920), (512, 1920))]),
        *[(a[i], a[i]) for i in range(2, 7)],
        (a[7], [line((1664, 256), (1664, 1792))]),
        (a[8], [line((1664, 1792), (1664, 1792))]),
        (a[9], [line((1664, 1792), (1664, 1792))]),
        (a[10], top_right),
        (a[11], [line((1536, 1920), (1536, 1920))]),
    ])
    triangle = [curve for section in contours[2] for curve in section]
    flap = (path(triangle), path([line((1664, 1920), (1664, 1920))] * len(triangle)))

    document = json.loads(OUTPUT.read_text(encoding="utf-8"))
    shapes = [s for s in document["layers"][0]["shapes"][0]["it"] if s["ty"] == "sh"]
    for shape, name, (first, second) in zip(shapes,
            ("Paper outer edge", "Paper inner edge and crease", "Fold opening"),
            (outer, inner, flap)):
        shape.update(nm=name, ks=animated(first, second))
    return document


if __name__ == "__main__":
    encoded = json.dumps(generate(), separators=(",", ":")) + "\n"
    OUTPUT.write_text(encoded, encoding="utf-8")
    print(OUTPUT)
    if len(sys.argv) > 1:
        target = Path(sys.argv[1]) / "public/projects/tonarink-document-outline-fix/scene-1/lottie.json"
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists():
            json.loads(target.read_text(encoding="utf-8"))
        target.write_text(encoded, encoding="utf-8")
        print(target)

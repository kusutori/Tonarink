"""Animate the exact EC1B badge glyph with a compact pulse and outline emphasis.

uv run --with fonttools python tools/build-settings-receive-badge-lottie.py [player-root]
"""
import importlib.util
import json
import sys
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src/Tonarink.App/Assets/Lottie/SettingsReceiveBadgeToggleIcon.json"
FONT = Path("C:/Windows/Fonts/SegoeIcons.ttf")
CODEPOINT = 0xEC1B

spec = importlib.util.spec_from_file_location(
    "folder_contours", Path(__file__).with_name("build-folder-open-lottie.py"))
contour_tools = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(contour_tools)


def static(value):
    return dict(a=0, k=value)


def animated(samples):
    # A restrained ease-out for the pulse; explicit scale poses provide its
    # anticipation and single overshoot rather than an oscillating expression.
    keys = [dict(t=frame, s=value, e=samples[index + 1][1],
                 o=dict(x=[0.2], y=[0.75]), i=dict(x=[0.34], y=[0.94]))
            for index, (frame, value) in enumerate(samples[:-1])]
    keys.append(dict(t=samples[-1][0], s=samples[-1][1]))
    return dict(a=1, k=keys)


def generate():
    with TTFont(FONT) as font:
        glyph_name = font.getBestCmap()[CODEPOINT]
        glyph_set = font.getGlyphSet()
        bounds = BoundsPen(glyph_set)
        glyph_set[glyph_name].draw(bounds)
        x0, y0, x1, y1 = bounds.bounds
        # Leave room for the 6% overshoot without changing the 24px layout slot.
        scale = 44 / max(x1 - x0, y1 - y0)
        left = (48 - (x1 - x0) * scale) / 2
        top = (48 - (y1 - y0) * scale) / 2

        def point(p):
            return [round(left + (p[0] - x0) * scale, 6),
                    round(top + (y1 - p[1]) * scale, 6)]

        shapes = []
        for index, sections in enumerate(contour_tools.extract(font, CODEPOINT)):
            curves = sum(sections, [])
            vertices, incoming, outgoing = [], [], []
            for j, curve in enumerate(curves):
                a, b, _, _ = map(point, curve)
                c = point(curves[j - 1][2])
                vertices.append(a)
                incoming.append([round(c[k] - a[k], 6) for k in (0, 1)])
                outgoing.append([round(b[k] - a[k], 6) for k in (0, 1)])
            shapes.append(dict(ty="sh", nm=f"Badge contour {index + 1}",
                               ks=static(dict(v=vertices, i=incoming, o=outgoing, c=True))))

    shapes.append(dict(ty="fl", nm="Foreground {Color:var(Foreground)}",
                       c=static([0, 0, 0, 1]), o=static(100), r=1))
    scaling = animated([
        (0, [100, 100, 100]), (5, [94, 94, 100]),
        (14, [106, 106, 100]), (32, [100, 100, 100]),
        (40, [100, 100, 100]), (48, [94, 94, 100]),
        (64, [100, 100, 100]), (68, [100, 100, 100]),
    ])
    opacity = animated([
        (0, [100]), (5, [72]), (14, [100]), (32, [100]),
        (40, [100]), (48, [78]), (64, [100]), (68, [100]),
    ])
    markers = [dict(tm=0, cm="Off", dr=0), dict(tm=36, cm="On", dr=0)]
    for transition, start, end in (("OffToOn", 0, 32), ("OnToOff", 40, 68)):
        markers.extend([dict(tm=start, cm=f"{transition}_Start", dr=0),
                        dict(tm=end, cm=f"{transition}_End", dr=0)])
    return dict(v="5.12.2", fr=60, ip=0, op=72, w=48, h=48,
                nm="Tonarink receive badge pulse", ddd=0, assets=[],
                meta=dict(font=str(FONT), codepoint=f"{CODEPOINT:04X}", glyph=glyph_name),
                markers=markers, layers=[dict(ddd=0, ind=1, ty=4,
                    nm="Exact Segoe Fluent Icons EC1B", sr=1,
                    ks=dict(o=opacity, r=static(0), p=static([24, 24, 0]),
                            a=static([24, 24, 0]), s=scaling),
                    ao=0, ip=0, op=72, st=0, bm=0, shapes=shapes)])


if __name__ == "__main__":
    encoded = json.dumps(generate(), separators=(",", ":")) + "\n"
    OUTPUT.write_text(encoded, encoding="utf-8")
    print(OUTPUT)
    if len(sys.argv) > 1:
        scene = Path(sys.argv[1]) / "public/projects/tonarink-receive-badge/scene-1/lottie.json"
        scene.parent.mkdir(parents=True, exist_ok=True)
        scene.write_text(encoded, encoding="utf-8")
        print(scene)

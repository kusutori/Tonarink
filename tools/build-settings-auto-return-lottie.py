"""Pulse the exact Segoe Fluent Icons E80F home glyph without changing its outline.

uv run --with fonttools python tools/build-settings-auto-return-lottie.py [player-root]
"""
import importlib.util
import json
import sys
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src/Tonarink.App/Assets/Lottie/SettingsAutoReturnToggleIcon.json"
FONT = Path("C:/Windows/Fonts/SegoeIcons.ttf")
CODEPOINT = 0xE80F

# This extractor preserves the font's curves and does not run its own generator on import.
spec = importlib.util.spec_from_file_location(
    "folder_contours", Path(__file__).with_name("build-folder-open-lottie.py"))
contour_tools = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(contour_tools)


def generate():
    with TTFont(FONT) as font:
        glyph_name = font.getBestCmap()[CODEPOINT]
        glyph_set = font.getGlyphSet()
        bounds = BoundsPen(glyph_set)
        glyph_set[glyph_name].draw(bounds)
        x0, y0, x1, y1 = bounds.bounds
        scale = 46 / max(x1 - x0, y1 - y0)
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
            shapes.append(dict(ty="sh", nm=f"Home contour {index + 1}",
                               ks=dict(a=0, k=dict(v=vertices, i=incoming, o=outgoing, c=True))))

    shapes.append(dict(ty="fl", nm="Foreground {Color:var(Foreground)}",
                       c=dict(a=0, k=[0, 0, 0, 1]), o=dict(a=0, k=100), r=1))
    static = lambda value: dict(a=0, k=value)
    # Shrink within the existing canvas so the icon never clips or moves its layout slot.
    scaling = dict(a=1, k=[
        dict(t=0, s=[100, 100, 100], e=[88, 88, 100],
             o=dict(x=[0.2], y=[0.75]), i=dict(x=[0.34], y=[0.94])),
        dict(t=10, s=[88, 88, 100], e=[100, 100, 100],
             o=dict(x=[0], y=[0.65]), i=dict(x=[0.51], y=[0.99])),
        dict(t=28, s=[100, 100, 100]),
    ])
    markers = [dict(tm=0, cm="Off", dr=0), dict(tm=28, cm="On", dr=0)]
    for transition in ("OffToOn", "OnToOff"):
        markers.extend([dict(tm=0, cm=f"{transition}_Start", dr=0),
                        dict(tm=28, cm=f"{transition}_End", dr=0)])
    return dict(v="5.12.2", fr=60, ip=0, op=36, w=48, h=48,
                nm="Tonarink automatic return home pulse", ddd=0, assets=[],
                meta=dict(font=str(FONT), codepoint=f"{CODEPOINT:04X}", glyph=glyph_name),
                markers=markers, layers=[dict(ddd=0, ind=1, ty=4,
                    nm="Exact Segoe Fluent Icons E80F", sr=1,
                    ks=dict(o=static(100), r=static(0), p=static([24, 24, 0]),
                            a=static([24, 24, 0]), s=scaling),
                    ao=0, ip=0, op=36, st=0, bm=0, shapes=shapes)])


if __name__ == "__main__":
    encoded = json.dumps(generate(), separators=(",", ":")) + "\n"
    OUTPUT.write_text(encoded, encoding="utf-8")
    print(OUTPUT)
    if len(sys.argv) > 1:
        scene = Path(sys.argv[1]) / "public/projects/tonarink-auto-return/scene-1/lottie.json"
        scene.parent.mkdir(parents=True, exist_ok=True)
        scene.write_text(encoded, encoding="utf-8")
        print(scene)

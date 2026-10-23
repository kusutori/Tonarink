"""Keep the EADF clipboard still while its two checked rows fade out and return.

uv run --with fonttools python tools/build-settings-jump-list-lottie.py [player-root]
"""
import importlib.util
import json
import sys
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src/Tonarink.App/Assets/Lottie/SettingsJumpListToggleIcon.json"
FONT = Path("C:/Windows/Fonts/SegoeIcons.ttf")
CODEPOINT = 0xEADF

spec = importlib.util.spec_from_file_location(
    "folder_contours", Path(__file__).with_name("build-folder-open-lottie.py"))
contour_tools = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(contour_tools)


def static(value):
    return dict(a=0, k=value)


def row_opacity(delay):
    samples = [(0, 100), (10, 0), (18, 0), (30, 100)]
    keys = [dict(t=frame + delay, s=[value], e=[samples[index + 1][1]],
                 o=dict(x=[0.2], y=[0.75]), i=dict(x=[0.34], y=[0.94]))
            for index, (frame, value) in enumerate(samples[:-1])]
    keys.append(dict(t=30 + delay, s=[100]))
    if delay:
        keys.insert(0, dict(t=0, s=[100], e=[100],
                            o=dict(x=[0], y=[0]), i=dict(x=[1], y=[1])))
    return dict(a=1, k=keys)


def layer(index, name, paths, opacity):
    return dict(ddd=0, ind=index, ty=4, nm=name, sr=1,
                ks=dict(o=opacity, r=static(0), p=static([0, 0, 0]),
                        a=static([0, 0, 0]), s=static([100, 100, 100])),
                ao=0, ip=0, op=44, st=0, bm=0,
                shapes=[*paths, dict(ty="fl", nm="Foreground {Color:var(Foreground)}",
                                    c=static([0, 0, 0, 1]), o=static(100), r=1)])


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

        paths = []
        for index, sections in enumerate(contour_tools.extract(font, CODEPOINT)):
            curves = sum(sections, [])
            vertices, incoming, outgoing = [], [], []
            for j, curve in enumerate(curves):
                a, b, _, _ = map(point, curve)
                c = point(curves[j - 1][2])
                vertices.append(a)
                incoming.append([round(c[k] - a[k], 6) for k in (0, 1)])
                outgoing.append([round(b[k] - a[k], 6) for k in (0, 1)])
            paths.append(dict(ty="sh", nm=f"EADF contour {index + 1}",
                              ks=static(dict(v=vertices, i=incoming, o=outgoing, c=True))))

    # The real glyph has the clipboard boundary + two cutouts, then two pairs
    # of check mark / text line contours. Keep the cutouts in the same fill.
    if len(paths) != 7:
        raise ValueError(f"Expected seven EADF contours, got {len(paths)}")
    markers = [dict(tm=0, cm="Off", dr=0), dict(tm=38, cm="On", dr=0)]
    for transition in ("OffToOn", "OnToOff"):
        markers.extend([dict(tm=0, cm=f"{transition}_Start", dr=0),
                        dict(tm=38, cm=f"{transition}_End", dr=0)])
    return dict(v="5.12.2", fr=60, ip=0, op=44, w=48, h=48,
                nm="Tonarink jump list checked rows", ddd=0, assets=[],
                meta=dict(font=str(FONT), codepoint=f"{CODEPOINT:04X}", glyph=glyph_name),
                markers=markers, layers=[
                    layer(1, "Upper checked row", paths[3:5], row_opacity(0)),
                    layer(2, "Lower checked row", paths[5:7], row_opacity(4)),
                    layer(3, "Stationary clipboard", paths[:3], static(100)),
                ])


if __name__ == "__main__":
    encoded = json.dumps(generate(), separators=(",", ":")) + "\n"
    OUTPUT.write_text(encoded, encoding="utf-8")
    print(OUTPUT)
    if len(sys.argv) > 1:
        scene = Path(sys.argv[1]) / "public/projects/tonarink-jump-list/scene-1/lottie.json"
        scene.parent.mkdir(parents=True, exist_ok=True)
        scene.write_text(encoded, encoding="utf-8")
        print(scene)

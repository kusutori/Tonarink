"""Pass the indentation of the exact E8A4 glyph upward, then back down.

uv run --with fonttools python tools/build-settings-execution-alias-lottie.py [player-root]
"""
import importlib.util
import json
import sys
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src/Tonarink.App/Assets/Lottie/SettingsExecutionAliasIcon.json"
# Reuse the existing exact quadratic-to-cubic contour extractor without running
# its folder-icon generator (the latter is guarded by __main__).
spec = importlib.util.spec_from_file_location("folder_contours", Path(__file__).with_name("build-folder-open-lottie.py"))
contour_tools = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(contour_tools)
extract = contour_tools.extract


def generate():
    font = TTFont("C:/Windows/Fonts/SegoeIcons.ttf")
    glyph = font.getGlyphSet()[font.getBestCmap()[0xE8A4]]
    bounds = BoundsPen(font.getGlyphSet())
    glyph.draw(bounds)
    x0, y0, x1, y1 = bounds.bounds
    scale = 46 / max(x1 - x0, y1 - y0)
    left = (48 - (x1 - x0) * scale) / 2
    top = (48 - (y1 - y0) * scale) / 2
    contours = [sum(sections, []) for sections in extract(font, 0xE8A4)]
    assert len(contours) == 6, "Expected three dot/line pairs in E8A4"
    # Pin the right caps. Only the left cap and the adjacent straight edge move;
    # unlike scaling the capsule, this preserves the original font's round ends.
    poses = [(0, (0, 0, 0)), (14, (0, 512, -512)),
             (28, (512, 0, -512)), (36, (512, 0, -512)),
             (50, (0, 512, -512)), (64, (0, 0, 0))]

    def path(curves, contour_index, offset):
        is_dot = contour_index % 2 == 0
        cap_center = 1088 if contour_index == 5 else 576

        def point(p):
            shift = offset if is_dot or p[0] <= cap_center else 0
            return [round(left + (p[0] + shift - x0) * scale, 6),
                    round(top + (y1 - p[1]) * scale, 6)]

        v, incoming, outgoing = [], [], []
        for j, curve in enumerate(curves):
            a, b, _, _ = map(point, curve)
            c = point(curves[j - 1][2])
            v.append(a)
            incoming.append([round(c[k] - a[k], 6) for k in (0, 1)])
            outgoing.append([round(b[k] - a[k], 6) for k in (0, 1)])
        return dict(v=v, i=incoming, o=outgoing, c=True)

    shapes = []
    for index, curves in enumerate(contours):
        values = [path(curves, index, offsets[index // 2]) for _, offsets in poses]
        keys = [dict(t=frame, s=[values[j]], e=[values[j + 1]],
                     o=dict(x=[0.25], y=[0]), i=dict(x=[0.35], y=[1]))
                for j, (frame, _) in enumerate(poses[:-1])]
        keys.append(dict(t=64, s=[values[-1]], h=1))
        shapes.append(dict(ty="sh", nm=f"Row {index // 2 + 1} {'dot' if index % 2 == 0 else 'line'}",
                           ks=dict(a=1, k=keys)))
    shapes.append(dict(ty="fl", nm="Foreground {Color:var(Foreground)}",
                       c=dict(a=0, k=[0, 0, 0, 1]), o=dict(a=0, k=100), r=1))
    states = [f"Alias{i}" for i in range(3)]
    markers = [dict(tm=0, cm=state, dr=0) for state in states]
    for a in states:
        for b in states:
            if a != b:
                markers.extend([dict(tm=0, cm=f"{a}To{b}_Start", dr=0),
                                dict(tm=64, cm=f"{a}To{b}_End", dr=0)])
    static = lambda value: dict(a=0, k=value)
    return dict(v="5.12.2", fr=60, ip=0, op=68, w=48, h=48,
                nm="Tonarink execution alias indentation wave", ddd=0, assets=[],
                markers=markers, layers=[dict(ddd=0, ind=1, ty=4,
                    nm="Exact Segoe Fluent Icons E8A4", sr=1,
                    ks=dict(o=static(100), r=static(0), p=static([0, 0, 0]),
                            a=static([0, 0, 0]), s=static([100, 100, 100])),
                    ao=0, ip=0, op=68, st=0, bm=0, shapes=shapes)])


if __name__ == "__main__":
    encoded = json.dumps(generate(), separators=(",", ":")) + "\n"
    OUTPUT.write_text(encoded, encoding="utf-8")
    print(OUTPUT)
    if len(sys.argv) > 1:
        scene = Path(sys.argv[1]) / "public/projects/tonarink-execution-alias/scene-1/lottie.json"
        scene.parent.mkdir(parents=True, exist_ok=True)
        scene.write_text(encoded, encoding="utf-8")
        print(scene)

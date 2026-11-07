"""Animate the actual E70E chevron: compress on press, spring open on release.

uv run --with fonttools python tools/build-collapse-types-lottie.py [player-root]
"""
import importlib.util
import json
import sys
from pathlib import Path

from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src/Tonarink.App/Assets/Lottie/CollapseTypesIcon.json"
spec = importlib.util.spec_from_file_location(
    "arrow_tools", Path(__file__).with_name("build-undo-redo-lottie.py"))
arrow_tools = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(arrow_tools)


def static(value):
    return dict(a=0, k=value)


def generate():
    with TTFont(arrow_tools.FONT_PATH) as font:
        glyph_name, contours = arrow_tools.extract_contours(font, 0xE70E)
    bottom = max(point[1] for contour in contours for point in contour["v"])
    poses = [(0, [100, 100, 100]), (8, [94, 56, 100]),
             (14, [102, 115, 100]), (19, [100, 97, 100]), (25, [100, 100, 100])]
    keyframes = []
    for index, (frame, pose) in enumerate(poses):
        key = dict(t=frame, s=pose)
        if index + 1 < len(poses):
            key.update(e=poses[index + 1][1],
                       o=dict(x=[0.2], y=[0.75]), i=dict(x=[0.34], y=[0.94]))
        keyframes.append(key)
    shapes = [dict(ty="sh", nm=f"E70E contour {index + 1}", ks=static(contour))
              for index, contour in enumerate(contours)]
    shapes.append(dict(ty="fl", nm="Foreground {Color:var(Foreground)}",
                       c=static([0, 0, 0, 1]), o=static(100), r=1))
    return dict(v="5.12.2", fr=60, ip=0, op=26, w=48, h=48,
                nm="Tonarink collapse types - chevron compression and spring", ddd=0, assets=[],
                meta=dict(font=str(arrow_tools.FONT_PATH), codepoint="E70E", glyph=glyph_name),
                markers=arrow_tools.markers(), layers=[dict(ddd=0, ind=1, ty=4,
                    nm="Exact Segoe Fluent Icons E70E", sr=1,
                    ks=dict(o=static(100), r=static(0), p=static([24, bottom, 0]),
                            a=static([24, bottom, 0]), s=dict(a=1, k=keyframes)),
                    ao=0, ip=0, op=26, st=0, bm=0, shapes=shapes)])


if __name__ == "__main__":
    encoded = json.dumps(generate(), separators=(",", ":")) + "\n"
    OUTPUT.write_text(encoded, encoding="utf-8")
    print(OUTPUT)
    if len(sys.argv) > 1:
        scene = Path(sys.argv[1]) / "public/projects/tonarink-collapse-types/scene-1/lottie.json"
        scene.parent.mkdir(parents=True, exist_ok=True)
        scene.write_text(encoded, encoding="utf-8")
        print(scene)

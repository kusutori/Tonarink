"""Animate the real E8B1 arrows along opposing curves, exiting and wrapping.

uv run --with fonttools python tools/build-shuffle-pin-lottie.py [player-root]
"""
import importlib.util
import json
import sys
from pathlib import Path

from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src/Tonarink.App/Assets/Lottie/ShufflePinIcon.json"
FONT = Path("C:/Windows/Fonts/SegoeIcons.ttf")
spec = importlib.util.spec_from_file_location(
    "glyph_contours", Path(__file__).with_name("build-folder-open-lottie.py"))
contours = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(contours)


def static(value):
    return dict(a=0, k=value)


def shape(curves):
    def point(p):
        return [round(4 + p[0] * 40 / 2048, 6),
                round(24 - (p[1] - 1024) * 40 / 2048, 6)]

    vertices, incoming, outgoing = [], [], []
    for j, curve in enumerate(curves):
        a, b = point(curve[0]), point(curve[1])
        c = point(curves[j - 1][2])
        vertices.append(a)
        incoming.append([round(c[k] - a[k], 6) for k in (0, 1)])
        outgoing.append([round(b[k] - a[k], 6) for k in (0, 1)])
    return dict(ty="sh", ks=static(dict(v=vertices, i=incoming, o=outgoing, c=True)))


def position(delay, direction):
    samples = []
    for frame in range(28):
        local = frame - delay
        if local <= 0 or local >= 22:
            value = [0, 0, 0]
        elif local <= 8:
            u = local / 8
            value = [52 * u * u, direction * 12 * u ** 3, 0]
        else:
            v = (local - 9) / 13
            value = [-52 * (1 - v) ** 3, -direction * 12 * (1 - v) ** 2, 0]
        samples.append([round(x, 6) for x in value])
    keys = []
    for frame, value in enumerate(samples):
        key = dict(t=frame, s=value)
        if frame < 27:
            key.update(e=samples[frame + 1],
                       o=dict(x=0, y=0), i=dict(x=1, y=1))
            # Both poses are outside the canvas: never tween the wrap across it.
            if frame == delay + 8:
                key["h"] = 1
        keys.append(key)
    return dict(a=1, k=keys)


def layer(index, name, geometry, delay, direction):
    return dict(ddd=0, ind=index, ty=4, nm=name, sr=1, ao=0,
                ks=dict(o=static(100), r=static(0), p=position(delay, direction),
                        a=static([0, 0, 0]), s=static([100, 100, 100])),
                shapes=[*geometry, dict(ty="fl", nm="Foreground {Color:var(Foreground)}",
                                        c=static([0, 0, 0, 1]), o=static(100), r=1)],
                ip=0, op=28, st=0, bm=0)


def generate():
    with TTFont(FONT) as font:
        glyph_name = font.getBestCmap()[0xE8B1]
        original = contours.extract(font, 0xE8B1)
    if len(original) != 2 or len(original[0]) != 27:
        raise ValueError("E8B1 outline changed; recheck the crossing split")
    first, second = original
    # The font merges the upper-left branch with the rising arrow. Cut only
    # along the existing crossing junction; preserve every original curve.
    junction = contours.line(first[7][-1][-1], first[13][0][0])
    rising = sum(first[:8], []) + [junction] + sum(first[13:], [])
    falling_start = sum(first[8:13], []) + [
        contours.line(first[12][-1][-1], first[8][0][0])]
    markers = [dict(tm=0, cm="Normal", dr=0), dict(tm=26, cm="Shuffled", dr=0)]
    for transition in ("NormalToShuffled", "ShuffledToNormal"):
        markers.extend([dict(tm=0, cm=f"{transition}_Start", dr=0),
                        dict(tm=26, cm=f"{transition}_End", dr=0)])
    return dict(v="5.12.2", fr=60, ip=0, op=28, w=48, h=48,
                nm="Tonarink PIN shuffle curved wrap", ddd=0, assets=[],
                meta=dict(font=str(FONT), codepoint="E8B1", glyph=glyph_name),
                markers=markers, layers=[
                    layer(1, "Rising arrow", [shape(rising)], 0, -1),
                    layer(2, "Falling arrow", [shape(falling_start), shape(sum(second, []))], 2, 1),
                ])


if __name__ == "__main__":
    encoded = json.dumps(generate(), separators=(",", ":")) + "\n"
    OUTPUT.write_text(encoded, encoding="utf-8")
    print(OUTPUT)
    if len(sys.argv) > 1:
        scene = Path(sys.argv[1]) / "public/projects/tonarink-pin-shuffle/scene-1/lottie.json"
        scene.parent.mkdir(parents=True, exist_ok=True)
        scene.write_text(encoded, encoding="utf-8")
        print(scene)

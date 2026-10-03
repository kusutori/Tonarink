from __future__ import annotations

import json
import sys
from pathlib import Path

from fontTools.pens.qu2cuPen import Qu2CuPen
from fontTools.pens.recordingPen import RecordingPen
from fontTools.ttLib import TTFont


ROOT = Path(__file__).resolve().parents[1]
FONT_PATH = Path(r"C:\Windows\Fonts\SegoeIcons.ttf")
OUTPUT_PATH = ROOT / "src" / "Tonarink.App" / "Assets" / "Lottie" / "QuickActionsSparkleIcon.json"
CODEPOINT = 0xF6C7
SCALE = 40 / 2048


def point(value: tuple[float, float]) -> list[float]:
    x, y = value
    return [round(4 + x * SCALE, 4), round(44 - y * SCALE, 4)]


def vector(start: list[float], end: list[float]) -> list[float]:
    return [round(end[0] - start[0], 4), round(end[1] - start[1], 4)]


def extract_contours() -> tuple[str, list[dict[str, object]]]:
    font = TTFont(FONT_PATH)
    glyph_set = font.getGlyphSet()
    glyph_name = font.getBestCmap()[CODEPOINT]
    recording = RecordingPen()
    glyph_set[glyph_name].draw(Qu2CuPen(recording, 1.0, all_cubic=True))

    contours: list[dict[str, object]] = []
    vertices: list[list[float]] = []
    incoming: list[list[float]] = []
    outgoing: list[list[float]] = []

    def finish(closed: bool) -> None:
        nonlocal vertices, incoming, outgoing
        if vertices:
            contours.append({"i": incoming, "o": outgoing, "v": vertices, "c": closed})
        vertices, incoming, outgoing = [], [], []

    for operation, args in recording.value:
        if operation == "moveTo":
            finish(False)
            vertices.append(point(args[0]))
            incoming.append([0, 0])
            outgoing.append([0, 0])
        elif operation == "lineTo":
            vertices.append(point(args[0]))
            incoming.append([0, 0])
            outgoing.append([0, 0])
        elif operation == "curveTo":
            control1, control2, destination = map(point, args)
            outgoing[-1] = vector(vertices[-1], control1)
            vertices.append(destination)
            incoming.append(vector(destination, control2))
            outgoing.append([0, 0])
        elif operation == "closePath":
            finish(True)
        elif operation == "endPath":
            finish(False)
        else:
            raise ValueError(f"Unsupported pen operation: {operation}")

    finish(False)
    return glyph_name, contours


def animated_property(keyframes: list[dict[str, object]]) -> dict[str, object]:
    return {"a": 1, "k": keyframes}


def keyframe(
    frame: int,
    start: list[float],
    end: list[float] | None = None,
    easing: tuple[float, float, float, float] | None = None,
) -> dict[str, object]:
    result: dict[str, object] = {"t": frame, "s": start}
    if end is not None:
        result["e"] = end
    if easing is not None:
        x1, y1, x2, y2 = easing
        result["o"] = {"x": [x1], "y": [y1]}
        result["i"] = {"x": [x2], "y": [y2]}
    return result


def layer(
    index: int,
    name: str,
    contour: dict[str, object],
    anchor: list[float],
    position: dict[str, object] | list[float],
    scale: dict[str, object] | list[float],
    rotation: dict[str, object] | float = 0,
    opacity: dict[str, object] | float = 100,
) -> dict[str, object]:
    def prop(value: dict[str, object] | list[float] | float) -> dict[str, object]:
        return value if isinstance(value, dict) else {"a": 0, "k": value}

    return {
        "ddd": 0,
        "ind": index,
        "ty": 4,
        "nm": name,
        "sr": 1,
        "ks": {
            "o": prop(opacity),
            "r": prop(rotation),
            "p": prop(position),
            "a": {"a": 0, "k": [*anchor, 0]},
            "s": prop(scale),
        },
        "ao": 0,
        "shapes": [
            {
                "ty": "gr",
                "nm": name,
                "it": [
                    {"ty": "sh", "nm": f"{name} contour", "ks": {"a": 0, "k": contour}},
                    {
                        "ty": "fl",
                        "nm": "Foreground {Color:var(Foreground)}",
                        "c": {"a": 0, "k": [0, 0, 0, 1]},
                        "o": {"a": 0, "k": 100},
                        "r": 1,
                    },
                    {
                        "ty": "tr",
                        "p": {"a": 0, "k": [0, 0]},
                        "a": {"a": 0, "k": [0, 0]},
                        "s": {"a": 0, "k": [100, 100]},
                        "r": {"a": 0, "k": 0},
                        "o": {"a": 0, "k": 100},
                        "sk": {"a": 0, "k": 0},
                        "sa": {"a": 0, "k": 0},
                    },
                ],
            }
        ],
        "ip": 0,
        "op": 36,
        "st": 0,
        "bm": 0,
    }


glyph_name, contours = extract_contours()
if len(contours) != 3:
    raise ValueError(f"Expected three contours for U+{CODEPOINT:04X}, got {len(contours)}")

ease_out = (0.20, 0.75, 0.34, 0.94)
kinetic = (0.85, 0.46, 0.14, 0.53)
settle = (0.00, 0.65, 0.51, 0.99)

pen_position = animated_property([
    keyframe(0, [24, 24, 0], [23.1, 25.0, 0], ease_out),
    keyframe(6, [23.1, 25.0, 0], [24.8, 23.4, 0], kinetic),
    keyframe(13, [24.8, 23.4, 0], [24, 24, 0], settle),
    keyframe(22, [24, 24, 0]),
])
pen_rotation = animated_property([
    keyframe(0, [0], [-2.2], ease_out),
    keyframe(6, [-2.2], [1.8], kinetic),
    keyframe(13, [1.8], [0], settle),
    keyframe(22, [0]),
])

big_star_scale = animated_property([
    keyframe(0, [100, 100, 100], [94, 94, 100], ease_out),
    keyframe(7, [94, 94, 100], [118, 118, 100], ease_out),
    keyframe(13, [118, 118, 100], [100, 100, 100], settle),
    keyframe(22, [100, 100, 100]),
])
big_star_opacity = animated_property([
    keyframe(0, [100], [72], ease_out),
    keyframe(7, [72], [100], ease_out),
    keyframe(13, [100]),
])

small_star_scale = animated_property([
    keyframe(0, [100, 100, 100]),
    keyframe(8, [100, 100, 100], [90, 90, 100], ease_out),
    keyframe(13, [90, 90, 100], [122, 122, 100], ease_out),
    keyframe(19, [122, 122, 100], [100, 100, 100], settle),
    keyframe(28, [100, 100, 100]),
])
small_star_opacity = animated_property([
    keyframe(0, [100]),
    keyframe(8, [100], [62], ease_out),
    keyframe(13, [62], [100], ease_out),
    keyframe(19, [100]),
])

lottie = {
    "v": "5.12.2",
    "fr": 60,
    "ip": 0,
    "op": 36,
    "w": 48,
    "h": 48,
    "nm": "Tonarink quick actions sparkle icon (Segoe Fluent Icons)",
    "ddd": 0,
    "assets": [],
    "meta": {
        "font": str(FONT_PATH),
        "codepoint": f"{CODEPOINT:04X}",
        "glyph": glyph_name,
    },
    "layers": [
        layer(1, "Magic pen U+F6C7", contours[2], [24, 24], pen_position, [100, 100, 100], pen_rotation),
        layer(2, "Large sparkle U+F6C7", contours[0], [12.7, 12.75], [12.7, 12.75, 0], big_star_scale, opacity=big_star_opacity),
        layer(3, "Small sparkle U+F6C7", contours[1], [20.25, 25.25], [20.25, 25.25, 0], small_star_scale, opacity=small_star_opacity),
    ],
    "markers": [],
}

serialized = json.dumps(lottie, ensure_ascii=False, separators=(",", ":")) + "\n"
OUTPUT_PATH.write_text(serialized, encoding="utf-8")
print(OUTPUT_PATH)
if len(sys.argv) > 1:
    player_scene_path = Path(sys.argv[1]).resolve()
    player_scene_path.parent.mkdir(parents=True, exist_ok=True)
    player_scene_path.write_text(serialized, encoding="utf-8")
    print(player_scene_path)

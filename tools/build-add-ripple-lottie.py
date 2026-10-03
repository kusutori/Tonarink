from __future__ import annotations

import json
import sys
from pathlib import Path

from fontTools.pens.qu2cuPen import Qu2CuPen
from fontTools.pens.recordingPen import RecordingPen
from fontTools.ttLib import TTFont


ROOT = Path(__file__).resolve().parents[1]
FONT_PATH = Path(r"C:\Windows\Fonts\SegoeIcons.ttf")
OUTPUT_PATH = ROOT / "src" / "Tonarink.App" / "Assets" / "Lottie" / "AddRippleIcon.json"
CODEPOINT = 0xE710
CANVAS_SIZE = 48
GLYPH_SIZE = 32


def point(value: tuple[float, float], bounds: tuple[float, float, float, float]) -> list[float]:
    x_min, y_min, x_max, y_max = bounds
    scale = GLYPH_SIZE / max(x_max - x_min, y_max - y_min)
    left = (CANVAS_SIZE - (x_max - x_min) * scale) / 2
    top = (CANVAS_SIZE - (y_max - y_min) * scale) / 2
    x, y = value
    return [round(left + (x - x_min) * scale, 4), round(top + (y_max - y) * scale, 4)]


def vector(start: list[float], end: list[float]) -> list[float]:
    return [round(end[0] - start[0], 4), round(end[1] - start[1], 4)]


def extract_contour() -> tuple[str, dict[str, object]]:
    font = TTFont(FONT_PATH)
    glyph_set = font.getGlyphSet()
    glyph_name = font.getBestCmap()[CODEPOINT]

    from fontTools.pens.boundsPen import BoundsPen

    bounds_pen = BoundsPen(glyph_set)
    glyph_set[glyph_name].draw(bounds_pen)
    if bounds_pen.bounds is None:
        raise ValueError(f"U+{CODEPOINT:04X} has no bounds")

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
            vertices.append(point(args[0], bounds_pen.bounds))
            incoming.append([0, 0])
            outgoing.append([0, 0])
        elif operation == "lineTo":
            vertices.append(point(args[0], bounds_pen.bounds))
            incoming.append([0, 0])
            outgoing.append([0, 0])
        elif operation == "curveTo":
            control1, control2, destination = (point(item, bounds_pen.bounds) for item in args)
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
    if len(contours) != 1:
        raise ValueError(f"Expected one contour for U+{CODEPOINT:04X}, got {len(contours)}")
    return glyph_name, contours[0]


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


def animated(keyframes: list[dict[str, object]]) -> dict[str, object]:
    return {"a": 1, "k": keyframes}


def transform(scale: dict[str, object], opacity: dict[str, object]) -> dict[str, object]:
    return {
        "o": opacity,
        "r": {"a": 0, "k": 0},
        "p": {"a": 0, "k": [24, 24, 0]},
        "a": {"a": 0, "k": [24, 24, 0]},
        "s": scale,
    }


def shape_layer(index: int, name: str, shapes: list[dict[str, object]], scale: dict[str, object], opacity: dict[str, object]) -> dict[str, object]:
    return {
        "ddd": 0,
        "ind": index,
        "ty": 4,
        "nm": name,
        "sr": 1,
        "ks": transform(scale, opacity),
        "ao": 0,
        "shapes": shapes,
        "ip": 0,
        "op": 18,
        "st": 0,
        "bm": 0,
    }


glyph_name, plus_contour = extract_contour()
ease_out = (0.20, 0.75, 0.34, 0.94)
settle = (0.00, 0.65, 0.51, 0.99)

plus_scale = animated([
    keyframe(0, [100, 100, 100], [91, 91, 100], ease_out),
    keyframe(3, [91, 91, 100], [113, 113, 100], ease_out),
    keyframe(8, [113, 113, 100], [100, 100, 100], settle),
    keyframe(14, [100, 100, 100]),
])
ripple_scale = animated([
    keyframe(1, [22, 22, 100], [108, 108, 100], ease_out),
    keyframe(11, [108, 108, 100]),
])
ripple_opacity = animated([
    keyframe(0, [0], [42], ease_out),
    keyframe(2, [42], [0], ease_out),
    keyframe(11, [0]),
])

foreground_fill = {
    "ty": "fl",
    "nm": "Foreground {Color:var(Foreground)}",
    "c": {"a": 0, "k": [0, 0, 0, 1]},
    "o": {"a": 0, "k": 100},
    "r": 1,
}
foreground_stroke = {
    "ty": "st",
    "nm": "Foreground {Color:var(Foreground)}",
    "c": {"a": 0, "k": [0, 0, 0, 1]},
    "o": {"a": 0, "k": 100},
    "w": {"a": 0, "k": 1.75},
    "lc": 2,
    "lj": 2,
}

lottie = {
    "v": "5.12.2",
    "fr": 60,
    "ip": 0,
    "op": 18,
    "w": 48,
    "h": 48,
    "nm": "Tonarink add ripple icon (Segoe Fluent Icons)",
    "ddd": 0,
    "assets": [],
    "meta": {
        "font": str(FONT_PATH),
        "codepoint": f"{CODEPOINT:04X}",
        "glyph": glyph_name,
    },
    "layers": [
        shape_layer(
            1,
            "Add U+E710",
            [{"ty": "sh", "nm": "Add contour", "ks": {"a": 0, "k": plus_contour}}, foreground_fill],
            plus_scale,
            {"a": 0, "k": 100},
        ),
        shape_layer(
            2,
            "Center ripple",
            [
                {"ty": "el", "nm": "Ripple ellipse", "p": {"a": 0, "k": [24, 24]}, "s": {"a": 0, "k": [34, 34]}},
                foreground_stroke,
            ],
            ripple_scale,
            ripple_opacity,
        ),
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

from __future__ import annotations

import json
import sys
from pathlib import Path

from fontTools.pens.qu2cuPen import Qu2CuPen
from fontTools.pens.recordingPen import RecordingPen
from fontTools.ttLib import TTFont


ROOT = Path(__file__).resolve().parents[1]
FONT_PATH = Path(r"C:\Windows\Fonts\SegoeIcons.ttf")
OUTPUT_PATH = ROOT / "src" / "Tonarink.App" / "Assets" / "Lottie" / "SelectAllToggleIcon.json"
CODEPOINT = 0xE8B3
FRAME_RATE = 60
SEGMENT_FRAMES = 10
OUT_FRAME = 41


def vector(start: list[float], end: list[float]) -> list[float]:
    return [round(end[0] - start[0], 4), round(end[1] - start[1], 4)]


def extract_contours() -> tuple[str, list[dict[str, object]]]:
    font = TTFont(FONT_PATH)
    glyph_set = font.getGlyphSet()
    glyph_name = font.getBestCmap()[CODEPOINT]
    recording = RecordingPen()
    glyph_set[glyph_name].draw(Qu2CuPen(recording, 1.0, all_cubic=True))

    raw_points = [
        point
        for operation, args in recording.value
        if operation in {"moveTo", "lineTo", "curveTo"}
        for point in args
    ]
    x_min = min(point[0] for point in raw_points)
    x_max = max(point[0] for point in raw_points)
    y_min = min(point[1] for point in raw_points)
    y_max = max(point[1] for point in raw_points)
    scale = min(40 / (x_max - x_min), 40 / (y_max - y_min))
    x_offset = 24 - (x_min + x_max) * scale / 2
    y_offset = 24 + (y_min + y_max) * scale / 2

    def point(value: tuple[float, float]) -> list[float]:
        x, y = value
        return [round(x_offset + x * scale, 4), round(y_offset - y * scale, 4)]

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


def bounds(contour: dict[str, object]) -> tuple[float, float, float, float]:
    vertices = contour["v"]
    xs = [point[0] for point in vertices]
    ys = [point[1] for point in vertices]
    return min(xs), min(ys), max(xs), max(ys)


def center(contour: dict[str, object]) -> tuple[float, float]:
    left, top, right, bottom = bounds(contour)
    return (left + right) / 2, (top + bottom) / 2


def keyframe(frame: int, start: list[float], end: list[float] | None = None) -> dict[str, object]:
    result: dict[str, object] = {"t": frame, "s": start}
    if end is not None:
        result["e"] = end
        result["o"] = {"x": [0.333] * len(start), "y": [0.333] * len(start)}
        result["i"] = {"x": [0.667] * len(start), "y": [0.667] * len(start)}
    return result


def animated_values(values: list[list[float]]) -> dict[str, object]:
    return {
        "a": 1,
        "k": [
            keyframe(frame, value, values[frame + 1] if frame + 1 < len(values) else None)
            for frame, value in enumerate(values)
        ],
    }


def smoothstep(value: float) -> float:
    value = max(0.0, min(1.0, value))
    return value * value * (3 - 2 * value)


def transition_value(
    local_frame: int,
    from_filled: bool,
    to_filled: bool,
) -> float:
    if not from_filled and not to_filled:
        return 0

    if from_filled and to_filled:
        opacity = 100
    elif to_filled:
        opacity = 100 * smoothstep(local_frame / 2)
    else:
        opacity = 100 * (1 - smoothstep((local_frame - 4) / 2))
    return round(opacity, 4)


def animation_values(square_name: str) -> tuple[list[list[float]], list[list[float]]]:
    state_sequence = [
        set(),
        {"top-right", "bottom-left", "bottom-right"},
        {"top-left", "top-right", "bottom-left", "bottom-right"},
        {"top-right", "bottom-left", "bottom-right"},
        set(),
    ]
    offsets = {
        "bottom-right": 0,
        "bottom-left": 1,
        "top-right": 2,
        "top-left": 3,
    }
    scales: list[list[float]] = []
    opacities: list[list[float]] = []
    for frame in range(OUT_FRAME):
        if frame == OUT_FRAME - 1:
            scales.append([100, 100, 100])
            opacities.append([0])
            continue

        segment = min(frame // SEGMENT_FRAMES, 3)
        segment_frame = frame - segment * SEGMENT_FRAMES - offsets[square_name]
        from_filled = square_name in state_sequence[segment]
        to_filled = square_name in state_sequence[segment + 1]
        if segment_frame <= 0 or segment_frame >= 6:
            scale = 100
        elif segment_frame <= 3:
            scale = 100 + 12 * smoothstep(segment_frame / 3)
        else:
            scale = 112 - 12 * smoothstep((segment_frame - 3) / 3)
        opacity = transition_value(segment_frame, from_filled, to_filled)
        scales.append([scale, scale, 100])
        opacities.append([opacity])
    return scales, opacities


def foreground_fill() -> dict[str, object]:
    return {
        "ty": "fl",
        "nm": "Foreground {Color:var(Foreground)}",
        "c": {"a": 0, "k": [0, 0, 0, 1]},
        "o": {"a": 0, "k": 100},
        "r": 1,
    }


def group_transform(scale: float = 100) -> dict[str, object]:
    return {
        "ty": "tr",
        "p": {"a": 0, "k": [0, 0]},
        "a": {"a": 0, "k": [0, 0]},
        "s": {"a": 0, "k": [scale, scale]},
        "r": {"a": 0, "k": 0},
        "o": {"a": 0, "k": 100},
        "sk": {"a": 0, "k": 0},
        "sa": {"a": 0, "k": 0},
    }


def contour_layer(
    index: int,
    name: str,
    contours: list[dict[str, object]],
    anchor: tuple[float, float],
    scales: list[list[float]],
    opacities: list[list[float]] | None = None,
    content_scale: float = 100,
) -> dict[str, object]:
    anchor_x, anchor_y = anchor
    return {
        "ddd": 0,
        "ind": index,
        "ty": 4,
        "nm": name,
        "sr": 1,
        "ks": {
            "o": animated_values(opacities) if opacities is not None else {"a": 0, "k": 100},
            "r": {"a": 0, "k": 0},
            "p": {"a": 0, "k": [anchor_x, anchor_y, 0]},
            "a": {"a": 0, "k": [anchor_x, anchor_y, 0]},
            "s": animated_values(scales),
        },
        "ao": 0,
        "shapes": [{
            "ty": "gr",
            "nm": name,
            "it": [
                *[
                    {"ty": "sh", "nm": f"Contour {index + 1}", "ks": {"a": 0, "k": contour}}
                    for index, contour in enumerate(contours)
                ],
                foreground_fill(),
                group_transform(content_scale),
            ],
        }],
        "ip": 0,
        "op": OUT_FRAME,
        "st": 0,
        "bm": 0,
    }


def combined_center(contours: list[dict[str, object]]) -> tuple[float, float]:
    contour_bounds = [bounds(contour) for contour in contours]
    return (
        (min(item[0] for item in contour_bounds) + max(item[2] for item in contour_bounds)) / 2,
        (min(item[1] for item in contour_bounds) + max(item[3] for item in contour_bounds)) / 2,
    )


def quadrant_name(contour: dict[str, object]) -> str:
    center_x, center_y = center(contour)
    vertical = "top" if center_y < 24 else "bottom"
    horizontal = "left" if center_x < 24 else "right"
    return f"{vertical}-{horizontal}"


glyph_name, contours = extract_contours()
hole_candidates = []
square_outline_candidates = []
for contour in contours:
    left, top, right, bottom = bounds(contour)
    center_x, center_y = center(contour)
    width = right - left
    height = bottom - top
    if 10 < center_x < 38 and 10 < center_y < 38 and 5 < width < 12 and 5 < height < 12:
        hole_candidates.append(contour)
    elif 10 < center_x < 38 and 10 < center_y < 38 and 12 <= width < 14 and 12 <= height < 14:
        square_outline_candidates.append(contour)

if len(hole_candidates) != 4 or len(square_outline_candidates) != 4:
    diagnostic = [(bounds(contour), center(contour)) for contour in contours]
    raise ValueError(
        "Expected four square outlines and four inner contours, "
        f"got {len(square_outline_candidates)} and {len(hole_candidates)}: {diagnostic}"
    )

named_holes: dict[str, dict[str, object]] = {}
named_outlines: dict[str, dict[str, object]] = {}
for contour in hole_candidates:
    named_holes[quadrant_name(contour)] = contour
for contour in square_outline_candidates:
    named_outlines[quadrant_name(contour)] = contour

square_contour_ids = {
    *[id(contour) for contour in hole_candidates],
    *[id(contour) for contour in square_outline_candidates],
}
border_quadrants: dict[str, list[dict[str, object]]] = {
    "top-left": [],
    "top-right": [],
    "bottom-left": [],
    "bottom-right": [],
}
for contour in contours:
    if id(contour) not in square_contour_ids:
        border_quadrants[quadrant_name(contour)].append(contour)

square_names = ["bottom-right", "bottom-left", "top-right", "top-left"]
layers: list[dict[str, object]] = []
for index, name in enumerate(square_names, start=1):
    scales, opacities = animation_values(name)
    fill_scales = [
        [round(scale[0] * 1.05, 4), round(scale[1] * 1.05, 4), scale[2]]
        for scale in scales
    ]
    layers.append(contour_layer(
        index,
        f"{name} fill ripple",
        [named_holes[name]],
        center(named_holes[name]),
        fill_scales,
        opacities,
    ))
for index, name in enumerate(square_names, start=5):
    scales, _ = animation_values(name)
    layers.append(contour_layer(
        index,
        f"{name} square ripple",
        [named_outlines[name], named_holes[name]],
        center(named_outlines[name]),
        scales,
    ))
for index, name in enumerate(square_names, start=9):
    scales, _ = animation_values(name)
    quadrant_contours = border_quadrants[name]
    layers.append(contour_layer(
        index,
        f"{name} outer border ripple",
        quadrant_contours,
        combined_center(quadrant_contours),
        scales,
    ))

lottie = {
    "v": "5.12.2",
    "fr": FRAME_RATE,
    "ip": 0,
    "op": OUT_FRAME,
    "w": 48,
    "h": 48,
    "nm": "Tonarink select-all tri-state ripple (Segoe Fluent Icons)",
    "ddd": 0,
    "assets": [],
    "meta": {
        "font": str(FONT_PATH),
        "codepoint": f"{CODEPOINT:04X}",
        "glyph": glyph_name,
        "states": {"unchecked": 0, "indeterminate": 10, "checked": 20},
    },
    "layers": layers,
    "markers": [],
}

serialized = json.dumps(lottie, ensure_ascii=False, separators=(",", ":")) + "\n"
OUTPUT_PATH.write_text(serialized, encoding="utf-8")
print(OUTPUT_PATH)
if len(sys.argv) > 1:
    preview_path = Path(sys.argv[1]).resolve()
    preview_path.parent.mkdir(parents=True, exist_ok=True)
    preview_path.write_text(serialized, encoding="utf-8")
    print(preview_path)

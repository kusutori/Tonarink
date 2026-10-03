from __future__ import annotations

import json
import sys
from pathlib import Path

from fontTools.pens.qu2cuPen import Qu2CuPen
from fontTools.pens.recordingPen import RecordingPen
from fontTools.ttLib import TTFont


ROOT = Path(__file__).resolve().parents[1]
FONT_PATH = Path(r"C:\Windows\Fonts\SegoeIcons.ttf")
OUTPUT_DIRECTORY = ROOT / "src" / "Tonarink.App" / "Assets" / "Lottie"
ICONS = {
    "UndoFlowIcon": 0xE7A7,
    "RedoFlowIcon": 0xE7A6,
}


def vector(start: list[float], end: list[float]) -> list[float]:
    return [round(end[0] - start[0], 4), round(end[1] - start[1], 4)]


def extract_contours(font: TTFont, codepoint: int) -> tuple[str, list[dict[str, object]]]:
    glyph_set = font.getGlyphSet()
    glyph_name = font.getBestCmap()[codepoint]
    recording = RecordingPen()
    glyph_set[glyph_name].draw(Qu2CuPen(recording, 1.0, all_cubic=True))

    points = [
        point
        for operation, args in recording.value
        if operation in {"moveTo", "lineTo", "curveTo"}
        for point in args
    ]
    x_min = min(point[0] for point in points)
    x_max = max(point[0] for point in points)
    y_min = min(point[1] for point in points)
    y_max = max(point[1] for point in points)
    scale = min(36 / (x_max - x_min), 36 / (y_max - y_min))
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


def transform(opacity: dict[str, object] | int = 100) -> dict[str, object]:
    return {
        "o": opacity if isinstance(opacity, dict) else {"a": 0, "k": opacity},
        "r": {"a": 0, "k": 0},
        "p": {"a": 0, "k": [0, 0, 0]},
        "a": {"a": 0, "k": [0, 0, 0]},
        "s": {"a": 0, "k": [100, 100, 100]},
    }


def shape_layer(
    index: int,
    name: str,
    shapes: list[dict[str, object]],
    opacity: dict[str, object] | int = 100,
) -> dict[str, object]:
    return {
        "ddd": 0,
        "ind": index,
        "ty": 4,
        "nm": name,
        "sr": 1,
        "ks": transform(opacity),
        "ao": 0,
        "shapes": shapes,
        "ip": 0,
        "op": 32,
        "st": 0,
        "bm": 0,
    }


def foreground_fill() -> dict[str, object]:
    return {
        "ty": "fl",
        "nm": "Foreground {Color:var(Foreground)}",
        "c": {"a": 0, "k": [0, 0, 0, 1]},
        "o": {"a": 0, "k": 100},
        "r": 1,
    }


def add(left: list[float], right: list[float]) -> list[float]:
    return [left[0] + right[0], left[1] + right[1]]


def midpoint(left: list[float], right: list[float]) -> list[float]:
    return [(left[0] + right[0]) / 2, (left[1] + right[1]) / 2]


def cubic(
    start: list[float],
    control1: list[float],
    control2: list[float],
    end: list[float],
    progress: float,
) -> list[float]:
    inverse = 1 - progress
    return [
        inverse**3 * start[axis]
        + 3 * inverse**2 * progress * control1[axis]
        + 3 * inverse * progress**2 * control2[axis]
        + progress**3 * end[axis]
        for axis in range(2)
    ]


def exact_centerline(
    contour: dict[str, object],
    codepoint: int,
) -> list[list[float]]:
    vertices = contour["v"]
    incoming = contour["i"]
    outgoing = contour["o"]

    def controls(start: int, end: int, forward: bool) -> tuple[list[float], list[float]]:
        if forward:
            return add(vertices[start], outgoing[start]), add(vertices[end], incoming[end])
        return add(vertices[start], incoming[start]), add(vertices[end], outgoing[end])

    if codepoint == 0xE7A7:
        # Each tuple pairs the two actual outline edges around the same shaft
        # segment. The final duplicate vertex (22) retains the closing curve's
        # incoming handle, unlike vertex 0 at the same coordinate.
        segments = [
            (2, 1, False, 5, 6, True),
            (1, 0, False, 6, 7, True),
            (22, 21, False, 7, 8, True),
            (21, 20, False, 8, 9, True),
            (20, 19, False, 9, 10, True),
        ]
        tip = [vertices[17][0], vertices[12][1]]
    else:
        segments = [
            (11, 10, False, 13, 14, True),
            (10, 9, False, 14, 15, True),
            (9, 8, False, 15, 16, True),
            (8, 7, False, 16, 17, True),
            (7, 6, False, 17, 18, True),
        ]
        tip = [vertices[20][0], vertices[4][1]]

    points: list[list[float]] = []
    for edge1_start, edge1_end, edge1_forward, edge2_start, edge2_end, edge2_forward in segments:
        start = midpoint(vertices[edge1_start], vertices[edge2_start])
        end = midpoint(vertices[edge1_end], vertices[edge2_end])
        edge1_control1, edge1_control2 = controls(edge1_start, edge1_end, edge1_forward)
        edge2_control1, edge2_control2 = controls(edge2_start, edge2_end, edge2_forward)
        control1 = midpoint(edge1_control1, edge2_control1)
        control2 = midpoint(edge1_control2, edge2_control2)
        first_step = 0 if not points else 1
        points.extend(
            cubic(start, control1, control2, end, step / 24)
            for step in range(first_step, 25)
        )

    end = points[-1]
    points.extend(
        [
            end[0] + (tip[0] - end[0]) * step / 8,
            end[1] + (tip[1] - end[1]) * step / 8,
        ]
        for step in range(1, 9)
    )
    return points


def sample_centerline(
    contour: dict[str, object],
    codepoint: int,
    count: int,
) -> list[list[float]]:
    points = exact_centerline(contour, codepoint)
    distances = [0.0]
    for previous, current in zip(points, points[1:]):
        distances.append(
            distances[-1]
            + ((current[0] - previous[0]) ** 2 + (current[1] - previous[1]) ** 2) ** 0.5
        )

    sampled: list[list[float]] = []
    cursor = 0
    for index in range(count):
        linear_progress = index / (count - 1)
        # Smooth acceleration and deceleration while retaining exact endpoints.
        progress = linear_progress * linear_progress * (3 - 2 * linear_progress)
        target = distances[-1] * progress
        while cursor + 1 < len(distances) and distances[cursor + 1] < target:
            cursor += 1
        if cursor + 1 == len(distances):
            sampled.append(points[-1])
            continue

        span = distances[cursor + 1] - distances[cursor]
        local = 0 if span == 0 else (target - distances[cursor]) / span
        sampled.append([
            points[cursor][0] + (points[cursor + 1][0] - points[cursor][0]) * local,
            points[cursor][1] + (points[cursor + 1][1] - points[cursor][1]) * local,
        ])
    return [[round(x, 4), round(y, 4), 0] for x, y in sampled]


def square_layer(
    index: int,
    contour: dict[str, object],
    codepoint: int,
) -> dict[str, object]:
    positions = sample_centerline(contour, codepoint, 20)
    position_keyframes: list[dict[str, object]] = [keyframe(0, positions[0], positions[0])]
    for offset, position in enumerate(positions):
        frame = 3 + offset
        next_position = positions[min(offset + 1, len(positions) - 1)]
        position_keyframes.append(keyframe(frame, position, next_position))

    opacity = animated([
        keyframe(0, [0], [0]),
        keyframe(2, [0], [100]),
        keyframe(4, [100], [100]),
        keyframe(21, [100], [0]),
        keyframe(24, [0]),
    ])
    return {
        "ddd": 0,
        "ind": index,
        "ty": 4,
        "nm": "Tail-to-head square",
        "sr": 1,
        "ks": {
            **transform(opacity),
            "p": animated(position_keyframes),
        },
        "ao": 0,
        "shapes": [
            {
                "ty": "gr",
                "nm": "Traveling square",
                "it": [
                    {
                        "ty": "rc",
                        "nm": "Square",
                        "p": {"a": 0, "k": [0, 0]},
                        "s": {"a": 0, "k": [3.6, 3.6]},
                        "r": {"a": 0, "k": 0},
                    },
                    foreground_fill(),
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
        "op": 32,
        "st": 0,
        "bm": 0,
    }


def build_lottie(font: TTFont, name: str, codepoint: int) -> dict[str, object]:
    glyph_name, contours = extract_contours(font, codepoint)
    ease_out = (0.18, 0.82, 0.28, 1.0)
    base_opacity = animated([
        keyframe(0, [100], [100]),
        keyframe(3, [100], [42], ease_out),
        keyframe(6, [42], [42]),
        keyframe(21, [42], [100], ease_out),
        keyframe(27, [100]),
    ])

    base_shapes: list[dict[str, object]] = [
        {
            "ty": "gr",
            "nm": f"Segoe Fluent Icons U+{codepoint:04X}",
            "it": [
                *[
                    {"ty": "sh", "nm": f"Contour {index + 1}", "ks": {"a": 0, "k": contour}}
                    for index, contour in enumerate(contours)
                ],
                foreground_fill(),
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
    ]
    return {
        "v": "5.12.2",
        "fr": 60,
        "ip": 0,
        "op": 32,
        "w": 48,
        "h": 48,
        "nm": f"Tonarink {name} (Segoe Fluent Icons)",
        "ddd": 0,
        "assets": [],
        "meta": {
            "font": str(FONT_PATH),
            "codepoint": f"{codepoint:04X}",
            "glyph": glyph_name,
        },
        "layers": [
            square_layer(1, contours[0], codepoint),
            shape_layer(2, "Original icon", base_shapes, base_opacity),
        ],
        "markers": [],
    }


font = TTFont(FONT_PATH)
for icon_name, icon_codepoint in ICONS.items():
    payload = json.dumps(
        build_lottie(font, icon_name, icon_codepoint),
        ensure_ascii=False,
        separators=(",", ":"),
    ) + "\n"
    output_path = OUTPUT_DIRECTORY / f"{icon_name}.json"
    output_path.write_text(payload, encoding="utf-8")
    print(output_path)

    if len(sys.argv) > 1:
        preview_directory = Path(sys.argv[1]).resolve()
        preview_directory.mkdir(parents=True, exist_ok=True)
        preview_path = preview_directory / f"{icon_name}.json"
        preview_path.write_text(payload, encoding="utf-8")
        print(preview_path)

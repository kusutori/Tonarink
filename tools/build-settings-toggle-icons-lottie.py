from __future__ import annotations

import json
import math
import sys
from copy import deepcopy
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.qu2cuPen import Qu2CuPen
from fontTools.pens.recordingPen import RecordingPen
from fontTools.ttLib import TTFont


ROOT = Path(__file__).resolve().parents[1]
FONT_PATH = Path(r"C:\Windows\Fonts\SegoeIcons.ttf")
OUTPUT_DIRECTORY = ROOT / "src" / "Tonarink.App" / "Assets" / "Lottie"
CANVAS_SIZE = 48
GLYPH_SIZE = 46
FRAME_RATE = 60
MOTION_PEAK_FRAME = 16
MOTION_END_FRAME = 32
OUT_FRAME = 40


def vector(start: list[float], end: list[float]) -> list[float]:
    return [round(end[0] - start[0], 4), round(end[1] - start[1], 4)]


def extract_glyph(codepoint: int) -> tuple[str, list[dict[str, object]], tuple[float, float, float, float]]:
    font = TTFont(FONT_PATH)
    glyph_set = font.getGlyphSet()
    glyph_name = font.getBestCmap()[codepoint]

    bounds_pen = BoundsPen(glyph_set)
    glyph_set[glyph_name].draw(bounds_pen)
    if bounds_pen.bounds is None:
        raise ValueError(f"U+{codepoint:04X} has no bounds")
    bounds = bounds_pen.bounds
    x_min, y_min, x_max, y_max = bounds
    scale = GLYPH_SIZE / max(x_max - x_min, y_max - y_min)
    left = (CANVAS_SIZE - (x_max - x_min) * scale) / 2
    top = (CANVAS_SIZE - (y_max - y_min) * scale) / 2

    def point(value: tuple[float, float]) -> list[float]:
        x, y = value
        return [round(left + (x - x_min) * scale, 4), round(top + (y_max - y) * scale, 4)]

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
    return glyph_name, contours, bounds


def normalized_point(
    value: tuple[float, float],
    bounds: tuple[float, float, float, float],
) -> list[float]:
    x_min, y_min, x_max, y_max = bounds
    scale = GLYPH_SIZE / max(x_max - x_min, y_max - y_min)
    left = (CANVAS_SIZE - (x_max - x_min) * scale) / 2
    top = (CANVAS_SIZE - (y_max - y_min) * scale) / 2
    x, y = value
    return [round(left + (x - x_min) * scale, 4), round(top + (y_max - y) * scale, 4)]


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


def foreground_fill() -> dict[str, object]:
    return {
        "ty": "fl",
        "nm": "Foreground {Color:var(Foreground)}",
        "c": {"a": 0, "k": [0, 0, 0, 1]},
        "o": {"a": 0, "k": 100},
        "r": 1,
    }


def rotate_contour(
    contour: dict[str, object],
    pivot: list[float],
    degrees: float | dict[int, float],
) -> dict[str, object]:
    result = deepcopy(contour)

    def rotate(point: list[float], angle: float) -> list[float]:
        radians = math.radians(angle)
        cosine = math.cos(radians)
        sine = math.sin(radians)
        dx = point[0] - pivot[0]
        dy = point[1] - pivot[1]
        return [
            round(pivot[0] + cosine * dx - sine * dy, 4),
            round(pivot[1] + sine * dx + cosine * dy, 4),
        ]

    for index in range(len(result["v"])):
        angle = degrees.get(index, 0) if isinstance(degrees, dict) else degrees
        if angle == 0:
            continue
        vertex = result["v"][index]
        incoming_control = [
            vertex[0] + result["i"][index][0],
            vertex[1] + result["i"][index][1],
        ]
        outgoing_control = [
            vertex[0] + result["o"][index][0],
            vertex[1] + result["o"][index][1],
        ]
        rotated_vertex = rotate(vertex, angle)
        rotated_incoming = rotate(incoming_control, angle)
        rotated_outgoing = rotate(outgoing_control, angle)
        result["v"][index] = rotated_vertex
        result["i"][index] = vector(rotated_vertex, rotated_incoming)
        result["o"][index] = vector(rotated_vertex, rotated_outgoing)

    return result


def animated_contour(
    start: dict[str, object],
    moved: dict[str, object],
) -> dict[str, object]:
    return animated([
        keyframe(0, [start], [moved], travel),
        keyframe(MOTION_PEAK_FRAME, [moved], [start], settle),
        keyframe(MOTION_END_FRAME, [start]),
    ])


def layer(
    index: int,
    name: str,
    contours: list[dict[str, object]],
    anchor: list[float],
    rotation: dict[str, object] | float = 0,
    masks: list[dict[str, object]] | None = None,
    opacity: dict[str, object] | float = 100,
) -> dict[str, object]:
    rotation_property = rotation if isinstance(rotation, dict) else {"a": 0, "k": rotation}
    opacity_property = opacity if isinstance(opacity, dict) else {"a": 0, "k": opacity}
    paths = []
    for contour_index, contour in enumerate(contours):
        contour_property = contour if "a" in contour else {"a": 0, "k": contour}
        paths.append({
            "ty": "sh",
            "nm": f"{name} contour {contour_index + 1}",
            "ks": contour_property,
        })
    return {
        "ddd": 0,
        "ind": index,
        "ty": 4,
        "nm": name,
        "sr": 1,
        "ks": {
            "o": opacity_property,
            "r": rotation_property,
            "p": {"a": 0, "k": [*anchor, 0]},
            "a": {"a": 0, "k": [*anchor, 0]},
            "s": {"a": 0, "k": [100, 100, 100]},
        },
        "ao": 0,
        "hasMask": bool(masks),
        "masksProperties": masks or [],
        "shapes": [*paths, foreground_fill()],
        "ip": 0,
        "op": OUT_FRAME,
        "st": 0,
        "bm": 0,
    }


def document(name: str, codepoint: int, glyph_name: str, layers: list[dict[str, object]]) -> dict[str, object]:
    return {
        "v": "5.12.2",
        "fr": FRAME_RATE,
        "ip": 0,
        "op": OUT_FRAME,
        "w": CANVAS_SIZE,
        "h": CANVAS_SIZE,
        "nm": name,
        "ddd": 0,
        "assets": [],
        "meta": {
            "font": str(FONT_PATH),
            "codepoint": f"{codepoint:04X}",
            "glyph": glyph_name,
        },
        "layers": layers,
        "markers": [
            {"tm": 0, "cm": "Off", "dr": 0},
            {"tm": MOTION_END_FRAME, "cm": "On", "dr": 0},
            {"tm": 0, "cm": "OffToOn_Start", "dr": 0},
            {"tm": MOTION_END_FRAME, "cm": "OffToOn_End", "dr": 0},
            {"tm": 0, "cm": "OnToOff_Start", "dr": 0},
            {"tm": MOTION_END_FRAME, "cm": "OnToOff_End", "dr": 0},
        ],
    }


travel = (1.00, 0.49, 0.00, 0.55)
settle = (0.00, 0.65, 0.51, 0.99)

history_name, history_contours, history_bounds = extract_glyph(0xE81C)
if len(history_contours) != 2:
    raise ValueError(f"Expected two contours for U+E81C, got {len(history_contours)}")
history_center = normalized_point((1024, 1024), history_bounds)
history_rotation_outer = animated([
    keyframe(0, [0], [-360], travel),
    keyframe(MOTION_END_FRAME, [-360]),
])
history_rotation_inner = animated([
    keyframe(0, [0], [360], travel),
    keyframe(MOTION_END_FRAME, [360]),
])
history = document(
    "Tonarink settings history toggle icon (Segoe Fluent Icons)",
    0xE81C,
    history_name,
    [
        layer(1, "History outer arrow", [history_contours[0]], history_center, history_rotation_outer),
        layer(2, "History clock hand", [history_contours[1]], history_center, history_rotation_inner),
    ],
)

startup_name, startup_contours, startup_bounds = extract_glyph(0xEC4A)
if len(startup_contours) != 4:
    raise ValueError(f"Expected four contours for U+EC4A, got {len(startup_contours)}")
startup_pivot = normalized_point((992, 608), startup_bounds)
startup_rotation = animated([
    keyframe(0, [0], [-38], travel),
    keyframe(MOTION_PEAK_FRAME, [-38], [0], settle),
    keyframe(MOTION_END_FRAME, [0]),
])
def circle_through(
    first: list[float],
    second: list[float],
    third: list[float],
) -> list[float]:
    x1, y1 = first
    x2, y2 = second
    x3, y3 = third
    denominator = 2 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2))
    if abs(denominator) < 1e-6:
        raise ValueError("Gauge trajectory points are collinear")
    first_squared = x1 * x1 + y1 * y1
    second_squared = x2 * x2 + y2 * y2
    third_squared = x3 * x3 + y3 * y3
    return [
        (first_squared * (y2 - y3) + second_squared * (y3 - y1) + third_squared * (y1 - y2)) / denominator,
        (first_squared * (x3 - x2) + second_squared * (x1 - x3) + third_squared * (x2 - x1)) / denominator,
    ]


startup_gauge_center = circle_through(
    startup_contours[0]["v"][8],
    startup_contours[0]["v"][10],
    startup_contours[2]["v"][12],
)


def polar(value: list[float], center: list[float]) -> tuple[float, float]:
    dx = value[0] - center[0]
    dy = value[1] - center[1]
    return math.hypot(dx, dy), math.degrees(math.atan2(dy, dx))


def annular_sector(
    center: list[float],
    inner_radius: float,
    outer_radius: float,
    start_angle: float,
    end_angle: float,
) -> dict[str, object]:
    def point(radius: float, degrees: float) -> list[float]:
        radians = math.radians(degrees)
        return [
            round(center[0] + math.cos(radians) * radius, 4),
            round(center[1] + math.sin(radians) * radius, 4),
        ]

    def arc_handles(radius: float, start: float, end: float) -> tuple[list[float], list[float]]:
        start_radians = math.radians(start)
        end_radians = math.radians(end)
        factor = 4 / 3 * math.tan(math.radians(end - start) / 4) * radius
        return (
            [round(-math.sin(start_radians) * factor, 4), round(math.cos(start_radians) * factor, 4)],
            [round(math.sin(end_radians) * factor, 4), round(-math.cos(end_radians) * factor, 4)],
        )

    outer_out, outer_in = arc_handles(outer_radius, start_angle, end_angle)
    inner_out, inner_in = arc_handles(inner_radius, end_angle, start_angle)
    vertices = [
        point(outer_radius, start_angle),
        point(outer_radius, end_angle),
        point(inner_radius, end_angle),
        point(inner_radius, start_angle),
    ]
    return {
        "i": [[0, 0], outer_in, [0, 0], inner_in],
        "o": [outer_out, [0, 0], inner_out, [0, 0]],
        "v": vertices,
        "c": True,
    }


left_outer_radius, left_outer_angle = polar(startup_contours[0]["v"][11], startup_gauge_center)
left_inner_radius, left_inner_angle = polar(startup_contours[0]["v"][12], startup_gauge_center)
right_inner_radius, right_inner_angle = polar(startup_contours[2]["v"][9], startup_gauge_center)
right_outer_radius, right_outer_angle = polar(startup_contours[2]["v"][10], startup_gauge_center)
gap_inner_radius = (left_inner_radius + right_inner_radius) / 2
gap_outer_radius = (left_outer_radius + right_outer_radius) / 2
gap_start_angle = (left_outer_angle + left_inner_angle) / 2
gap_end_angle = (right_outer_angle + right_inner_angle) / 2
startup_gap_filler = annular_sector(
    startup_gauge_center,
    min(left_inner_radius, right_inner_radius) - 0.08,
    max(left_outer_radius, right_outer_radius) + 0.08,
    gap_start_angle - 1.5,
    gap_end_angle + 1.5,
)
startup_gap_mask_shape = annular_sector(
    startup_gauge_center,
    min(left_inner_radius, right_inner_radius) - 2,
    max(left_outer_radius, right_outer_radius) + 2,
    gap_start_angle,
    gap_end_angle,
)
startup_gap_mask = {
    "inv": False,
    "mode": "s",
    "pt": animated_contour(
        startup_gap_mask_shape,
        rotate_contour(startup_gap_mask_shape, startup_gauge_center, -38),
    ),
    "o": {"a": 0, "k": 100},
    "x": {"a": 0, "k": 0},
    "nm": "Moving gauge gap",
}
# Switch between the untouched font contour and the animated masked contour in
# one frame. Cross-fading identical black geometry makes it look grey and also
# exposes both antialiased mask edges at once.
startup_gap_fill_opacity = animated([
    {"t": 0, "s": [0], "h": 1},
    {"t": 1, "s": [100], "h": 1},
    {"t": MOTION_END_FRAME, "s": [0]},
])
startup_rest_gauge_opacity = animated([
    {"t": 0, "s": [100], "h": 1},
    {"t": 1, "s": [0], "h": 1},
    {"t": MOTION_END_FRAME, "s": [100]},
])
startup = document(
    "Tonarink settings startup toggle icon (Segoe Fluent Icons)",
    0xEC4A,
    startup_name,
    [
        layer(2, "Startup needle", [startup_contours[1], startup_contours[3]], startup_pivot, startup_rotation),
        layer(
            3,
            "Startup gauge gap fill",
            [startup_gap_filler],
            startup_pivot,
            masks=[deepcopy(startup_gap_mask)],
            opacity=startup_gap_fill_opacity,
        ),
        layer(
            1,
            "Startup gauge",
            [startup_contours[0], startup_contours[2]],
            startup_pivot,
            masks=[deepcopy(startup_gap_mask)],
            opacity=startup_gap_fill_opacity,
        ),
        layer(
            4,
            "Startup resting gauge",
            [startup_contours[0], startup_contours[2]],
            startup_pivot,
            opacity=startup_rest_gauge_opacity,
        ),
    ],
)

outputs = {
    "SettingsHistoryToggleIcon.json": history,
    "SettingsStartupToggleIcon.json": startup,
}
for filename, value in outputs.items():
    serialized = json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n"
    output_path = OUTPUT_DIRECTORY / filename
    output_path.write_text(serialized, encoding="utf-8")
    print(output_path)

if len(sys.argv) > 1:
    player_root = Path(sys.argv[1]).resolve()
    for filename, value in outputs.items():
        slug = "tonarink-settings-history" if "History" in filename else "tonarink-settings-startup"
        player_path = player_root / "public" / "projects" / slug / "scene-1" / "lottie.json"
        player_path.parent.mkdir(parents=True, exist_ok=True)
        player_path.write_text(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
        print(player_path)

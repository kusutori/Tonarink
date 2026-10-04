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


def extract_glyph(
    codepoint: int,
    glyph_size: float = GLYPH_SIZE,
) -> tuple[str, list[dict[str, object]], tuple[float, float, float, float]]:
    font = TTFont(FONT_PATH)
    glyph_set = font.getGlyphSet()
    glyph_name = font.getBestCmap()[codepoint]

    bounds_pen = BoundsPen(glyph_set)
    glyph_set[glyph_name].draw(bounds_pen)
    if bounds_pen.bounds is None:
        raise ValueError(f"U+{codepoint:04X} has no bounds")
    bounds = bounds_pen.bounds
    x_min, y_min, x_max, y_max = bounds
    scale = glyph_size / max(x_max - x_min, y_max - y_min)
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
    glyph_size: float = GLYPH_SIZE,
) -> list[float]:
    x_min, y_min, x_max, y_max = bounds
    scale = glyph_size / max(x_max - x_min, y_max - y_min)
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


def translate_contour(
    contour: dict[str, object],
    x: float,
    y: float,
) -> dict[str, object]:
    result = deepcopy(contour)
    result["v"] = [
        [round(vertex[0] + x, 4), round(vertex[1] + y, 4)]
        for vertex in result["v"]
    ]
    return result


def contour_polyline(
    contour: dict[str, object],
    samples_per_segment: int = 16,
) -> list[list[float]]:
    points: list[list[float]] = []
    vertices = contour["v"]
    incoming = contour["i"]
    outgoing = contour["o"]

    for index, start in enumerate(vertices):
        next_index = (index + 1) % len(vertices)
        end = vertices[next_index]
        control1 = [start[0] + outgoing[index][0], start[1] + outgoing[index][1]]
        control2 = [end[0] + incoming[next_index][0], end[1] + incoming[next_index][1]]
        for step in range(samples_per_segment):
            t = step / samples_per_segment
            inverse = 1 - t
            points.append([
                inverse**3 * start[0]
                + 3 * inverse**2 * t * control1[0]
                + 3 * inverse * t**2 * control2[0]
                + t**3 * end[0],
                inverse**3 * start[1]
                + 3 * inverse**2 * t * control1[1]
                + 3 * inverse * t**2 * control2[1]
                + t**3 * end[1],
            ])
    return points


def resample_closed_polyline(points: list[list[float]], count: int) -> list[list[float]]:
    lengths: list[float] = []
    total = 0.0
    for index, point in enumerate(points):
        next_point = points[(index + 1) % len(points)]
        length = math.dist(point, next_point)
        lengths.append(length)
        total += length

    result: list[list[float]] = []
    segment = 0
    traversed = 0.0
    for sample_index in range(count):
        target = total * sample_index / count
        while traversed + lengths[segment] < target:
            traversed += lengths[segment]
            segment = (segment + 1) % len(points)
        start = points[segment]
        end = points[(segment + 1) % len(points)]
        distance = target - traversed
        ratio = 0 if lengths[segment] == 0 else distance / lengths[segment]
        result.append([
            round(start[0] + (end[0] - start[0]) * ratio, 4),
            round(start[1] + (end[1] - start[1]) * ratio, 4),
        ])
    return result


def right_side_run(points: list[list[float]], cut_x: float) -> list[list[float]]:
    runs: list[list[list[float]]] = []
    current: list[list[float]] = []
    for index, start in enumerate(points):
        end = points[(index + 1) % len(points)]
        start_inside = start[0] >= cut_x
        end_inside = end[0] >= cut_x
        if start_inside and not current:
            current = [start]
        elif start_inside:
            current.append(start)

        if start_inside != end_inside:
            ratio = (cut_x - start[0]) / (end[0] - start[0])
            intersection = [cut_x, start[1] + (end[1] - start[1]) * ratio]
            if start_inside:
                current.append(intersection)
                runs.append(current)
                current = []
            else:
                current = [intersection]

    if current:
        if runs and points[0][0] >= cut_x:
            runs[0] = current + runs[0]
        else:
            runs.append(current)
    return max(runs, key=len)


def aligned_contours(
    start: dict[str, object],
    target_points: list[list[float]],
    vertex_count: int = 96,
) -> tuple[dict[str, object], dict[str, object]]:
    start_points = resample_closed_polyline(contour_polyline(start), vertex_count)
    target = resample_closed_polyline(target_points, vertex_count)
    candidates = [target, list(reversed(target))]
    best: list[list[float]] | None = None
    best_score = math.inf
    for candidate in candidates:
        for shift in range(vertex_count):
            shifted = candidate[shift:] + candidate[:shift]
            score = sum(
                (source[0] - destination[0]) ** 2 + (source[1] - destination[1]) ** 2
                for source, destination in zip(start_points, shifted)
            )
            if score < best_score:
                best_score = score
                best = shifted

    def contour(points: list[list[float]]) -> dict[str, object]:
        return {
            "i": [[0, 0] for _ in points],
            "o": [[0, 0] for _ in points],
            "v": points,
            "c": True,
        }

    if best is None:
        raise ValueError("Unable to align contact body contours")
    return contour(start_points), contour(best)


def rectangle_contour(left: float, top: float, right: float, bottom: float) -> dict[str, object]:
    return {
        "i": [[0, 0], [0, 0], [0, 0], [0, 0]],
        "o": [[0, 0], [0, 0], [0, 0], [0, 0]],
        "v": [[left, top], [right, top], [right, bottom], [left, bottom]],
        "c": True,
    }


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
    position: dict[str, object] | list[float] | None = None,
    scale: dict[str, object] | list[float] | None = None,
) -> dict[str, object]:
    rotation_property = rotation if isinstance(rotation, dict) else {"a": 0, "k": rotation}
    opacity_property = opacity if isinstance(opacity, dict) else {"a": 0, "k": opacity}
    position_property = position if isinstance(position, dict) else {
        "a": 0,
        "k": [*(position or anchor), 0],
    }
    scale_property = scale if isinstance(scale, dict) else {
        "a": 0,
        "k": scale or [100, 100, 100],
    }
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
            "p": position_property,
            "a": {"a": 0, "k": [*anchor, 0]},
            "s": scale_property,
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


def document(
    name: str,
    codepoint: int,
    glyph_name: str,
    layers: list[dict[str, object]],
    markers: list[dict[str, object]] | None = None,
) -> dict[str, object]:
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
        "markers": markers or [
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


def subset_contour(
    contour: dict[str, object],
    indices: list[int],
    reset_incoming: set[int] | None = None,
    reset_outgoing: set[int] | None = None,
) -> dict[str, object]:
    reset_incoming = reset_incoming or set()
    reset_outgoing = reset_outgoing or set()
    return {
        "i": [
            [0, 0] if source_index in reset_incoming else deepcopy(contour["i"][source_index])
            for source_index in indices
        ],
        "o": [
            [0, 0] if source_index in reset_outgoing else deepcopy(contour["o"][source_index])
            for source_index in indices
        ],
        "v": [deepcopy(contour["v"][source_index]) for source_index in indices],
        "c": True,
    }


state_markers = [
    {"tm": 0, "cm": "Off", "dr": 0},
    {"tm": 16, "cm": "On", "dr": 0},
    {"tm": 0, "cm": "OffToOn_Start", "dr": 0},
    {"tm": 16, "cm": "OffToOn_End", "dr": 0},
    {"tm": 20, "cm": "OnToOff_Start", "dr": 0},
    {"tm": 36, "cm": "OnToOff_End", "dr": 0},
]

pin_name, pin_contours, pin_bounds = extract_glyph(0xE72E)
if len(pin_contours) != 4:
    raise ValueError(f"Expected four contours for U+E72E, got {len(pin_contours)}")
pin_center = normalized_point((1024, 1024), pin_bounds)
pin_body_outer = subset_contour(
    pin_contours[0],
    [0, 1, 2, 3, 4, 5, 6, 10, 11],
    reset_incoming={10},
    reset_outgoing={6},
)
pin_shackle_outer = subset_contour(
    pin_contours[0],
    [6, 7, 8, 9, 10],
    reset_incoming={6},
    reset_outgoing={10},
)
pin_right_outer_x = pin_contours[0]["v"][9][0]
pin_right_inner_x = pin_contours[1]["v"][2][0]
pin_body_top_y = pin_contours[0]["v"][10][1]
pin_gap_left = pin_right_inner_x - 0.75
pin_gap_right = pin_right_outer_x + 0.75
pin_gap_bottom = pin_body_top_y + 0.25
pin_gap_open = rectangle_contour(
    pin_gap_left,
    pin_body_top_y - 5.25,
    pin_gap_right,
    pin_gap_bottom,
)
pin_gap_closed = rectangle_contour(
    pin_gap_left,
    pin_gap_bottom,
    pin_gap_right,
    pin_gap_bottom,
)
pin_gap_mask = {
    "inv": False,
    "mode": "s",
    "pt": animated([
        keyframe(0, [pin_gap_open], [pin_gap_closed], travel),
        keyframe(16, [pin_gap_closed]),
        keyframe(20, [pin_gap_closed], [pin_gap_open], travel),
        keyframe(36, [pin_gap_open]),
    ]),
    "o": {"a": 0, "k": 100},
    "x": {"a": 0, "k": 0},
    "nm": "Right shackle opening",
}
pin = document(
    "Tonarink settings PIN toggle icon (Segoe Fluent Icons)",
    0xE72E,
    pin_name,
    [
        layer(
            1,
            "PIN shackle",
            [pin_shackle_outer, pin_contours[1]],
            pin_center,
            masks=[pin_gap_mask],
        ),
        layer(2, "PIN body", [pin_body_outer, pin_contours[2], pin_contours[3]], pin_center),
    ],
    state_markers,
)

notification_name, notification_contours, notification_bounds = extract_glyph(0xEA8F)
if len(notification_contours) != 3:
    raise ValueError(f"Expected three contours for U+EA8F, got {len(notification_contours)}")
notification_pivot = normalized_point((1024, 1792), notification_bounds)
notification_rotation = animated([
    keyframe(0, [0], [-5.5], travel),
    keyframe(4, [-5.5], [5], settle),
    keyframe(8, [5], [-3], settle),
    keyframe(12, [-3], [0], settle),
    keyframe(16, [0]),
    keyframe(20, [0], [5.5], travel),
    keyframe(24, [5.5], [-5], settle),
    keyframe(28, [-5], [3], settle),
    keyframe(32, [3], [0], settle),
    keyframe(36, [0]),
])
notification = document(
    "Tonarink settings notification toggle icon (Segoe Fluent Icons)",
    0xEA8F,
    notification_name,
    [layer(1, "Notification bell", notification_contours, notification_pivot, notification_rotation)],
    state_markers,
)


def contours_center(contours: list[dict[str, object]]) -> list[float]:
    vertices = [vertex for contour in contours for vertex in contour["v"]]
    return [
        round((min(vertex[0] for vertex in vertices) + max(vertex[0] for vertex in vertices)) / 2, 4),
        round((min(vertex[1] for vertex in vertices) + max(vertex[1] for vertex in vertices)) / 2, 4),
    ]


contact_name, contact_contours, _ = extract_glyph(0xE716)
if len(contact_contours) != 7:
    raise ValueError(f"Expected seven contours for U+E716, got {len(contact_contours)}")
contact_front = [contact_contours[index] for index in (0, 1, 4, 6)]
contact_back_head = [
    animated_contour(contact_contours[2], contact_contours[0]),
    animated_contour(contact_contours[3], contact_contours[1]),
]
contact_front_center = contours_center(contact_front)
contact_front_body_center = contours_center([contact_contours[4], contact_contours[6]])
contact_outer_right = right_side_run(contour_polyline(contact_contours[4]), contact_front_body_center[0])
contact_inner_right = right_side_run(contour_polyline(contact_contours[6]), contact_front_body_center[0])
if contact_outer_right[0][1] > contact_outer_right[-1][1]:
    contact_outer_right.reverse()
if contact_inner_right[0][1] < contact_inner_right[-1][1]:
    contact_inner_right.reverse()
contact_half_body_target = contact_outer_right + contact_inner_right
contact_half_body_start, contact_half_body_end = aligned_contours(
    contact_contours[5],
    contact_half_body_target,
)
contact_back_body = [animated_contour(contact_half_body_start, contact_half_body_end)]
contact = document(
    "Tonarink settings contact toggle icon (Segoe Fluent Icons)",
    0xE716,
    contact_name,
    [
        layer(1, "Contact foreground person", contact_front, contact_front_center),
        layer(2, "Contact background head", contact_back_head, [24, 24]),
        layer(3, "Contact background half body", contact_back_body, [24, 24]),
    ],
)

context_menu_name, context_menu_contours, _ = extract_glyph(0xE7AC)
if len(context_menu_contours) != 10:
    raise ValueError(f"Expected ten contours for U+E7AC, got {len(context_menu_contours)}")
context_menu_list = [context_menu_contours[index] for index in (0, 1, 2, 3, 4, 5, 8, 9)]
context_menu_badge_center = contours_center([context_menu_contours[6]])
context_menu_arrow = context_menu_contours[7]
context_menu_arrow_exit = translate_contour(context_menu_arrow, 24, -24)
context_menu_arrow_entry = translate_contour(context_menu_arrow, -24, 24)
context_menu_arrow_mask = {
    "inv": False,
    "mode": "s",
    "pt": animated([
        keyframe(0, [context_menu_arrow], [context_menu_arrow_exit], (0.55, 0.05, 0.90, 0.35)),
        {"t": 10, "s": [context_menu_arrow_exit], "h": 1},
        keyframe(12, [context_menu_arrow_entry], [context_menu_arrow], (0.16, 0.80, 0.20, 1.00)),
        keyframe(28, [context_menu_arrow]),
        keyframe(MOTION_END_FRAME, [context_menu_arrow]),
    ]),
    "o": {"a": 0, "k": 100},
    "x": {"a": 0, "k": 0},
    "nm": "Animated arrow cutout",
}
context_menu = document(
    "Tonarink settings Explorer context-menu toggle icon (Segoe Fluent Icons)",
    0xE7AC,
    context_menu_name,
    [
        layer(
            1,
            "Explorer context-menu arrow badge",
            [context_menu_contours[6]],
            context_menu_badge_center,
            masks=[context_menu_arrow_mask],
        ),
        layer(2, "Explorer context-menu list", context_menu_list, contours_center(context_menu_list)),
    ],
)

outputs = {
    "SettingsHistoryToggleIcon.json": history,
    "SettingsStartupToggleIcon.json": startup,
    "SettingsPinToggleIcon.json": pin,
    "SettingsNotificationToggleIcon.json": notification,
    "SettingsContactToggleIcon.json": contact,
    "SettingsContextMenuToggleIcon.json": context_menu,
}
for filename, value in outputs.items():
    serialized = json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n"
    output_path = OUTPUT_DIRECTORY / filename
    output_path.write_text(serialized, encoding="utf-8")
    print(output_path)

if len(sys.argv) > 1:
    player_root = Path(sys.argv[1]).resolve()
    for filename, value in outputs.items():
        slug = {
            "SettingsHistoryToggleIcon.json": "tonarink-settings-history",
            "SettingsStartupToggleIcon.json": "tonarink-settings-startup",
            "SettingsPinToggleIcon.json": "tonarink-settings-pin",
            "SettingsNotificationToggleIcon.json": "tonarink-settings-notification",
            "SettingsContactToggleIcon.json": "tonarink-settings-contact",
            "SettingsContextMenuToggleIcon.json": "tonarink-settings-context-menu",
        }[filename]
        player_path = player_root / "public" / "projects" / slug / "scene-1" / "lottie.json"
        player_path.parent.mkdir(parents=True, exist_ok=True)
        player_path.write_text(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
        print(player_path)

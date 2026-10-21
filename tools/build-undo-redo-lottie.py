from __future__ import annotations

import json
import math
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

PRESSED_FRAME = 8
REST_FRAME = 25
END_FRAME = REST_FRAME + 1


def deformed(contour: dict, codepoint: int, amount: float) -> dict:
    # Identify the real head/shaft join on both edges of the font outline.
    # The front shaft segment is straight, tangent to the bend. Retract along
    # that segment towards the bend: diagonally up/right for undo, up/left for
    # redo. Canvas-X deformation is NOT movement along these curved arrows.
    vertices = contour["v"]
    if codepoint == 0xE7A7:
        head_indices = set(range(10, 20))
        neck_indices = (10, 19)
        upstream_indices = (9, 20)
        tail_indices = set(range(2, 6))
        tail_join_indices = (2, 5)
        tail_upstream_indices = (1, 6)
    else:
        # Vertices 0 and 22 coincide at the contour seam; move both copies.
        head_indices = set(range(7)) | set(range(18, len(vertices)))
        neck_indices = (6, 18)
        upstream_indices = (7, 17)
        tail_indices = set(range(11, 14))
        tail_join_indices = (11, 13)
        tail_upstream_indices = (10, 14)

    def center(indices: tuple[int, int]) -> list[float]:
        return [(vertices[indices[0]][axis] + vertices[indices[1]][axis]) / 2
                for axis in range(2)]

    def retreat(join: tuple[int, int], upstream: tuple[int, int], distance: float) -> list[float]:
        start, end = center(join), center(upstream)
        tangent = [end[axis] - start[axis] for axis in range(2)]
        length = math.hypot(*tangent)
        if distance >= length:
            raise ValueError("Retraction must remain within the glyph's straight shaft segment")
        return [distance * coordinate / length for coordinate in tangent]

    head_shift = retreat(neck_indices, upstream_indices, amount)
    tail_shift = retreat(tail_join_indices, tail_upstream_indices, amount * 0.4)

    # Retract both ends towards the fixed bend, with a smaller tail movement.
    # Move each cap rigidly so its Bezier handles, rounding and width stay intact.
    def move(index: int, point: list[float]) -> list[float]:
        shift = head_shift if index in head_indices else tail_shift if index in tail_indices else (0, 0)
        return [round(point[axis] + shift[axis], 4) for axis in range(2)]

    return {
        "v": [move(index, point) for index, point in enumerate(vertices)],
        "i": [handle[:] for handle in contour["i"]],
        "o": [handle[:] for handle in contour["o"]],
        "c": contour["c"],
    }


def animated_path(contour: dict, codepoint: int) -> dict:
    # 133 ms press, then a restrained directional overshoot and soft settle.
    # AnimatedIcon holds frame 8 for an arbitrarily long physical press.
    beats = [(0, 0), (PRESSED_FRAME, 6.5), (14, -1.8), (19, 0.4), (REST_FRAME, 0)]
    easings = [
        (0.20, 0.75, 0.34, 0.94),
        (0.20, 0.75, 0.34, 0.94),
        (0.00, 0.65, 0.51, 0.99),
        (0.00, 0.65, 0.51, 0.99),
    ]
    shapes = [deformed(contour, codepoint, amount) for _, amount in beats]
    keyframes = []
    for index, (frame, _) in enumerate(beats):
        keyframe = {"t": frame, "s": [shapes[index]]}
        if index < len(easings):
            x1, y1, x2, y2 = easings[index]
            keyframe.update({
                "e": [shapes[index + 1]],
                "o": {"x": x1, "y": y1},
                "i": {"x": x2, "y": y2},
            })
        keyframes.append(keyframe)
    return {"a": 1, "k": keyframes}


def markers() -> list[dict]:
    result = [
        {"tm": 0, "cm": "Normal", "dr": 0},
        {"tm": 0, "cm": "PointerOver", "dr": 0},
        {"tm": PRESSED_FRAME, "cm": "Pressed", "dr": 0},
    ]
    for name, start, end in [
        ("NormalToPointerOver", 0, 0),
        ("PointerOverToNormal", 0, 0),
        ("NormalToPressed", 0, PRESSED_FRAME),
        ("PointerOverToPressed", 0, PRESSED_FRAME),
        ("PressedToNormal", PRESSED_FRAME, REST_FRAME),
        ("PressedToPointerOver", PRESSED_FRAME, REST_FRAME),
    ]:
        result.extend([
            {"tm": start, "cm": f"{name}_Start", "dr": 0},
            {"tm": end, "cm": f"{name}_End", "dr": 0},
        ])
    return result


def build_lottie(font: TTFont, name: str, codepoint: int) -> dict:
    glyph_name, contours = extract_contours(font, codepoint)
    return {
        "v": "5.12.2", "fr": 60, "ip": 0, "op": END_FRAME, "w": 48, "h": 48,
        "nm": f"Tonarink {name} - press and spring (Segoe Fluent Icons)",
        "ddd": 0, "assets": [],
        "meta": {"font": str(FONT_PATH), "codepoint": f"{codepoint:04X}", "glyph": glyph_name},
        "layers": [{
            "ddd": 0, "ind": 1, "ty": 4, "nm": "Arrow press and release", "sr": 1,
            "ks": {
                "o": {"a": 0, "k": 100}, "r": {"a": 0, "k": 0},
                "p": {"a": 0, "k": [0, 0, 0]}, "a": {"a": 0, "k": [0, 0, 0]},
                "s": {"a": 0, "k": [100, 100, 100]},
            },
            "ao": 0,
            "shapes": [{
                "ty": "gr", "nm": f"Segoe Fluent Icons U+{codepoint:04X}",
                "it": [
                    *[{
                        "ty": "sh", "nm": f"Contour {index + 1}",
                        "ks": animated_path(contour, codepoint),
                    } for index, contour in enumerate(contours)],
                    {
                        "ty": "fl", "nm": "Foreground {Color:var(Foreground)}",
                        "c": {"a": 0, "k": [0, 0, 0, 1]}, "o": {"a": 0, "k": 100}, "r": 1,
                    },
                    {
                        "ty": "tr", "p": {"a": 0, "k": [0, 0]}, "a": {"a": 0, "k": [0, 0]},
                        "s": {"a": 0, "k": [100, 100]}, "r": {"a": 0, "k": 0},
                        "o": {"a": 0, "k": 100}, "sk": {"a": 0, "k": 0}, "sa": {"a": 0, "k": 0},
                    },
                ],
            }],
            "ip": 0, "op": END_FRAME, "st": 0, "bm": 0,
        }],
        "markers": markers(),
    }


if __name__ == "__main__":
    font = TTFont(FONT_PATH)
    for index, (icon_name, icon_codepoint) in enumerate(ICONS.items(), start=1):
        payload = json.dumps(
            build_lottie(font, icon_name, icon_codepoint),
            ensure_ascii=False, separators=(",", ":"),
        ) + "\n"
        output_path = OUTPUT_DIRECTORY / f"{icon_name}.json"
        output_path.write_text(payload, encoding="utf-8")
        print(output_path)

        if len(sys.argv) > 1:
            preview_directory = Path(sys.argv[1]).resolve() / f"scene-{index}"
            preview_directory.mkdir(parents=True, exist_ok=True)
            preview_path = preview_directory / "lottie.json"
            preview_path.write_text(payload, encoding="utf-8")
            print(preview_path)

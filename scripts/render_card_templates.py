from __future__ import annotations

import argparse
import subprocess
from pathlib import Path
import tempfile
import xml.etree.ElementTree as ET

from PIL import Image


CARD_RECTS = {
    "slash": (132.39, 65.92, 172.12, 300.00),
    "thrust": (316.48, 63.18, 172.12, 300.00),
    "blunt": (134.29, 386.32, 172.12, 300.00),
    "ranged": (334.38, 393.71, 172.12, 300.00),
    "skill": (517.59, 64.64, 172.12, 300.00),
    "ability": (524.06, 394.10, 172.12, 300.00),
}

OUT_SIZE = (184, 300)


def strip_text(svg_in: Path, svg_out: Path) -> None:
    ET.register_namespace("", "http://www.w3.org/2000/svg")
    ET.register_namespace("xlink", "http://www.w3.org/1999/xlink")
    tree = ET.parse(svg_in)
    root = tree.getroot()

    for parent in list(root.iter()):
        for child in list(parent):
            if child.tag.endswith("text"):
                parent.remove(child)

    tree.write(svg_out, encoding="utf-8", xml_declaration=True)


def punch_art_window_alpha(img: Image.Image) -> Image.Image:
    rgba = img.convert("RGBA")
    px = rgba.load()
    w, h = rgba.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            # The AI template uses a saturated green placeholder for the art window.
            # Make it transparent so Godot can draw card art/color underneath.
            if a and g > 105 and r < 45 and b < 115:
                px[x, y] = (r, g, b, 0)
    return rgba


def render(source: Path, out_dir: Path, keep_sheet: bool) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as td:
        clean_svg = Path(td) / "card_templates_notext.svg"
        sheet_png = Path(td) / "card_templates_sheet.png"
        strip_text(source, clean_svg)
        render_sheet_with_godot(clean_svg, sheet_png)

        sheet = Image.open(sheet_png).convert("RGBA")
        sx = sheet.width / 760.62
        sy = sheet.height / 758.53

        for name, (x, y, w, h) in CARD_RECTS.items():
            pad = 2.0
            box = (
                round((x - pad) * sx),
                round((y - pad) * sy),
                round((x + w + pad) * sx),
                round((y + h + pad) * sy),
            )
            card = sheet.crop(box).resize(OUT_SIZE, Image.Resampling.LANCZOS)
            card = punch_art_window_alpha(card)
            card.save(out_dir / f"frame_{name}.png")

        if keep_sheet:
            sheet.save(out_dir / "_debug_source_sheet.png")


def render_sheet_with_godot(svg_path: Path, png_path: Path) -> None:
    godot = Path(
        "tools/Godot_v4.7-stable_mono_win64/"
        "Godot_v4.7-stable_mono_win64/"
        "Godot_v4.7-stable_mono_win64_console.exe"
    )
    if not godot.exists():
        raise FileNotFoundError(f"Godot console not found: {godot}")

    gd = png_path.with_suffix(".render.gd")
    svg_abs = svg_path.resolve().as_posix()
    png_abs = png_path.resolve().as_posix()
    gd.write_text(
        "\n".join(
            [
                "extends SceneTree",
                "func _init():",
                f"    var svg = FileAccess.get_file_as_bytes(\"{svg_abs}\")",
                "    var img = Image.new()",
                "    var err = img.load_svg_from_buffer(svg, 4.0)",
                "    if err != OK:",
                "        push_error(\"load_svg_from_buffer failed: %s\" % err)",
                "        quit(1)",
                f"    err = img.save_png(\"{png_abs}\")",
                "    if err != OK:",
                "        push_error(\"save_png failed: %s\" % err)",
                "        quit(1)",
                "    quit(0)",
            ]
        ),
        encoding="utf-8",
    )
    try:
        subprocess.run(
            [str(godot), "--headless", "--path", ".", "--script", str(gd.resolve())],
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
    finally:
        gd.unlink(missing_ok=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--source",
        default="art/card_templates/source_cards.svg",
        help="SVG exported from Adobe Illustrator.",
    )
    parser.add_argument(
        "--out",
        default="art/cards",
        help="Directory for rendered PNG card frames.",
    )
    parser.add_argument("--keep-sheet", action="store_true")
    args = parser.parse_args()
    render(Path(args.source), Path(args.out), args.keep_sheet)


if __name__ == "__main__":
    main()

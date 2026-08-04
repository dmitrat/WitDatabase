#!/usr/bin/env python3
"""
Builds the platform icon set for WitDatabase Studio from the existing logo.

Why this exists: Studio shipped one icon, WitDatabase.ico, holding a single
256x209 image. Windows tolerates a non-square icon by stretching it; macOS and
Linux packaging do not, and Parcel needs an .icns and an .svg that did not
exist. Rather than draw a new logo, this squares and re-packages the one Studio
already has, so the branding is unchanged and the derivation is reproducible.

    python build-icons.py

Requires Pillow. Writes into the directory this file lives in.
"""

from __future__ import annotations

import base64
import io
import pathlib

from PIL import Image

HERE = pathlib.Path(__file__).parent
SOURCE = HERE.parent.parent / "WitDatabase.ico"

# Windows wants every size in one file; macOS wants the icns ladder.
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
ICNS_SIZES = [16, 32, 64, 128, 256, 512, 1024]


def squared(image: Image.Image) -> Image.Image:
    """Centres the logo on a transparent square canvas without rescaling it."""
    side = max(image.size)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(
        image,
        ((side - image.width) // 2, (side - image.height) // 2),
        image,
    )
    return canvas


def main() -> None:
    source = Image.open(SOURCE).convert("RGBA")
    print(f"source {SOURCE.name}: {source.size}")

    master = squared(source)
    largest = max(ICNS_SIZES)
    master = master.resize((largest, largest), Image.LANCZOS)

    png_path = HERE / "WitDatabaseStudio.png"
    master.resize((256, 256), Image.LANCZOS).save(png_path)
    print(f"wrote {png_path.name}")

    ico_path = HERE / "WitDatabaseStudio.ico"
    master.save(ico_path, sizes=[(s, s) for s in ICO_SIZES])
    print(f"wrote {ico_path.name}: {ICO_SIZES}")

    icns_path = HERE / "WitDatabaseStudio.icns"
    master.save(icns_path, format="ICNS", sizes=[(s, s) for s in ICNS_SIZES])
    print(f"wrote {icns_path.name}: {ICNS_SIZES}")

    # Linux packaging asks for an SVG. The logo is raster, so this is an SVG
    # wrapper around the 512px bitmap - it scales cleanly enough for desktop
    # icon sizes and keeps one source of truth for the artwork.
    buffer = io.BytesIO()
    master.resize((512, 512), Image.LANCZOS).save(buffer, format="PNG")
    encoded = base64.b64encode(buffer.getvalue()).decode("ascii")

    svg_path = HERE / "WitDatabaseStudio.svg"
    svg_path.write_text(
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<svg xmlns="http://www.w3.org/2000/svg" '
        'xmlns:xlink="http://www.w3.org/1999/xlink" '
        'width="512" height="512" viewBox="0 0 512 512">\n'
        f'  <image width="512" height="512" xlink:href="data:image/png;base64,{encoded}"/>\n'
        "</svg>\n",
        encoding="utf-8",
    )
    print(f"wrote {svg_path.name}")


if __name__ == "__main__":
    main()

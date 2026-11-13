#!/usr/bin/env python3
"""Draws the installer's wizard pictures (brand gradient + logo) for 100/125/150/200 % screen scaling.

Run once after the logo or the brand colours change; the BMPs are committed next to this script.
"""
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
LOGO = HERE.parents[1] / "src" / "Manajoy.GameClass.Bridge" / "SetupUi" / "logo-white.png"
STOPS = [(0.0, (0x7A, 0x2B, 0xE2)), (0.26, (0x43, 0x24, 0xD6)), (0.58, (0x1B, 0x54, 0xE4)), (1.0, (0x0E, 0xA5, 0xF0))]
SCALES = [1.0, 1.25, 1.5, 2.0]


def colour(t):
    for (t0, c0), (t1, c1) in zip(STOPS, STOPS[1:]):
        if t <= t1:
            k = (t - t0) / (t1 - t0)
            return tuple(round(a + (b - a) * k) for a, b in zip(c0, c1))
    return STOPS[-1][1]


def gradient(w, h):
    image = Image.new("RGB", (w, h))
    pixels = image.load()
    for y in range(h):
        for x in range(w):
            # 150 degrees, like the window's rail: top-left purple → bottom-right blue.
            t = min(1.0, max(0.0, (0.5 * x / w + 0.866 * y / h) / 1.366))
            pixels[x, y] = colour(t)
    return image


def with_logo(image, size, top):
    logo = Image.open(LOGO).convert("RGBA").resize((size, size), Image.LANCZOS)
    left = (image.width - size) // 2
    image.paste(logo, (left, top), logo)
    return image


def main():
    for scale in SCALES:
        tag = f"{round(scale * 100)}"
        w, h = round(164 * scale), round(314 * scale)
        with_logo(gradient(w, h), round(72 * scale), round(48 * scale)).save(HERE / "images" / f"wizard-{tag}.bmp")
        s = round(55 * scale)
        with_logo(gradient(s, s), round(40 * scale), round(7.5 * scale)).save(HERE / "images" / f"small-{tag}.bmp")


if __name__ == "__main__":
    main()

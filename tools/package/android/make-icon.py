#!/usr/bin/env python3
"""Draws the game's icon (game/icon.png, 512 x 512): the title mark's splat of paint in the menu's accent
colour (a blob with fingers thrown out round it, droplets beyond, drips running down; the same recipe as
game/ui/TitleMark.cs), a darker splat a little behind it, and "Pb" stencilled in dark over it, on the
menus' dark ground. It's the launcher icon on Android and the window icon on the desktop. The title is
the working one; nothing here is anyone's mark. Edit this and run it, not the picture:

    python3 tools/package/android/make-icon.py game/icon.png
"""
import math
import random
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

SIZE = 512
ACCENT = (255, 133, 41)        # UiKit.Accent (1, 0.52, 0.16)
SHADOW = (115, 60, 18)         # the accent darkened, for the splat behind
GROUND = (27, 29, 33)          # the menus' backdrop
INK = (24, 22, 20)             # the stencilled letters
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


def capsule(p, a, b, r0, r1):
    """Signed distance from p to a capsule from a (radius r0) to b (radius r1)."""
    ax, ay = p[0] - a[0], p[1] - a[1]
    bx, by = b[0] - a[0], b[1] - a[1]
    t = max(0.0, min(1.0, (ax * bx + ay * by) / max(bx * bx + by * by, 1e-6)))
    dx, dy = ax - bx * t, ay - by * t
    return math.hypot(dx, dy) - (r0 + (r1 - r0) * t)


def splat(rng, scale):
    """The splat's coverage (0-255) as a greyscale image, like TitleMark.Paint but square and scaled up."""
    def R(a, b):
        return a + rng.random() * (b - a)

    cx, cy = SIZE * 0.5, SIZE * 0.46
    radius = 60.0 * scale
    p1, p2, p3 = R(0, math.tau), R(0, math.tau), R(0, math.tau)
    fingers = []
    for i in range(11):
        a = math.tau * (i + R(-0.3, 0.3)) / 11
        d = (math.cos(a) * 1.2, math.sin(a) * 0.9)
        fingers.append(((cx + d[0] * radius * 0.7, cy + d[1] * radius * 0.7),
                        (cx + d[0] * radius * R(1.2, 1.6), cy + d[1] * radius * R(1.2, 1.6)),
                        R(9, 14) * scale, R(2, 4.5) * scale))
    drops = []
    for _ in range(18):
        a = R(0, math.tau)
        k = radius * R(1.45, 1.85)
        drops.append(((cx + math.cos(a) * 1.15 * k, cy + math.sin(a) * 0.9 * k), R(2.5, 7) * scale))
    drips = []
    for _ in range(4):
        x = cx + R(-0.6, 0.6) * radius
        top = cy + radius * 0.5
        drips.append(((x, top), (x + R(-2, 2) * scale, top + R(30, 52) * scale), R(3.5, 5.5) * scale))

    image = Image.new("L", (SIZE, SIZE), 0)
    pixels = image.load()
    for y in range(SIZE):
        for x in range(SIZE):
            p = (x + 0.5, y + 0.5)
            dx, dy = p[0] - cx, p[1] - cy
            angle = math.atan2(dy, dx)
            edge = radius * (1 + 0.1 * math.sin(5 * angle + p1) + 0.07 * math.sin(9 * angle + p2) + 0.04 * math.sin(17 * angle + p3))
            inside = math.hypot(dx / 1.15, dy / 0.92) - edge
            for a, b, r0, r1 in fingers:
                inside = min(inside, capsule(p, a, b, r0, r1))
            for at, r in drops:
                inside = min(inside, math.hypot(p[0] - at[0], p[1] - at[1]) - r)
            for a, b, r in drips:
                inside = min(inside, capsule(p, a, b, r, r * 0.8))
                inside = min(inside, math.hypot(p[0] - b[0], p[1] - b[1]) - r * 1.35)
            pixels[x, y] = int(max(0.0, min(1.0, 0.5 - inside)) * 255)
    return image


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "game/icon.png"
    cover = splat(random.Random(0x5B1A7), 2.05)
    icon = Image.new("RGB", (SIZE, SIZE), GROUND)
    behind = Image.new("RGB", (SIZE, SIZE), SHADOW)
    icon.paste(behind, (10, 12), cover)
    icon.paste(Image.new("RGB", (SIZE, SIZE), ACCENT), (0, 0), cover)
    letters = Image.new("L", (SIZE, SIZE), 0)
    font = ImageFont.truetype(FONT, 190)
    draw = ImageDraw.Draw(letters)
    box = draw.textbbox((0, 0), "Pb", font=font)
    draw.text(((SIZE - (box[2] - box[0])) / 2 - box[0], SIZE * 0.47 - (box[3] - box[1]) / 2 - box[1]), "Pb", font=font, fill=255)
    # Stencilled: a gap through each letter, as on a sprayed stencil.
    ImageDraw.Draw(letters).rectangle((0, int(SIZE * 0.47) - 5, SIZE, int(SIZE * 0.47) + 5), fill=0)
    icon.paste(Image.new("RGB", (SIZE, SIZE), INK), (0, 0), letters.filter(ImageFilter.GaussianBlur(0.6)))
    icon.save(out)
    print(f"{out}: {SIZE} x {SIZE}")


if __name__ == "__main__":
    main()

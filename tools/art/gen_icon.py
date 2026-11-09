"""Значок приложения: пухлая милая пчёлка (пиксель-арт 64x64 → 512x512 без сглаживания).

Запуск из корня репозитория: python3 tools/art/gen_icon.py
Результат: Assets/Swarm/Editor/AppIcon.png (его ставит SwarmProjectSetup.ConfigurePlayer)
и tools/art/out/icon_preview.png (как значок выглядит в маске лаунчера).
"""
import math
import os

from PIL import Image, ImageDraw

N = 64
SCALE = 8


def hx(s):
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


BG0, BG1, BG2 = hx('ffe29a'), hx('ffd066'), hx('f5b942')     # тёплый медовый фон
OUT = hx('3b2314')                                          # контур
YEL = [hx('f2a515'), hx('ffc634'), hx('ffd95e'), hx('fff0a8')]
BRN = [hx('3b2314'), hx('57361f')]
WING = [hx('cfc8bb'), hx('e6e0d4'), hx('f7f4ee')]
PINK = hx('ff9a8a')
WHITE = hx('ffffff')
INK = hx('1d1310')

img = Image.new('RGBA', (N, N), (0, 0, 0, 0))
px = img.load()


def put(x, y, c):
    if 0 <= x < N and 0 <= y < N:
        px[x, y] = c


def inside_ellipse(x, y, cx, cy, rx, ry):
    return ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1.0


def fill_ellipse(cx, cy, rx, ry, ramp, light=(-0.6, -0.8)):
    """Эллипс с объёмом: свет сверху-слева, по ступеням палитры."""
    for y in range(N):
        for x in range(N):
            if inside_ellipse(x, y, cx, cy, rx, ry):
                u, v = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
                k = 0.55 - 0.45 * (u * light[0] + v * light[1]) * -1
                k = max(0.0, min(0.999, 0.5 + 0.5 * (-(u * 0.55) - (v * 0.75))))
                put(x, y, ramp[int(k * len(ramp))])


def outline(mask_color_check, color):
    src = img.copy().load()
    for y in range(N):
        for x in range(N):
            if src[x, y][3] == 0:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    xx, yy = x + dx, y + dy
                    if 0 <= xx < N and 0 <= yy < N and mask_color_check(src[xx, yy]):
                        put(x, y, color)
                        break


# --- фон: скруглённый квадрат с мягкими сотами
bg = Image.new('RGBA', (N, N), (0, 0, 0, 0))
d = ImageDraw.Draw(bg)
d.rounded_rectangle([0, 0, N - 1, N - 1], radius=12, fill=BG0)
bpx = bg.load()
for y in range(N):
    for x in range(N):
        if bpx[x, y][3] == 0:
            continue
        # шестиугольная сетка сот — тонкие линии тоном темнее
        row = y // 7
        ox = (row % 2) * 4
        if (y % 7 == 0 and (x + ox) % 8 < 5) or ((x + ox) % 8 == 5 and y % 7 != 0):
            bpx[x, y] = BG1
        # радиальное затемнение к краям
        r = math.hypot(x - 30, y - 28) / 44
        if r > 0.85 and bpx[x, y] == BG0:
            bpx[x, y] = BG1

# --- крылья (за телом)
fill_ellipse(36, 15, 8.5, 10.5, WING)
fill_ellipse(47, 17, 7.5, 9.5, WING)
outline(lambda c: c in WING, OUT)

# --- тело: пухлый овал
BCX, BCY, BRX, BRY = 30, 38, 21, 16.5
for y in range(N):
    for x in range(N):
        if inside_ellipse(x, y, BCX, BCY, BRX, BRY):
            u, v = (x + 0.5 - BCX) / BRX, (y + 0.5 - BCY) / BRY
            k = max(0.0, min(0.999, 0.55 - 0.35 * u - 0.5 * v))
            c = YEL[int(k * len(YEL))]
            # две полоски ближе к хвосту
            if 30 <= x <= 34 or 39 <= x <= 43:
                c = BRN[1] if v < -0.35 and u < 0.6 else BRN[0]
            put(x, y, c)
# жало
for i, (x, y) in enumerate([(51, 37), (52, 38), (53, 39), (52, 40), (51, 41)]):
    put(x, y, OUT)
put(51, 38, BRN[1]); put(51, 39, BRN[1]); put(52, 39, BRN[1]); put(51, 40, BRN[1])
outline(lambda c: c in YEL or c in BRN, OUT)

# --- мордочка
for ex in (18, 26):
    for dx in range(2):
        for dy in range(3):
            put(ex + dx, 36 + dy, INK)
    put(ex, 36, WHITE)                       # блик в глазу
for x, y in [(21, 41), (22, 42), (23, 41), (24, 42), (25, 41)]:
    put(x, y, INK)                           # улыбка «w»
for cx in (14, 29):
    for dx in range(3):
        put(cx + dx, 40, PINK)
        put(cx + dx, 41, PINK)

# --- усики
for x, y in [(21, 23), (20, 22), (19, 21), (19, 20), (18, 19)]:
    put(x, y, OUT)
for x, y in [(24, 22), (24, 21), (23, 20), (23, 19)]:
    put(x, y, OUT)
for x, y in [(17, 18), (18, 18), (17, 17), (18, 17), (22, 18), (23, 18), (22, 17), (23, 17)]:
    put(x, y, OUT)

# --- лапки
for x, y in [(23, 55), (23, 56), (30, 55), (30, 56), (38, 54), (39, 55)]:
    put(x, y, OUT)

full = Image.alpha_composite(bg, img)
root = os.path.join(os.path.dirname(__file__), '..', '..')
big = full.resize((N * SCALE, N * SCALE), Image.NEAREST)
os.makedirs(os.path.join(root, 'Assets', 'Swarm', 'Editor'), exist_ok=True)
big.save(os.path.join(root, 'Assets', 'Swarm', 'Editor', 'AppIcon.png'))

# превью: как в маске лаунчера (круг / скруглённый квадрат) и в реальном размере
os.makedirs(os.path.join(os.path.dirname(__file__), 'out'), exist_ok=True)
prev = Image.new('RGBA', (N * SCALE + 300, N * SCALE), (40, 44, 52, 255))
prev.alpha_composite(big, (0, 0))
circle = Image.new('L', (192, 192), 0)
ImageDraw.Draw(circle).ellipse([0, 0, 191, 191], fill=255)
small = full.resize((192, 192), Image.NEAREST)
prev.paste(small, (N * SCALE + 50, 40), circle)
prev.alpha_composite(full.resize((48, 48), Image.LANCZOS), (N * SCALE + 122, 300))
prev.save(os.path.join(os.path.dirname(__file__), 'out', 'icon_preview.png'))
print('icon ok')

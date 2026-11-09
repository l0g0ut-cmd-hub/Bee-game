#!/usr/bin/env python3
"""Генератор пиксель-арта SWARM.

Рисует все спрайты программно из единой палитры, пакует в атлас и пишет
Assets/Swarm/Resources/SwarmAtlas.png + AtlasData.cs (прямоугольники, пивоты, шрифт).

Запуск: python3 tools/art/gen_art.py [--preview DIR]
"""
import math
import os
import random
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
from palette import *  # noqa
import font

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT_PNG = os.path.join(ROOT, 'Assets/Swarm/Resources/SwarmAtlas.png')
OUT_CS = os.path.join(ROOT, 'Assets/Swarm/Scripts/Presentation/Generated/AtlasData.cs')

BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def dith(x, y):
    return (BAYER[y & 3][x & 3] + 0.5) / 16.0


def ramp_pick(ramp, t, x, y):
    """t in [0,1] -> цвет из рампы с упорядоченным дизерингом между соседями."""
    t = max(0.0, min(0.9999, t))
    f = t * (len(ramp) - 1)
    i = int(f)
    frac = f - i
    if i + 1 < len(ramp) and frac > dith(x, y):
        i += 1
    return ramp[i]


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.px = [[CLEAR] * w for _ in range(h)]

    def get(self, x, y):
        if 0 <= x < self.w and 0 <= y < self.h:
            return self.px[y][x]
        return CLEAR

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h and c is not None:
            self.px[y][x] = c

    def opaque(self, x, y):
        return self.get(x, y)[3] > 0

    def pattern(self, rows, cmap, ox=0, oy=0):
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch in cmap:
                    self.set(ox + x, oy + y, cmap[ch])

    def ellipse(self, cx, cy, rx, ry, ramp, light=(-0.55, -0.65), amb=0.18, dither=True, only_empty=False):
        lx, ly = light
        lz = math.sqrt(max(0.0, 1 - lx * lx - ly * ly))
        for y in range(int(cy - ry - 1), int(cy + ry + 2)):
            for x in range(int(cx - rx - 1), int(cx + rx + 2)):
                u = (x + 0.5 - cx) / rx
                v = (y + 0.5 - cy) / ry
                d = u * u + v * v
                if d > 1.0:
                    continue
                if only_empty and self.opaque(x, y):
                    continue
                nz = math.sqrt(max(0.0, 1 - d))
                t = amb + (1 - amb) * max(0.0, u * lx + v * ly + nz * lz)
                c = ramp_pick(ramp, t, x, y) if dither else ramp[min(len(ramp) - 1, int(t * len(ramp)))]
                self.set(x, y, c)

    def rect(self, x0, y0, w, h, c):
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                self.set(x, y, c)

    def outline(self, color, diag=False, where=None):
        add = []
        for y in range(self.h):
            for x in range(self.w):
                if self.opaque(x, y):
                    continue
                nb = [(1, 0), (-1, 0), (0, 1), (0, -1)]
                if diag:
                    nb += [(1, 1), (-1, -1), (1, -1), (-1, 1)]
                for dx, dy in nb:
                    if self.opaque(x + dx, y + dy):
                        if where is None or where(x, y):
                            add.append((x, y))
                        break
        for x, y in add:
            self.set(x, y, color)

    def selective_outline(self, darken_map, fallback):
        """Контур цветом, зависящим от соседнего пикселя (тёмный оттенок, а не чёрный)."""
        add = []
        for y in range(self.h):
            for x in range(self.w):
                if self.opaque(x, y):
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    c = self.get(x + dx, y + dy)
                    if c[3] > 0:
                        add.append((x, y, darken_map.get(c[:3], fallback)))
                        break
        for x, y, c in add:
            self.set(x, y, c)

    def image(self):
        img = Image.new('RGBA', (self.w, self.h))
        img.putdata([c for row in self.px for c in row])
        return img

    def flipped(self):
        c = Canvas(self.w, self.h)
        for y in range(self.h):
            c.px[y] = list(reversed(self.px[y]))
        return c


def darken_map_for(*ramps):
    m = {}
    for r in ramps:
        for i, c in enumerate(r):
            m[c[:3]] = r[max(0, i - 3)] if i >= 3 else INK[0]
    return m


SPRITES = []  # (name, Canvas, pivot(x,y from bottom-left) or None)


def add(name, cv, pivot=None):
    SPRITES.append((name, cv, pivot))


# ------------------------------------------------------------------ пчела
BEE_MAP = {
    'k': INK[0], 'K': INK[1], 'y': HONEY[3], 'Y': HONEY[4], 'h': HONEY[5], 'd': HONEY[2],
    'w': WING[0], 'W': WING[2], 'v': WING[1], 'e': WHITE[3], 's': INK[2],
    'o': ORANGE[3], 'O': ORANGE[4],
}
BEE_BODY = [
    '..kkkkkkk..',
    '.kyYhKYhKkk',
    'skdyyKyyKek',
    '.kddyKdyKKk',
    '..kkkkkkkk.',
]
BEE_WINGS_UP = [
    '...vv.vv...',
    '..vWwvWwv..',
    '..vwwvwwv..',
]
BEE_WINGS_DOWN = [
    '...........',
    '.vvv.vvv...',
    'vWwwvWwwv..',
]


def make_bee(wings, loaded):
    cv = Canvas(11, 9)
    cv.pattern(BEE_BODY, BEE_MAP, 0, 3)
    cv.pattern(wings, BEE_MAP, 0, 0)
    # ножки
    cv.pattern(['...k.k.k...'], BEE_MAP, 0, 8)
    if loaded:
        cv.pattern(['...O.O.....', '...o.o.....'], BEE_MAP, 0, 7)
    return cv


add('BeeA', make_bee(BEE_WINGS_UP, False))
add('BeeB', make_bee(BEE_WINGS_DOWN, False))
add('BeeLoadA', make_bee(BEE_WINGS_UP, True))
add('BeeLoadB', make_bee(BEE_WINGS_DOWN, True))


# ------------------------------------------------------------------ осы и шершни (процедурно)
def insect(w, h, parts, wings, stripe_cols=(), stripe_c=INK[1], stinger=None, eye=None, legs=(), flash=False,
           antenna=(), outline=INK[0]):
    cv = Canvas(w, h)
    # крылья под телом рисуем первыми (прозрачная часть сверху)
    for (cx, cy, rx, ry) in wings:
        for y in range(int(cy - ry - 1), int(cy + ry + 2)):
            for x in range(int(cx - rx - 1), int(cx + rx + 2)):
                u = (x + 0.5 - cx) / rx
                v = (y + 0.5 - cy) / ry
                d = u * u + v * v
                if d <= 1:
                    cv.set(x, y, WING[1] if d > 0.55 else (WING[2] if (u < -0.2 and v < -0.1) else WING[0]))
    body = Canvas(w, h)
    for (cx, cy, rx, ry, ramp) in parts:
        body.ellipse(cx, cy, rx, ry, ramp)
    for x in stripe_cols:
        for y in range(h):
            if body.opaque(x, y):
                body.set(x, y, stripe_c)
    for (x, y) in legs:
        body.set(x, y, INK[0])
    for (x, y) in antenna:
        body.set(x, y, INK[0])
    if stinger:
        body.set(stinger[0], stinger[1], INK[2])
    body.outline(outline)
    if eye:
        body.set(eye[0], eye[1], INK[0])
        body.set(eye[0], eye[1] - 1, WHITE[3])
    for y in range(h):
        for x in range(w):
            if body.opaque(x, y):
                cv.set(x, y, body.get(x, y))
    if flash:
        for y in range(h):
            for x in range(w):
                if cv.opaque(x, y):
                    cv.set(x, y, (255, 255, 255, 255))
    return cv


def wasp(frame, flash=False):
    wings = [(8, 3, 3.2, 2.2), (11, 3, 2.6, 2.0)] if frame == 0 else [(6, 5, 3.6, 1.6), (9, 5, 3.0, 1.4)]
    parts = [
        (5, 8, 4.2, 2.6, [HONEY[3], HONEY[4], HONEY[5], WHITE[3]]),
        (9.5, 7.5, 1.9, 2.0, [INK[0], INK[1], INK[2], STONE[2]]),
        (12.5, 7.5, 1.9, 1.9, [HONEY[3], HONEY[4], HONEY[5], WHITE[3]]),
    ]
    return insect(16, 12, parts, wings, stripe_cols=(2, 4, 6), stripe_c=INK[0], stinger=(0, 8), eye=(13, 7),
                  legs=[(8, 10), (10, 10), (11, 10)], antenna=[(14, 5), (15, 4)], flash=flash, outline=RED[1])


def hornet(frame, flash=False):
    wings = [(11, 4, 4.6, 3.0), (15, 4, 3.8, 2.6)] if frame == 0 else [(8, 7, 5.4, 2.1), (12, 7, 4.6, 1.9)]
    parts = [
        (7, 11, 6.0, 3.8, [HONEY[0], HONEY[1], HONEY[2], HONEY[3], HONEY[4]]),
        (13.5, 10.5, 2.8, 3.0, HORNET),
        (18, 10.5, 2.8, 2.7, [HORNET[1], HORNET[2], HORNET[3], ORANGE[4]]),
    ]
    return insect(22, 16, parts, wings, stripe_cols=(3, 4, 7, 8), stripe_c=HORNET[0], stinger=(0, 11),
                  eye=(19, 10), legs=[(11, 14), (12, 15), (14, 15), (16, 14)], antenna=[(20, 7), (21, 6), (21, 5)],
                  flash=flash, outline=RED[0])


add('WaspA', wasp(0))
add('WaspB', wasp(1))
add('WaspFlash', wasp(0, True))
add('HornetA', hornet(0))
add('HornetB', hornet(1))
add('HornetFlash', hornet(0, True))


# ------------------------------------------------------------------ тени / частицы
def ellipse_mask(w, h, color=SHADOW):
    cv = Canvas(w, h)
    for y in range(h):
        for x in range(w):
            u = (x + 0.5 - w / 2) / (w / 2)
            v = (y + 0.5 - h / 2) / (h / 2)
            if u * u + v * v <= 1.0:
                cv.set(x, y, color)
    return cv


add('ShadowS', ellipse_mask(5, 2))
add('ShadowM', ellipse_mask(9, 3))
add('ShadowL', ellipse_mask(15, 5))
add('ShadowXL', ellipse_mask(40, 12))

W = (255, 255, 255, 255)
px1 = Canvas(1, 1); px1.set(0, 0, W); add('Pixel', px1)
px2 = Canvas(2, 2); px2.rect(0, 0, 2, 2, W); add('Pixel2', px2)
dot3 = Canvas(3, 3); dot3.pattern(['.#.', '###', '.#.'], {'#': W}); add('Dot3', dot3)
dot5 = Canvas(5, 5); dot5.pattern(['.###.', '#####', '#####', '#####', '.###.'], {'#': W}); add('Dot5', dot5)
spark = Canvas(5, 5); spark.pattern(['..#..', '..#..', '##W##', '..#..', '..#..'], {'#': (255, 255, 255, 170), 'W': W})
add('Sparkle', spark)
pollen = Canvas(2, 2); pollen.pattern(['Yy', 'yd'], {'Y': HONEY[5], 'y': HONEY[4], 'd': HONEY[3]}); add('Pollen', pollen)
drop = Canvas(3, 4); drop.pattern(['.y.', 'yYy', 'yhy', '.d.'], {'y': HONEY[3], 'Y': HONEY[4], 'h': HONEY[5], 'd': HONEY[2]})
add('HoneyDrop', drop)
for i, r in enumerate([1.5, 2.5, 3.5]):
    cv = Canvas(9, 9)
    for y in range(9):
        for x in range(9):
            d = math.hypot(x - 4, y - 4)
            if r - 0.7 <= d <= r + 0.5:
                cv.set(x, y, W)
    add('Ring%d' % i, cv)
leaf = Canvas(3, 2); leaf.pattern(['.g#', 'g#.'], {'g': LEAF[4], '#': LEAF[5]}); add('Leaf', leaf)
petal = Canvas(2, 2); petal.pattern(['#.', '##'], {'#': W}); add('Petal', petal)
# крестик-«пух» погибшей пчелы
puff = Canvas(5, 5); puff.pattern(['.#.#.', '#.#.#', '.#.#.', '#.#.#', '.#.#.'], {'#': (255, 255, 255, 200)})
add('Puff', puff)


# ------------------------------------------------------------------ цветы
def flower(kind, frame, depleted=False):
    cv = Canvas(15, 20)
    sway = 1 if frame == 1 else 0
    base_x = 7
    # стебель
    for y in range(9, 20):
        bend = sway if y < 13 else 0
        cv.set(base_x + bend, y, LEAF[3])
        if y > 11:
            cv.set(base_x + bend + 1, y, LEAF[2])
    # листья
    cv.pattern(['gG.', 'gGG', '.gg'], {'g': LEAF[3], 'G': LEAF[5]}, base_x - 3, 13)
    cv.pattern(['.Gg', 'GGg', 'gg.'], {'g': LEAF[3], 'G': LEAF[4]}, base_x + 2, 15)
    hx_, hy = base_x + sway + 0.5, 6.0
    if depleted:
        # закрытый поникший бутон
        bud = {
            'daisy': WHITE, 'poppy': RED, 'corn': BLUE, 'violet': PURPLE, 'sun': HONEY, 'pink': PINK,
        }[kind]
        cv.ellipse(hx_ + 0.5, hy + 3, 2.2, 2.6, [LEAF[2], LEAF[3], LEAF[4]])
        cv.set(int(hx_), int(hy + 1), bud[1])
        cv.set(int(hx_) + 1, int(hy + 1), bud[2])
        cv.set(int(hx_), int(hy), bud[1])
    elif kind == 'daisy':
        for a in range(10):
            ang = a / 10 * math.tau + 0.2
            cv.ellipse(hx_ + math.cos(ang) * 3.6, hy + math.sin(ang) * 3.0, 1.5, 1.4, WHITE)
        cv.ellipse(hx_, hy, 2.2, 2.0, [HONEY[2], HONEY[3], HONEY[4], HONEY[5]])
    elif kind == 'poppy':
        for a in range(5):
            ang = a / 5 * math.tau - 0.4
            cv.ellipse(hx_ + math.cos(ang) * 2.6, hy + math.sin(ang) * 2.2, 2.9, 2.6, RED[1:])
        cv.ellipse(hx_, hy, 1.4, 1.3, [INK[0], INK[1], INK[2]])
    elif kind == 'corn':
        for a in range(9):
            ang = a / 9 * math.tau
            for r in (2.0, 3.0, 4.2):
                cv.set(int(hx_ + math.cos(ang) * r), int(hy + math.sin(ang) * r * 0.85), BLUE[2] if r < 4 else BLUE[3])
        cv.ellipse(hx_, hy, 2.0, 1.8, [PURPLE[0], PURPLE[1], PURPLE[2]])
    elif kind == 'violet':
        for a in range(5):
            ang = a / 5 * math.tau + 1.2
            cv.ellipse(hx_ + math.cos(ang) * 2.6, hy + math.sin(ang) * 2.4, 2.2, 2.0, PURPLE[1:])
        cv.ellipse(hx_, hy, 1.2, 1.1, [HONEY[3], HONEY[4], HONEY[5]])
    elif kind == 'pink':
        for a in range(6):
            ang = a / 6 * math.tau
            cv.ellipse(hx_ + math.cos(ang) * 3.0, hy + math.sin(ang) * 2.6, 2.0, 1.8, PINK)
        cv.ellipse(hx_, hy, 1.6, 1.5, [HONEY[3], HONEY[4], WHITE[3]])
    elif kind == 'sun':
        for a in range(12):
            ang = a / 12 * math.tau
            cv.ellipse(hx_ + math.cos(ang) * 4.3, hy + math.sin(ang) * 3.8, 1.6, 1.5, HONEY[2:])
        cv.ellipse(hx_, hy, 3.0, 2.7, [WOOD[0], WOOD[1], WOOD[2], WOOD[3]])
    dm = darken_map_for(WHITE, RED, BLUE, PURPLE, PINK, HONEY, LEAF, WOOD, INK)
    cv.selective_outline(dm, LEAF[1])
    return cv


FLOWERS = ['daisy', 'poppy', 'corn', 'violet', 'pink', 'sun']
for k in FLOWERS:
    cap = k.capitalize()
    add('Flower%sA' % cap, flower(k, 0), (7, 0))
    add('Flower%sB' % cap, flower(k, 1), (7, 0))
    add('Flower%sDry' % cap, flower(k, 0, True), (7, 0))


# ------------------------------------------------------------------ улей-колода (соломенный)
def hive():
    w, h = 44, 50
    cv = Canvas(w, h)
    cx = 22
    top, bottom = 4, 38
    # купол из соломенных жгутов
    for y in range(top, bottom):
        t = (y - top) / (bottom - top)
        half = 19 * math.sin(min(1.0, 0.25 + t * 1.05) * math.pi / 2) ** 0.8
        band = (y - top) % 5
        for x in range(int(cx - half), int(cx + half) + 1):
            u = (x + 0.5 - cx) / max(1.0, half)
            shade = 0.62 - 0.45 * u - 0.25 * t
            if band == 0:
                shade -= 0.38  # шов между жгутами
            elif band == 1:
                shade += 0.2
            # фактура соломы
            if (x * 7 + y * 3) % 11 == 0 and band not in (0,):
                shade -= 0.15
            cv.set(x, y, ramp_pick(STRAW, shade, x, y))
    # леток
    for y in range(30, 38):
        for x in range(cx - 4, cx + 5):
            u = (x + 0.5 - cx) / 4.5
            v = (y - 38) / 8.0
            if u * u + v * v <= 1.0:
                cv.set(x, y, INK[0] if v > -0.75 else INK[1])
    for x in range(cx - 5, cx + 6):
        if cv.get(x, 38)[3] > 0 or True:
            pass
    # подставка: пень/доска
    for y in range(38, 46):
        for x in range(cx - 18, cx + 19):
            u = (x - cx) / 18
            t = 0.65 - 0.4 * u - (0.3 if y >= 43 else 0) - (0.25 if y == 38 else 0)
            cv.set(x, y, ramp_pick(WOOD, t, x, y))
    for x in range(cx - 18, cx + 19, 6):
        cv.set(x, 41, WOOD[1])
        cv.set(x + 1, 42, WOOD[1])
    # медовые подтёки
    for (x, ln) in ((cx - 9, 4), (cx + 6, 3), (cx + 13, 2)):
        for i in range(ln):
            cv.set(x, 36 + i, HONEY[3] if i < ln - 1 else HONEY[4])
        cv.set(x, 36 + ln, HONEY[2])
    # верхушка
    cv.ellipse(cx + 0.5, top + 0.5, 3.5, 2.2, STRAW[1:])
    dm = darken_map_for(STRAW, WOOD, HONEY, INK)
    cv.selective_outline(dm, WOOD[0])
    return cv


add('Hive', hive(), (22, 3))


def comb_glow():
    cv = Canvas(13, 7)
    for y in range(7):
        for x in range(13):
            u = (x + 0.5 - 6.5) / 6.5
            v = (y + 0.5 - 3.5) / 3.5
            d = u * u + v * v
            if d <= 1:
                cv.set(x, y, (255, 255, 255, int(255 * (1 - d) ** 1.5)))
    return cv


add('Glow', comb_glow())


# ------------------------------------------------------------------ деревья
def canopy(w, h, seed, ramp=LEAF):
    rnd = random.Random(seed)
    cv = Canvas(w, h)
    cx, cy = w / 2, h / 2 + 1
    clumps = []
    for i in range(14):
        a = rnd.random() * math.tau
        r = rnd.random() ** 0.6 * (min(w, h) / 2 - 7)
        clumps.append((cx + math.cos(a) * r * 1.1, cy + math.sin(a) * r * 0.85, rnd.uniform(5.5, 8.5)))
    clumps.sort(key=lambda c: c[1])  # нижние поверх
    # базовая масса
    cv.ellipse(cx, cy, w / 2 - 3, h / 2 - 3, ramp[1:4], amb=0.1)
    for (x, y, r) in clumps:
        cv.ellipse(x, y, r, r * 0.9, ramp[1:], light=(-0.5, -0.75), amb=0.05)
    # блики листьев
    for i in range(40):
        x = rnd.randrange(w); y = rnd.randrange(h)
        if cv.get(x, y)[:3] == ramp[4][:3]:
            cv.set(x, y, ramp[6])
    cv.outline(ramp[0])
    return cv


def trunk():
    cv = Canvas(14, 16)
    cv.pattern([
        '....wWWww.....',
        '....wWWww.....',
        '....wWWwd.....',
        '....wWWwd.....',
        '....wWwwd.....',
        '...wwWwwdd....',
        '..wwWWwwddd...',
        '.wwdWwwwdddd..',
        'ww.dwwwwdd.dd.',
    ], {'w': WOOD[2], 'W': WOOD[3], 'd': WOOD[1]}, 0, 7)
    cv.outline(WOOD[0])
    return cv


add('CanopyA', canopy(58, 50, 3), (29, 6))
add('CanopyB', canopy(46, 40, 11), (23, 5))
add('CanopyC', canopy(52, 46, 27, [hx('132416'), hx('1f3d22'), hx('2f5a2c'), hx('467a35'), hx('6a9c43'), hx('94bd5a'), hx('c1dc7c')]), (26, 6))
add('Trunk', trunk(), (6, 0))


def bush(seed, berries=False):
    rnd = random.Random(seed)
    cv = Canvas(28, 20)
    cv.ellipse(14, 12, 12, 7, LEAF[1:4])
    for i in range(7):
        cv.ellipse(5 + i * 3 + rnd.uniform(-1, 1), 9 + rnd.uniform(-2, 3), rnd.uniform(3.5, 5), 3.8, LEAF[1:])
    if berries:
        for i in range(7):
            x, y = rnd.randrange(5, 23), rnd.randrange(6, 16)
            if cv.opaque(x, y):
                cv.set(x, y, RED[2]); cv.set(x, y - 1, RED[4])
    cv.outline(LEAF[0])
    return cv


add('BushA', bush(4), (14, 2))
add('BushB', bush(9, True), (14, 2))


# ------------------------------------------------------------------ камни, пни, грибы
def rock(w, h, seed):
    rnd = random.Random(seed)
    cv = Canvas(w, h)
    cv.ellipse(w / 2, h / 2 + 1, w / 2 - 1, h / 2 - 1.5, STONE, light=(-0.6, -0.7))
    cv.ellipse(w / 2 - w * 0.18, h / 2 - 0.5, w / 4, h / 4, STONE[1:], only_empty=False, light=(-0.6, -0.7))
    for i in range(3):
        x, y = rnd.randrange(w), rnd.randrange(h)
        if cv.opaque(x, y) and cv.opaque(x + 1, y):
            cv.set(x, y, STONE[1]); cv.set(x + 1, y, STONE[1])
    # мох сверху
    for x in range(w):
        for y in range(h):
            if cv.opaque(x, y) and not cv.opaque(x, y - 1) and rnd.random() < 0.55:
                cv.set(x, y, MOSS[1]);
                if rnd.random() < 0.5: cv.set(x, y + 1, MOSS[0])
                break
    cv.outline(STONE[0])
    return cv


add('RockL', rock(20, 15, 5), (10, 2))
add('RockS', rock(11, 8, 8), (5, 1))


def stump():
    cv = Canvas(18, 14)
    for y in range(5, 13):
        for x in range(2, 16):
            u = (x - 9) / 7
            cv.set(x, y, ramp_pick(WOOD, 0.6 - 0.45 * u - (y - 5) * 0.02, x, y))
    cv.ellipse(9, 5, 7, 3.2, [WOOD[3], WOOD[4], STRAW[4]])
    for r in (2.0, 4.2):
        for a in range(24):
            ang = a / 24 * math.tau
            cv.set(int(9 + math.cos(ang) * r), int(5 + math.sin(ang) * r * 0.45), WOOD[2])
    cv.pattern(['w...........w.', '.w.........w..'], {'w': WOOD[1]}, 2, 12)
    cv.outline(WOOD[0])
    return cv


add('Stump', stump(), (9, 1))


def mushroom(cap):
    cv = Canvas(9, 9)
    cv.rect(3, 5, 3, 3, WHITE[2]); cv.rect(5, 5, 1, 3, WHITE[1])
    cv.ellipse(4.5, 4, 4.2, 3.2, cap)
    for (x, y) in ((3, 2), (6, 3), (2, 4)):
        if cv.opaque(x, y): cv.set(x, y, WHITE[3])
    cv.outline(INK[1])
    return cv


add('MushroomRed', mushroom(RED[1:]), (4, 0))
add('MushroomBrown', mushroom(WOOD[1:]), (4, 0))


# ------------------------------------------------------------------ трава, мелкий декор
def tuft(frame, tall=False):
    hgt = 11 if tall else 7
    cv = Canvas(9, hgt + 1)
    blades = [(1, hgt - 3, -1), (3, hgt, 0), (4, hgt - 1, 1), (6, hgt - 2, 1), (7, hgt - 4, 2)]
    lean = [-1, 0, 1][frame]
    for (bx, bh, bl) in blades:
        for i in range(bh):
            t = i / max(1, bh - 1)
            x = bx + int(round((bl * 0.3 + lean) * t * t * 1.6))
            y = hgt - i
            c = GRASS[3] if t < 0.35 else (GRASS[5] if t < 0.8 else GRASS[6])
            cv.set(x, y, c)
    for x in range(1, 8):
        cv.set(x, hgt, GRASS[2])
    return cv


for f in range(3):
    add('Tuft%d' % f, tuft(f), (4, 0))
for f in range(3):
    add('TallGrass%d' % f, tuft(f, True), (4, 0))


def tiny_flowers(col, seed):
    rnd = random.Random(seed)
    cv = Canvas(7, 6)
    for i in range(3):
        x, y = rnd.randrange(1, 6), rnd.randrange(1, 5)
        cv.set(x, y, col[-1]); cv.set(x + 1, y, col[-2]); cv.set(x, y + 1, col[-2])
        cv.set(x, y + 2, GRASS[4])
    return cv


add('TinyWhite', tiny_flowers(WHITE, 1), (3, 0))
add('TinyYellow', tiny_flowers(HONEY, 2), (3, 0))
add('TinyPink', tiny_flowers(PINK, 3), (3, 0))
add('TinyBlue', tiny_flowers(BLUE, 4), (3, 0))
clover = Canvas(7, 5); clover.pattern(['.gG.gG.', 'gGGgGGg', '.ggGgg.', '..gGg..', '...g...'], {'g': GRASS[4], 'G': GRASS[6]})
add('Clover', clover, (3, 0))
peb = Canvas(5, 3); peb.pattern(['.ss..', 'sSsss', '.sss.'], {'s': STONE[2], 'S': STONE[4]}); add('Pebbles', peb, (2, 0))


# ------------------------------------------------------------------ бабочка (тонируется vertex color)
def butterfly(frame):
    cv = Canvas(7, 6)
    if frame == 0:
        cv.pattern(['WW.WW..', 'WwW.wWW', 'WWWkWWW', '.WwkwW.', '.W.k.W.', '.......'],
                   {'W': (255, 255, 255, 255), 'w': (200, 200, 210, 255), 'k': INK[1]})
    else:
        cv.pattern(['.......', '.W.k.W.', 'WWWkWWW', '.WwkwW.', '..W.W..', '.......'],
                   {'W': (255, 255, 255, 255), 'w': (200, 200, 210, 255), 'k': INK[1]})
    return cv


add('ButterflyA', butterfly(0))
add('ButterflyB', butterfly(1))


# ------------------------------------------------------------------ тайлы земли и декали
def grass_tile(seed):
    rnd = random.Random(seed)
    cv = Canvas(16, 16)
    for y in range(16):
        for x in range(16):
            n = rnd.random()
            c = GRASS[3]
            if n < 0.18:
                c = GRASS[2]
            elif n > 0.9:
                c = GRASS[4]
            cv.set(x, y, c)
    for i in range(rnd.randrange(4, 8)):
        x, y = rnd.randrange(16), rnd.randrange(1, 16)
        cv.set(x, y, GRASS[2])
        cv.set(x, (y - 1) % 16, GRASS[5])
        if rnd.random() < 0.5:
            cv.set((x + 1) % 16, (y - 1) % 16, GRASS[4])
    return cv


for i in range(6):
    add('Grass%d' % i, grass_tile(100 + i), (0, 0))


def blob_decal(w, h, ramp, seed, edge_noise=0.18, base=0.5, alpha=255):
    rnd = random.Random(seed)
    cv = Canvas(w, h)
    lobes = [(rnd.uniform(0.3, 0.7) * w, rnd.uniform(0.35, 0.65) * h, rnd.uniform(0.25, 0.4) * w,
              rnd.uniform(0.25, 0.4) * h) for _ in range(5)]
    for y in range(h):
        for x in range(w):
            v = 0
            for (lx, ly, rx, ry) in lobes:
                d = ((x - lx) / rx) ** 2 + ((y - ly) / ry) ** 2
                v = max(v, 1 - d)
            v += (rnd.random() - 0.5) * edge_noise
            if v > 0.05 + dith(x, y) * 0.25:
                t = base + (rnd.random() - 0.5) * 0.35 + (0.12 if v > 0.5 else -0.1)
                c = ramp_pick(ramp, t, x, y)
                cv.set(x, y, (c[0], c[1], c[2], alpha))
    return cv


def clearing():
    w, h = 112, 76
    rnd = random.Random(77)
    cv = Canvas(w, h)
    for y in range(h):
        for x in range(w):
            u = (x + 0.5 - w / 2) / (w / 2)
            v = (y + 0.5 - h / 2) / (h / 2)
            d = u * u + v * v + (rnd.random() - 0.5) * 0.12
            if d < 0.86 + dith(x, y) * 0.14:
                t = 0.5 - 0.25 * u * 0.4 - v * 0.15 + (rnd.random() - 0.5) * 0.3
                if d > 0.78:
                    c = ramp_pick([GRASS[2], DIRT[1], DIRT[2]], t, x, y)
                else:
                    c = ramp_pick(DIRT[1:5], t, x, y)
                cv.set(x, y, c)
    for i in range(26):
        x, y = rnd.randrange(10, w - 10), rnd.randrange(8, h - 8)
        if cv.get(x, y)[:3] in [d[:3] for d in DIRT]:
            cv.set(x, y, STONE[3]); cv.set(x + 1, y, STONE[2]); cv.set(x, y + 1, DIRT[0])
    return cv


add('Clearing', clearing(), (56, 38))
add('PatchDark', blob_decal(48, 32, [GRASS[1], GRASS[2], GRASS[3]], 5, base=0.35), (24, 16))
add('PatchLight', blob_decal(44, 28, [GRASS[3], GRASS[4], GRASS[5]], 6, base=0.45), (22, 14))
add('PatchDirt', blob_decal(30, 20, [DIRT[1], DIRT[2], DIRT[3]], 7, base=0.5), (15, 10))
add('PatchMoss', blob_decal(36, 22, [GRASS[2], MOSS[0], MOSS[1], GRASS[4]], 8, base=0.55), (18, 11))


# ------------------------------------------------------------------ UI
def panel(dark=False, pressed=False):
    cv = Canvas(12, 12)
    fill = WOOD[1] if dark else STRAW[1]
    hi = WOOD[2] if dark else STRAW[2]
    border = INK[0]
    for y in range(12):
        for x in range(12):
            corner = (x in (0, 11) and y in (0, 11))
            if corner:
                continue
            edge = x in (0, 11) or y in (0, 11)
            if edge:
                cv.set(x, y, border)
            elif y == 1 and not pressed:
                cv.set(x, y, hi)
            elif y == 10 and not pressed:
                cv.set(x, y, WOOD[0])
            else:
                cv.set(x, y, fill)
    return cv


def button(state):
    # state 0 = обычная, 1 = нажата, 2 = заблокирована
    cv = Canvas(12, 12)
    ramp = {0: HONEY, 1: HONEY, 2: STONE}[state]
    for y in range(12):
        for x in range(12):
            if x in (0, 11) and y in (0, 11):
                continue
            if x in (0, 11) or y in (0, 11):
                cv.set(x, y, INK[0])
            elif state == 1:
                cv.set(x, y, ramp[2] if y > 2 else ramp[1])
            else:
                if y <= 2:
                    cv.set(x, y, ramp[4] if state == 0 else ramp[3])
                elif y >= 9:
                    cv.set(x, y, ramp[1])
                else:
                    cv.set(x, y, ramp[3] if state == 0 else ramp[2])
    return cv


add('Panel', panel())
add('PanelDark', panel(True))
add('Button', button(0))
add('ButtonDown', button(1))
add('ButtonOff', button(2))

ICON_MAP = {'k': INK[0], 'K': INK[1], 'y': HONEY[3], 'Y': HONEY[5], 'd': HONEY[2], 'w': WING[2], 'v': WING[1],
            'r': RED[2], 'R': RED[3], 'g': LEAF[4], 'G': LEAF[6], 's': STONE[3], 'S': STONE[4], 'o': ORANGE[3],
            'W': WHITE[3], 'b': BLUE[3], 'p': PINK[2]}
ICONS = {
    'IconBee': ['..vv.vv.', '.vwwvwwv', '..kkkkk.', '.kyKyKyk', 'kyyKyKkk', '.kyKyKk.', '..kkkk..', '........'],
    'IconHoney': ['...k....', '..kYk...', '..kyk...', '.kyYyk..', 'kyyYyyk.', 'kdyyyyk.', '.kddyk..', '..kkk...'],
    'IconNectar': ['.pp.pp..', 'pWWpWWp.', 'pWyyyWp.', '.pyYyp..', 'pWyyyWp.', 'pWWpWWp.', '.pp.pp..', '...g....'],
    'IconSpeed': ['.....ww.', '...wwvw.', '.wwvvw..', 'wwvvw...', '..wvw...', '.kkkkk..', 'kyKyKyk.', '.kkkkk..'],
    'IconCap': ['..kkkk..', '.kWWWWk.', 'kyyyyyyk', 'kyYYyyyk', 'kyYyyyyk', 'kdyyyydk', '.kddddk.', '..kkkk..'],
    'IconSting': ['.......S', '......Ss', '.....Ss.', '....Ss..', '.k.Ss...', '.kKs....', 'kKKk....', 'kkk.....'],
    'IconComb': ['.kkk.kk.', 'kYyykYyk', 'kyyykyyk', '.kkkdkk.', 'kYykkYyk', 'kyyykyyk', '.kkk.kk.', '........'],
    'IconSun': ['...y....', '.y.Y.y..', '..YYY...', 'yYYYYYy.', '..YYY...', '.y.Y.y..', '...y....', '........'],
    'IconPause': ['........', '.WW.WW..', '.WW.WW..', '.WW.WW..', '.WW.WW..', '.WW.WW..', '.WW.WW..', '........'],
    'IconCheck': ['........', '......G.', '.....GG.', 'G...GG..', 'GG.GG...', '.GGG....', '..G.....', '........'],
    'IconSkull': ['.WWWWW..', 'WWWWWWW.', 'WkkWkkW.', 'WkkWkkW.', 'WWWkWWW.', '.WWWWW..', '.W.W.W..', '........'],
    'IconArrow': ['...k....', '...kk...', 'kkkkWk..', 'kWWWWWk.', 'kkkkWk..', '...kk...', '...k....', '........'],
    'IconHeart': ['.rr.rr..', 'rRRrRRr.', 'rRrrrrr.', 'rrrrrrr.', '.rrrrr..', '..rrr...', '...r....', '........'],
    'IconStar': ['...y....', '...Y....', '..yYy...', 'yYYYYYy.', '.yYYYy..', '.yY.Yy..', 'yy...yy.', '........'],
    'IconStarEmpty': ['...K....', '...K....', '..KKK...', 'KKKKKKK.', '.KKKKK..', '.KK.KK..', 'KK...KK.', '........'],
    'IconGuard': ['.kkkkk..', 'kyYyYyk.', 'kYkkkYk.', 'kykYkyk.', 'kYkkkYk.', '.kyYyk..', '..kyk...', '...k....'],
    'IconWasp': ['..vv.vv.', '.vwwvwwv', '..kkkkk.', 'kokKokok', 'okKokKok', '.kkkkkk.', '..k..k..', '........'],
}
for name, rows in ICONS.items():
    cv = Canvas(8, 8)
    cv.pattern(rows, ICON_MAP)
    add(name, cv)


# ------------------------------------------------------------------ новые существа и объекты (сборка 5)
SOLDIER_MAP = dict(BEE_MAP)
SOLDIER_MAP.update({'y': HONEY[2], 'Y': HONEY[3], 'h': HONEY[4], 'd': HONEY[1], 'r': RED[2], 'R': RED[3]})
SOLDIER_BODY = [
    '..kkkkkkkkk..',
    '.kdYhKYhKrkk.',
    'skdyyKyyKRekk',
    'skddyKdyKrKKk',
    '.kddyKdyKKKk.',
    '..kkkkkkkkk..',
]


def make_soldier(up):
    cv = Canvas(13, 11)
    cv.pattern(SOLDIER_BODY, SOLDIER_MAP, 0, 3)
    wings = ['....vv.vv....', '...vWwvWwv...', '...vwwvwwv...'] if up else ['.............', '..vvv.vvv....', '.vWwwvWwwv...']
    cv.pattern(wings, SOLDIER_MAP, 0, 0)
    cv.pattern(['...k.k.k.k...', '..k.......k..'][0:1], SOLDIER_MAP, 0, 9)
    return cv


add('SoldierA', make_soldier(True))
add('SoldierB', make_soldier(False))


def bird(frame):
    # щурка: бирюзовое брюшко, рыжая спина, жёлтое горло, клюв — вид сбоку в полёте
    TEAL = [hx('12403f'), hx('1d6e6a'), hx('2ba39b'), hx('5fd1c4')]
    cv = Canvas(24, 14)
    body = Canvas(24, 14)
    body.ellipse(12, 8, 8.5, 3.2, TEAL)
    body.ellipse(17.5, 7, 3.2, 2.8, [ORANGE[1], ORANGE[2], ORANGE[3], ORANGE[4]])
    body.ellipse(19.5, 8.3, 1.6, 1.2, [HONEY[3], HONEY[4], HONEY[5]])
    for x in range(21, 24):
        body.set(x, 7 + (x - 21) // 2, INK[1])
    for x in range(1, 5):
        body.set(x, 9 + (x % 2), TEAL[1])
    if frame == 0:
        body.ellipse(11, 3.5, 5.5, 2.4, [WOOD[1], WOOD[2], ORANGE[2], ORANGE[3]])
    else:
        body.ellipse(11, 11.5, 5.5, 1.8, [WOOD[1], WOOD[2], ORANGE[2]])
    body.outline(INK[0])
    body.set(19, 6, INK[0])
    return body


add('BirdA', bird(0))
add('BirdB', bird(1))

spider = Canvas(11, 9)
spider.pattern(['k...k...k..', '.k.kkk.k...', '..kKKKk....', 'kkKrKKKkk..', '..kKKKk....', '.k.kkk.k...', 'k...k...k..'],
               {'k': INK[0], 'K': INK[2], 'r': RED[2]}, 0, 1)
add('Spider', spider)


def web():
    cv = Canvas(31, 31)
    c = 15
    W1 = (235, 240, 245, 210)
    W2 = (235, 240, 245, 120)
    for a in range(8):
        ang = a / 8 * math.tau + 0.2
        for r in range(1, 16):
            cv.set(int(round(c + math.cos(ang) * r)), int(round(c + math.sin(ang) * r)), W1)
    for ring in (4, 7, 10, 13):
        n = ring * 7
        for k in range(n):
            ang = k / n * math.tau
            x, y = int(round(c + math.cos(ang) * ring)), int(round(c + math.sin(ang) * ring))
            if not cv.opaque(x, y):
                cv.set(x, y, W2)
    return cv


add('Web', web())


def bear(frame, flash=False):
    BR = [hx('2b170f'), hx('4a2a1a'), hx('6e4128'), hx('945a36'), hx('b9784a')]
    cv = Canvas(34, 26)
    cv.ellipse(15, 15, 12, 8, BR)
    cv.ellipse(26.5, 11.5, 5.5, 5, BR)
    cv.ellipse(30.5, 12.5, 2.4, 1.9, [BR[3], WOOD[4], STRAW[4]])
    cv.ellipse(24.5, 7, 1.7, 1.7, BR[1:])
    cv.ellipse(28.5, 7, 1.7, 1.7, BR[1:])
    lift = 1 if frame else 0
    for lx in (7, 12, 19, 23):
        up = lift if lx in (7, 19) else 1 - lift
        cv.rect(lx, 20 - up, 3, 4, BR[1])
    cv.set(32, 12, INK[0])
    cv.outline(BR[0])
    cv.set(28, 10, INK[0])
    cv.set(28, 9, WHITE[3])
    if flash:
        for y in range(cv.h):
            for x in range(cv.w):
                if cv.opaque(x, y):
                    cv.set(x, y, (255, 255, 255, 255))
    return cv


add('BearA', bear(0))
add('BearB', bear(1))
add('BearFlash', bear(0, True))


def nest():
    P = [hx('4d4a45'), hx('726d63'), hx('9a948a'), hx('c4bdb1'), hx('e3ddd2')]
    cv = Canvas(24, 30)
    for y in range(3, 28):
        t = (y - 3) / 25
        half = 10 * math.sin(min(1.0, 0.18 + t * 1.05) * math.pi / 1.15) ** 0.8
        band = y % 3
        for x in range(int(12 - half), int(12 + half) + 1):
            u = (x + 0.5 - 12) / max(1.0, half)
            sh = 0.6 - 0.4 * u - 0.15 * t - (0.25 if band == 0 else 0)
            cv.set(x, y, ramp_pick(P, sh, x, y))
    for y in range(0, 4):
        cv.set(12, y, WOOD[1])
    cv.ellipse(12, 25, 2.6, 2.0, [INK[0], INK[1]])
    cv.outline(P[0])
    return cv


add('Nest', nest(), (12, 2))

def ant(frame):
    """Муравей сбоку, мельче пчелы: брюшко | талия | грудь | голова, коленчатый усик, 3 пары ног с шагом."""
    body = [
        '.........k..',
        '.kk.....k.k.',
        'kmhk.kk.kmhk',
        'kmmmkmhkmmmkk',
        '.kdmkkdkkdk.',
    ]
    legs = ['..k.k..k.k..', '.k..k..k..k.'] if frame == 0 else ['..k..k.k.k..', '...kk.k..kk.']
    cv = Canvas(13, 7)
    cv.pattern(body + legs, {'k': INK[0], 'm': HORNET[1], 'h': HORNET[3], 'd': RED[0]})
    return cv


for fr in range(2):
    add('Ant%s' % 'AB'[fr], ant(fr), (6, 6))


def linden(frame, dry=False):
    cv = Canvas(15, 13)
    rnd = random.Random(31 + frame)
    cv.ellipse(7.5, 5, 6, 3.5, LEAF[2:6])
    if not dry:
        for i in range(9):
            x, y = rnd.randrange(2, 13), rnd.randrange(2, 11)
            cv.set(x, y, HONEY[5]); cv.set(x + 1, y, WHITE[3]); cv.set(x, y + 1, HONEY[4])
    else:
        for i in range(4):
            x, y = rnd.randrange(3, 12), rnd.randrange(3, 9)
            cv.set(x, y, WOOD[3])
    for y in range(8, 13):
        cv.set(7 + (frame if y < 10 else 0), y, WOOD[2])
    cv.outline(LEAF[0])
    return cv


add('FlowerLindenA', linden(0), (7, 0))
add('FlowerLindenB', linden(1), (7, 0))
add('FlowerLindenDry', linden(0, True), (7, 0))


def pond():
    WATER = [hx('17344f'), hx('1f4f73'), hx('2f6f99'), hx('4b97bf'), hx('86c2de')]
    cv = Canvas(56, 38)
    rnd = random.Random(5)
    for y in range(38):
        for x in range(56):
            u = (x + 0.5 - 28) / 26
            v = (y + 0.5 - 19) / 16
            d = u * u + v * v + (rnd.random() - 0.5) * 0.06
            if d <= 1.0:
                if d > 0.82:
                    c = ramp_pick([DIRT[1], DIRT[2], GRASS[2]], 0.5 - u * 0.3, x, y)
                else:
                    c = ramp_pick(WATER, 0.25 + (-v) * 0.35 + (rnd.random() - 0.5) * 0.12, x, y)
                cv.set(x, y, c)
    for (x, y) in ((18, 12), (19, 12), (30, 17), (31, 17), (24, 23)):
        cv.set(x, y, WATER[4])
    for i in range(5):
        x, y = rnd.randrange(8, 48), rnd.randrange(6, 30)
        if 0.55 < ((x - 28) / 26) ** 2 + ((y - 19) / 16) ** 2 < 0.8:
            cv.set(x, y, LEAF[4]); cv.set(x + 1, y, LEAF[5])
    return cv


add('Pond', pond())

rain = Canvas(2, 5); rain.pattern(['.#', '.#', '#.', '#.', '#.'], {'#': (200, 225, 255, 200)}); add('RainDrop', rain)

ICONS2 = {
    'IconQueen': ['........', 'y.y.y.y.', 'yYyYyYy.', 'yYYYYYy.', 'kyyyyyk.', '.kkkkk..', '........', '........'],
    'IconMoon': ['..WWW...', '.WWw....', 'WWw.....', 'WWw.....', 'WWw.....', '.WWw....', '..WWW...', '........'],
    'IconRain': ['.ssss...', 'sSSSSs..', 'ssssss..', '.b.b.b..', 'b.b.b...', '.b.b.b..', '........', '........'],
    'IconWind': ['......w.', 'wwwwwvw.', '.....w..', '.wwwww..', '........', 'wwwwvw..', '....w...', '........'],
    'IconHeat': ['..rr....', '..rR....', '..rR....', '..rR....', '.rRRr...', '.rRRr...', '..rr....', '........'],
    'IconWater': ['...b....', '..bb....', '.bbbb...', '.bWbb...', 'bWbbbb..', '.bbbb...', '..bb....', '........'],
    'IconTrophy': ['yyyyyyy.', 'yYYYYYy.', '.yYYYy..', '..yYy...', '...y....', '..yyy...', '.kkkkk..', '........'],
    'IconLock': ['..sss...', '.s...s..', '.s...s..', 'kkkkkkk.', 'kSSSSSk.', 'kSSkSSk.', 'kkkkkkk.', '........'],
    'IconScout': ['..vv.vv.', '.vwwvwwv', '..kkkkk.', '.kyKyKyk', 'kyyKyKkk', '.kyKyKk.', '..kkkk..', '...rr...'],
    'IconSquad': ['.k...k..', 'kyk.kyk.', '.k...k..', '...k....', '..kyk...', '...k....', '.k...k..', 'kyk.kyk.'],
}
for name, rows in ICONS2.items():
    cv = Canvas(8, 8)
    cv.pattern(rows, ICON_MAP)
    add(name, cv)


# ------------------------------------------------------------------ частицы из Kenney Particle Pack (CC0)
# Исходники 512 px пикселизируются под стиль игры: обрезка по содержимому, уменьшение,
# альфа квантуется в 3 уровня упорядоченным дизерингом. Цвет даёт vertex color при отрисовке.
VENDOR = os.path.join(os.path.dirname(__file__), 'vendor', 'kenney')


def kenney(name, size):
    from PIL import ImageChops
    im = Image.open(os.path.join(VENDOR, name + '.png')).convert('RGBA')
    r, g, b, a = im.split()
    lum = Image.merge('RGB', (r, g, b)).convert('L')
    dens = ImageChops.multiply(lum, a)
    bbox = dens.point(lambda v: 255 if v > 20 else 0).getbbox()
    if bbox:
        dens = dens.crop(bbox)
    w, h = dens.size
    sc = size / max(w, h)
    dens = dens.resize((max(1, round(w * sc)), max(1, round(h * sc))), Image.LANCZOS)
    cv = Canvas(dens.width, dens.height)
    for y in range(dens.height):
        for x in range(dens.width):
            v = dens.getpixel((x, y)) / 255.0
            t = dith(x, y)
            if v > 0.62:
                cv.set(x, y, (255, 255, 255, 255))
            elif v > 0.3:
                cv.set(x, y, (255, 255, 255, 255 if v > 0.3 + t * 0.32 else 150))
            elif v > 0.08 + t * 0.22:
                cv.set(x, y, (255, 255, 255, 120))
    return cv


for nm, src, sz in (('KRing0', 'circle_02', 9), ('KRing1', 'circle_02', 13), ('KRing2', 'circle_02', 17),
                    ('KSpark', 'star_06', 9), ('KCross', 'star_08', 9), ('KStar', 'star_04', 11),
                    ('KSmokeA', 'smoke_07', 10), ('KSmokeB', 'smoke_08', 13), ('KSlash', 'slash_02', 15),
                    ('KTwirl', 'twirl_02', 13), ('KFlame', 'flame_03', 8), ('KBurst', 'muzzle_01', 15),
                    ('KTrace', 'trace_04', 11)):
    add(nm, kenney(src, sz))


# ------------------------------------------------------------------ логотип «РОЙ»
def logo(text='РОЙ', scale=5):
    gap = 2
    widths = [len(font.G[c][0]) for c in text]
    w = sum(widths) * scale + gap * scale * (len(text) - 1) + 6
    h = 7 * scale + 7
    cv = Canvas(w, h)
    x0 = 3
    mask = Canvas(w, h)
    for ci, c in enumerate(text):
        rows = font.G[c]
        for gy, row in enumerate(rows):
            for gx, ch in enumerate(row):
                if ch == '#':
                    for yy in range(scale):
                        for xx in range(scale):
                            mask.set(x0 + gx * scale + xx, 3 + gy * scale + yy, W)
        x0 += (widths[ci] + gap) * scale
    for y in range(h):
        for x in range(w):
            if mask.opaque(x, y):
                t = 1 - (y - 3) / (7 * scale)
                c = ramp_pick(HONEY[2:], 0.25 + t * 0.75, x, y)
                # сотовая фактура
                hxp = (x + (3 if (y // 5) % 2 else 0)) % 6
                if (y % 5 == 0 and hxp < 4) or hxp == 5:
                    c = HONEY[2] if t < 0.5 else HONEY[3]
                if not mask.opaque(x, y - 1):
                    c = HONEY[5]
                cv.set(x, y, c)
    cv.outline(HONEY[0])
    cv.outline(INK[0], diag=True)
    # тень вниз
    sh = Canvas(w, h)
    for y in range(h):
        for x in range(w):
            if cv.opaque(x, y - 2) and not cv.opaque(x, y):
                sh.set(x, y, INK[1])
    for y in range(h):
        for x in range(w):
            if sh.opaque(x, y):
                cv.set(x, y, sh.get(x, y))
    return cv


add('Logo', logo())


# ------------------------------------------------------------------ шрифт
GLYPHS = []
for ch in font.CHARS:
    rows = font.G[ch]
    cv = Canvas(len(rows[0]), 7)
    cv.pattern(rows, {'#': W})
    GLYPHS.append((ch, cv))


# ------------------------------------------------------------------ упаковка атласа
def pack(items, size=512, pad=1):
    order = sorted(range(len(items)), key=lambda i: (-items[i].h, -items[i].w))
    pos = [None] * len(items)
    x = y = 0
    shelf = 0
    for i in order:
        cv = items[i]
        if x + cv.w + pad > size:
            x = 0
            y += shelf + pad
            shelf = 0
        if y + cv.h > size:
            raise RuntimeError('atlas overflow')
        pos[i] = (x, y)
        x += cv.w + pad
        shelf = max(shelf, cv.h)
    return pos


def main():
    preview_dir = None
    if '--preview' in sys.argv:
        preview_dir = sys.argv[sys.argv.index('--preview') + 1]
    canv = [s[1] for s in SPRITES] + [g[1] for g in GLYPHS]
    size = 512
    pos = pack(canv, size)
    atlas = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    for cv, (x, y) in zip(canv, pos):
        atlas.alpha_composite(cv.image(), (x, y))
    os.makedirs(os.path.dirname(OUT_PNG), exist_ok=True)
    atlas.save(OUT_PNG)

    lines = []
    lines.append('// <auto-generated> tools/art/gen_art.py — не редактировать вручную </auto-generated>')
    lines.append('namespace Swarm.Presentation')
    lines.append('{')
    lines.append('    public enum Spr : short')
    lines.append('    {')
    for name, _, _ in SPRITES:
        lines.append('        %s,' % name)
    lines.append('        Count')
    lines.append('    }')
    lines.append('')
    lines.append('    public static class AtlasData')
    lines.append('    {')
    lines.append('        public const int Size = %d;' % size)
    lines.append('        public const int GlyphStart = %d;' % len(SPRITES))
    lines.append('        public const int FontHeight = 7;')
    lines.append('        // x, y (от низа атласа), w, h, pivotX, pivotY (пиксели от левого нижнего угла спрайта)')
    lines.append('        public static readonly short[] Rects =')
    lines.append('        {')
    all_items = [(n, c, p) for (n, c, p) in SPRITES] + [('glyph', c, (0, 0)) for (_, c) in GLYPHS]
    for (name, cv, piv), (x, y) in zip(all_items, pos):
        uy = size - y - cv.h
        if piv is None:
            pxv, pyv = cv.w // 2, cv.h // 2
        else:
            pxv, pyv = piv
        lines.append('            %d, %d, %d, %d, %d, %d, // %s' % (x, uy, cv.w, cv.h, pxv, pyv, name))
    lines.append('        };')
    chars = ''.join(c for c, _ in GLYPHS)
    esc = chars.replace('\\', '\\\\').replace('"', '\\"')
    lines.append('        public const string FontChars = "%s";' % esc)
    lines.append('    }')
    lines.append('}')
    os.makedirs(os.path.dirname(OUT_CS), exist_ok=True)
    with open(OUT_CS, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')
    print('sprites:', len(SPRITES), 'glyphs:', len(GLYPHS), 'atlas used height:',
          max(p[1] + c.h for c, p in zip(canv, pos)))

    if preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        atlas.resize((size * 2, size * 2), Image.NEAREST).save(os.path.join(preview_dir, 'atlas_x2.png'))
        sheet_w = 0
        tiles = []
        for name, cv, _ in SPRITES:
            tiles.append((name, cv.image()))
        bg = Image.new('RGBA', (640, 1100), GRASS[3])
        x = y = 4
        rowh = 0
        for name, im in tiles:
            if x + im.width + 4 > 640:
                x = 4; y += rowh + 4; rowh = 0
            bg.alpha_composite(im, (x, y))
            x += im.width + 4
            rowh = max(rowh, im.height)
        bg = bg.crop((0, 0, 640, y + rowh + 4))
        bg.resize((bg.width * 3, bg.height * 3), Image.NEAREST).save(os.path.join(preview_dir, 'sheet_x3.png'))
    return SPRITES, pos


if __name__ == '__main__':
    main()

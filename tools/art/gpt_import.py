"""Импорт картинок, сгенерированных в ChatGPT, в пиксель-арт игры.

GPT рисует «псевдо-пиксели»: клетки разного размера, сглаженные края, цвета мимо палитры.
Конвейер приводит их к нашему стилю:
  1. фон убирается по альфе (или заливкой от краёв для белого/чёрного фона);
  2. лист режется на кадры по прозрачным промежуткам (мелкие соринки — эффекты-штрихи — приклеиваются к ближайшему кадру);
  3. каждый кадр уменьшается до нужной высоты «голосованием» по клетке: берётся самый частый цвет палитры,
     прозрачность — если клетка в основном пустая (так края не мылятся, а мелкие детали не пропадают);
  4. цвета — строго из палитры игры; снаружи добавляется 1px тёмный контур, если край оказался светлым.
Кадры одного листа масштабируются ОДНИМ коэффициентом — размеры персонажа между кадрами не прыгают.
"""
import os
from collections import Counter

from PIL import Image

import palette as P

VENDOR = os.path.join(os.path.dirname(__file__), 'vendor', 'gpt')

PALETTE = []
for _n in dir(P):
    _v = getattr(P, _n)
    if isinstance(_v, list) and _v and isinstance(_v[0], tuple) and len(_v[0]) in (3, 4):
        for _c in _v:
            if _c[:3] not in PALETTE:
                PALETTE.append(_c[:3])
OUTLINE = P.INK[0][:3]


def _nearest(c, cache={}):
    if c in cache:
        return cache[c]
    r, g, b = c
    best, bd = None, 1e18
    for pr, pg, pb in PALETTE:
        # «красная средняя» — простая перцептивная метрика
        rm = (r + pr) / 2
        d = (2 + rm / 256) * (r - pr) ** 2 + 4 * (g - pg) ** 2 + (2 + (255 - rm) / 256) * (b - pb) ** 2
        if d < bd:
            bd, best = d, (pr, pg, pb)
    cache[c] = best
    return best


def load(name, bg=None):
    """RGBA; bg='white'/'black' — убрать сплошной фон заливкой от краёв."""
    im = Image.open(os.path.join(VENDOR, name)).convert('RGBA')
    if bg:
        tgt = (255, 255, 255) if bg == 'white' else (0, 0, 0)
        px = im.load()
        w, h = im.size
        seen = set()
        stack = [(x, y) for x in range(w) for y in (0, h - 1)] + [(x, y) for y in range(h) for x in (0, w - 1)]
        while stack:
            x, y = stack.pop()
            if (x, y) in seen or not (0 <= x < w and 0 <= y < h):
                continue
            seen.add((x, y))
            c = px[x, y]
            if c[3] < 30 or sum(abs(c[i] - tgt[i]) for i in range(3)) < 90:
                px[x, y] = (0, 0, 0, 0)
                stack.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    return im


def frames(im, count, alpha_cut=110):
    """Режет лист по прозрачным колонкам на count кадров (крупнейшие промежутки)."""
    w, h = im.size
    a = im.getchannel('A').point(lambda v: 255 if v > alpha_cut else 0)
    cols = [any(a.getpixel((x, y)) for y in range(0, h, 2)) for x in range(w)]
    segs, x = [], 0
    while x < w:
        if cols[x]:
            s = x
            while x < w and cols[x]:
                x += 1
            segs.append([s, x])
        x += 1
    # склеиваем сегменты, пока их больше count: по наименьшему промежутку
    while len(segs) > count:
        gaps = [(segs[i + 1][0] - segs[i][1], i) for i in range(len(segs) - 1)]
        _, i = min(gaps)
        segs[i] = [segs[i][0], segs[i + 1][1]]
        del segs[i + 1]
    out = []
    for s, e in segs:
        fr = im.crop((s, 0, e, h))
        bb = fr.getchannel('A').point(lambda v: 255 if v > alpha_cut else 0).getbbox()
        out.append(fr.crop(bb) if bb else fr)
    return out


def equal_frames(im, count, alpha_cut=110):
    """Режет лист на count равных по ширине кадров (когда кадры касаются друг друга)."""
    w, h = im.size
    out = []
    for i in range(count):
        fr = im.crop((i * w // count, 0, (i + 1) * w // count, h))
        bb = fr.getchannel('A').point(lambda v: 255 if v > alpha_cut else 0).getbbox()
        out.append(fr.crop(bb) if bb else fr)
    return out


def pixelize(im, scale, alpha_cut=110, outline=True):
    """Уменьшение голосованием по клеткам + палитра + контур. scale — во сколько раз уменьшить."""
    w, h = im.size
    tw, th = max(1, round(w / scale)), max(1, round(h / scale))
    src = im.load()
    out = Image.new('RGBA', (tw + 2, th + 2), (0, 0, 0, 0))   # +1px поля под контур
    o = out.load()
    for ty in range(th):
        y0, y1 = int(ty * h / th), max(int(ty * h / th) + 1, int((ty + 1) * h / th))
        for tx in range(tw):
            x0, x1 = int(tx * w / tw), max(int(tx * w / tw) + 1, int((tx + 1) * w / tw))
            cnt = Counter()
            total = opaque = 0
            for yy in range(y0, y1):
                for xx in range(x0, x1):
                    c = src[xx, yy]
                    total += 1
                    if c[3] > alpha_cut:
                        opaque += 1
                        cnt[_nearest(c[:3])] += 1
            if opaque * 2 >= total and cnt:
                o[tx + 1, ty + 1] = cnt.most_common(1)[0][0] + (255,)
    if outline:
        add = []
        for y in range(th + 2):
            for x in range(tw + 2):
                if o[x, y][3]:
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    xx, yy = x + dx, y + dy
                    if 0 <= xx < tw + 2 and 0 <= yy < th + 2 and o[xx, yy][3]:
                        r, g, b, _ = o[xx, yy]
                        if 0.3 * r + 0.59 * g + 0.11 * b > 70:   # край светлый — нужен контур
                            add.append((x, y))
                        break
        for x, y in add:
            o[x, y] = OUTLINE + (255,)
    bb = out.getbbox()
    return out.crop(bb) if bb else out


def quantize(im, alpha_cut=110):
    """Уже пиксельная картинка в натуральном размере — только палитра и бинарная альфа."""
    im = im.convert('RGBA')
    px = im.load()
    for y in range(im.size[1]):
        for x in range(im.size[0]):
            c = px[x, y]
            px[x, y] = _nearest(c[:3]) + (255,) if c[3] > alpha_cut else (0, 0, 0, 0)
    return im


def sheet_scale(frames_, target_h):
    """Общий коэффициент для листа: по самому высокому кадру."""
    return max(f.size[1] for f in frames_) / float(target_h)

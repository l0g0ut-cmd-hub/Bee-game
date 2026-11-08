"""Макет кадра из реальных спрайтов атласа (НЕ скриншот движка): проверка цельности арта."""
import math, random, sys, os
sys.path.insert(0, os.path.dirname(__file__))
from PIL import Image
import gen_art as g, font
spr = {n: c.image() for n, c, p in g.SPRITES}
piv = {n: (p if p else (c.w // 2, c.h // 2)) for n, c, p in g.SPRITES}
W, H = 216, 480
img = Image.new('RGBA', (W, H), g.GRASS[3])
def put(name, x, y, alpha=255, flip=False, layer=None):
    im = spr[name]
    if flip: im = im.transpose(Image.FLIP_LEFT_RIGHT)
    if alpha < 255:
        a = im.split()[3].point(lambda v: v * alpha // 255); im = im.copy(); im.putalpha(a)
    px, py = piv[name]
    if flip: px = im.width - px
    # y снизу вверх как в Unity
    img.alpha_composite(im, (int(round(x - px)), int(round(H - (y - py) - im.height))))
rnd = random.Random(3)
for ty in range(0, H, 16):
    for tx in range(0, W, 16):
        put('Grass%d' % rnd.choice([0,0,0,1,1,2,3,4,5]), tx + 8, ty + 8)
for i in range(5): put(rnd.choice(['PatchDark','PatchLight','PatchMoss']), rnd.randrange(W), rnd.randrange(H))
hive = (108, 250)
put('Clearing', hive[0], hive[1] + 4)
put('ShadowXL', hive[0] + 2, hive[1] + 1, 100)
for _ in range(60): put(rnd.choice(['TinyWhite','TinyYellow','Clover','Pebbles','TinyPink']), rnd.randrange(W), rnd.randrange(H))
stand = [('Hive', hive[0], hive[1])]
trees = [(30, 400, 'CanopyA'), (190, 120, 'CanopyB'), (200, 420, 'CanopyC')]
for (x, y, c) in trees: stand.append(('Trunk', x, y - 6))
for (x, y) in [(40, 150), (60, 165), (52, 140), (170, 330), (182, 316), (160, 300)]:
    stand.append((rnd.choice(['FlowerDaisyA','FlowerPoppyA','FlowerCornA','FlowerVioletA','FlowerPinkA','FlowerSunA']), x, y))
stand.append(('FlowerDaisyDry', 70, 150))
for _ in range(45):
    x, y = rnd.randrange(W), rnd.randrange(H)
    if abs(x - hive[0]) < 40 and abs(y - hive[1]) < 30: continue
    stand.append((rnd.choice(['Tuft0','Tuft1','Tuft2','TallGrass1']), x, y))
stand += [('RockL', 150, 200), ('BushB', 20, 260), ('MushroomRed', 120, 90), ('Stump', 90, 360)]
for (x, y, c) in trees: put('ShadowXL', x + 4, y - 4, 80)
for n, x, y in sorted(stand, key=lambda s: -s[2]): put(n, x, y)
# рой у цветов
cx, cy = 62, 170
for i in range(42):
    k = math.sqrt((i + .5) / 42); a = i * 2.39996
    x = cx + math.cos(a) * 20 * k + rnd.uniform(-2, 2); y = cy + math.sin(a) * 16 * k + rnd.uniform(-2, 2)
    put('ShadowS', x, y - 6, 60)
    put(rnd.choice(['BeeA','BeeB','BeeLoadA']), x, y, flip=rnd.random() < .4)
# оса, облепленная пчёлами, у улья
put('ShadowM', 140, 262, 70); put('WaspA', 140, 270, flip=True)
for i in range(7):
    a = i / 7 * 6.28; put('BeeA', 140 + math.cos(a) * 8, 270 + math.sin(a) * 7, flip=math.cos(a) > 0)
put('ShadowL', 175, 40, 70); put('HornetA', 175, 50, flip=True)
for x, y in [(100, 268), (116, 272), (108, 280)]: put('BeeB', x, y)
for (x, y, c) in trees: put(c, x, y + 6, 255 if c != 'CanopyA' else 110)
put('ButterflyA', 150, 380)
# HUD
def text(s, x, y, col):
    for ch in s:
        rows = font.G.get(ch, font.G['?'])
        for gy, r in enumerate(rows):
            for gx, c in enumerate(r):
                if c == '#':
                    img.putpixel((x + gx, H - y - 7 + gy + 1), (23, 18, 28, 255))
                    img.putpixel((x + gx, H - y - 7 + gy), col)
        x += len(rows[0]) + 1
bar = Image.new('RGBA', (W, 22), g.WOOD[1]); img.alpha_composite(bar, (0, 0))
for x in range(W): img.putpixel((x, 21), g.INK[0]); img.putpixel((x, 1), g.WOOD[2])
put('IconBee', 10, H - 11); text('49/65', 17, H - 14, (255, 250, 240, 255))
put('IconHoney', 82, H - 11); text('137', 89, H - 14, g.HONEY[4])
put('IconSun', 134, H - 11); text('6:12', 141, H - 14, (255, 250, 240, 255))
put('IconPause', 205, H - 11)
ov = Image.new('RGBA', (W, 34), (23, 18, 28, 140)); img.alpha_composite(ov, (0, 140))
text('ОСЫ!', 92, H - 165, g.HONEY[4])
text('ОСЫ ГРАБЯТ УЛЕЙ, ЕСЛИ РОЙ ДАЛЕКО', 10, H - 172, (255, 250, 240, 255))
out = sys.argv[1]
img.save(out.replace('.png', '_1x.png'))
img.resize((W * 3, H * 3), Image.NEAREST).save(out)

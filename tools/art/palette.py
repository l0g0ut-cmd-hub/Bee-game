"""Единая палитра проекта SWARM. Все ассеты рисуются только из неё."""


def hx(s, a=255):
    s = s.lstrip('#')
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), a)


CLEAR = (0, 0, 0, 0)

# Трава / земля
GRASS = [hx('1c3626'), hx('274a2f'), hx('326035'), hx('3f773b'), hx('558f40'), hx('73ab4b'), hx('9cc95c')]
DIRT = [hx('3d2722'), hx('5a3a2c'), hx('7a5136'), hx('9b6c45'), hx('bf915e'), hx('d9b680')]
MOSS = [hx('3a5a2a'), hx('56783a')]

# Мёд / жёлтый
HONEY = [hx('5e3410'), hx('8f5216'), hx('c97a1f'), hx('eba634'), hx('f7cf55'), hx('fff0a3')]
INK = [hx('17121c'), hx('2c2232'), hx('463848')]
WING = [hx('d9eeff', 200), hx('9dbfd9', 210), hx('ffffff', 230)]

RED = [hx('4f1622'), hx('8c2431'), hx('c93b3b'), hx('ee6a52'), hx('ff9c7a')]
BLUE = [hx('1f2f5c'), hx('2f53a3'), hx('4f86d9'), hx('86bdf5'), hx('c4e4ff')]
PURPLE = [hx('331d4a'), hx('5b3380'), hx('8c55b8'), hx('c08ae0'), hx('e6c4f5')]
PINK = [hx('8a3462'), hx('c95488'), hx('ef86b0'), hx('ffc2da')]
WHITE = [hx('8e8577'), hx('c4baa8'), hx('ece4d2'), hx('fffaf0')]
ORANGE = [hx('4d220c'), hx('8a3f12'), hx('cf6a1c'), hx('f29a35'), hx('ffc56b')]
HORNET = [hx('3b1209'), hx('6e2412'), hx('a3401c'), hx('d36a2a')]
STONE = [hx('2b2e3b'), hx('454a5c'), hx('666d80'), hx('8d95a6'), hx('b8bfcc')]
WOOD = [hx('2e1a12'), hx('4a2c1b'), hx('6b4227'), hx('8f5e37'), hx('b07e4c')]
LEAF = [hx('11261a'), hx('1b3b22'), hx('285a2c'), hx('3a7a35'), hx('549c42'), hx('7cbf55'), hx('a8dd73')]
STRAW = [hx('4a2a14'), hx('77461d'), hx('a66a2a'), hx('cc923c'), hx('e8bb5c'), hx('f7dc8a')]

SHADOW = (12, 24, 18, 255)  # рисуется полупрозрачным через vertex color

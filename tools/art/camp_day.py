"""Картинка турбазы «Тихая заводь» в теме «Летний день».

Рисует сцену кодом (Pillow) и сохраняет PNG для игры:
    python tools/art/camp_day.py [путь.png]
По умолчанию — client/Assets/_Game/Resources/Art/CampDay.png; рядом кладётся CampBoat.png — лодка на прозрачном фоне.
Облака, дым, лодку, людей и блики на воде рисует и двигает игра (LakeHud.CampLife.cs), в картинке их нет.

Координаты — как в макете: сцена 390×844 (портретный телефон). Вокруг неё есть запас, чтобы картинка закрывала
и узкие высокие экраны, и планшеты: по 125 слева и справа, 160 сверху (небо), 96 снизу (трава).
Размеры и запас повторены в игре: LakeHud.Camp.cs (CampArt*).
"""
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFont

SCENE_W, SCENE_H = 390, 844
PAD_X, PAD_TOP, PAD_BOTTOM = 125, 160, 96
SCALE = 3          # пикселей картинки на единицу сцены
SUPER = 2          # сглаживание: рисуем крупнее и уменьшаем
K = SCALE * SUPER
W = (SCENE_W + 2 * PAD_X) * K
H = (SCENE_H + PAD_TOP + PAD_BOTTOM) * K
X0, X1 = -PAD_X, SCENE_W + PAD_X
Y0, Y1 = -PAD_TOP, SCENE_H + PAD_BOTTOM

rng = random.Random(7)
img = Image.new("RGB", (W, H), "#58B4EE")
draw = ImageDraw.Draw(img, "RGBA")


def px(x, y):
    return ((x + PAD_X) * K, (y + PAD_TOP) * K)


def rgba(color, alpha=1.0):
    color = color.lstrip("#")
    return (int(color[0:2], 16), int(color[2:4], 16), int(color[4:6], 16), round(255 * alpha))


def mix(a, b, t):
    ca, cb = rgba(a), rgba(b)
    return tuple(round(ca[i] + (cb[i] - ca[i]) * t) for i in range(3))


def gradient(y_from, y_to, stops, mask_poly=None):
    """Вертикальный градиент на всю ширину; stops — [(доля, цвет)]. mask_poly — рисовать только внутри многоугольника."""
    top, bottom = round((y_from + PAD_TOP) * K), round((y_to + PAD_TOP) * K)
    layer = Image.new("RGB", (W, bottom - top))
    d = ImageDraw.Draw(layer)
    for row in range(bottom - top):
        t = row / max(1, bottom - top - 1)
        for i in range(len(stops) - 1):
            if stops[i][0] <= t <= stops[i + 1][0]:
                local = (t - stops[i][0]) / max(1e-6, stops[i + 1][0] - stops[i][0])
                d.line([(0, row), (W, row)], fill=mix(stops[i][1], stops[i + 1][1], local))
                break
    if mask_poly is None:
        img.paste(layer, (0, top))
        return
    mask = Image.new("L", (W, bottom - top), 0)
    ImageDraw.Draw(mask).polygon([(x, y - top) for x, y in (px(*p) for p in mask_poly)], fill=255)
    img.paste(layer, (0, top), mask)


def poly(points, color, alpha=1.0):
    draw.polygon([px(*p) for p in points], fill=rgba(color, alpha))


def rect(x, y, w, h, color, alpha=1.0, radius=0):
    box = [px(x, y), px(x + w, y + h)]
    if radius > 0:
        draw.rounded_rectangle(box, radius=radius * K, fill=rgba(color, alpha))
    else:
        draw.rectangle(box, fill=rgba(color, alpha))


def ellipse(cx, cy, rx, ry, color, alpha=1.0):
    draw.ellipse([px(cx - rx, cy - ry), px(cx + rx, cy + ry)], fill=rgba(color, alpha))


def line(points, color, width, alpha=1.0):
    pts = [px(*p) for p in points]
    w = max(1, round(width * K))
    draw.line(pts, fill=rgba(color, alpha), width=w, joint="curve")
    r = w / 2
    for x, y in (pts[0], pts[-1]):
        draw.ellipse([x - r, y - r, x + r, y + r], fill=rgba(color, alpha))


def cubic(p0, p1, p2, p3, steps=40):
    out = []
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        out.append((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                    u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))
    return out


def ridge(func, step=4):
    return [(x, func(x)) for x in range(X0, X1 + step, step)]


def spruce(x, base, height, color, dark):
    """Ель: три яруса-треугольника, тёмная половина справа."""
    w = height * 0.42
    tiers = 3
    for i in range(tiers):
        t0 = i / tiers
        top = base - height * min(1.0, t0 + 0.55)
        bottom = base - height * t0 * 0.8
        half = w * (1 - t0 * 0.6) / 2
        poly([(x - half, bottom), (x + half, bottom), (x, top)], color)
        poly([(x, bottom), (x + half, bottom), (x, top)], dark)
    rect(x - height * 0.03, base - 2, height * 0.06, 6, "#5E4028")


def bush(x, y, r, color, dark):
    ellipse(x, y, r, r * 0.7, dark)
    ellipse(x - r * 0.35, y - r * 0.25, r * 0.7, r * 0.55, color)
    ellipse(x + r * 0.4, y - r * 0.15, r * 0.55, r * 0.45, color)


def shadow(cx, cy, rx, ry):
    ellipse(cx, cy, rx, ry, "#1D4A24", 0.28)


FONT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "client", "Assets", "_Game", "Resources", "Fonts",
                    "Manrope-Bold.ttf")


def text(cx, cy, label, size, color):
    """Надпись на вывеске: середина в (cx, cy), высота букв size единиц сцены."""
    font = ImageFont.truetype(FONT, max(6, round(size * K)))
    draw.text(px(cx, cy), label, font=font, fill=rgba(color), anchor="mm")


class Local:
    """Дом в своих координатах — как на листе дизайна «Дома турбазы», вариант А: (x, y) → (ox + k·x, oy + k·y)."""

    def __init__(self, ox, oy, k):
        self.ox, self.oy, self.k = ox, oy, k

    def p(self, x, y):
        return (self.ox + self.k * x, self.oy + self.k * y)

    def rect(self, x, y, w, h, color, alpha=1.0, radius=0):
        rect(self.ox + self.k * x, self.oy + self.k * y, self.k * w, self.k * h, color, alpha, radius * self.k)

    def poly(self, points, color, alpha=1.0):
        poly([self.p(*q) for q in points], color, alpha)

    def line(self, points, color, width, alpha=1.0):
        line([self.p(*q) for q in points], color, width * self.k, alpha)

    def ellipse(self, cx, cy, rx, ry, color, alpha=1.0):
        x, y = self.p(cx, cy)
        ellipse(x, y, rx * self.k, ry * self.k, color, alpha)

    def text(self, cx, cy, label, size, color):
        x, y = self.p(cx, cy)
        text(x, y, label, size * self.k, color)

    def ring(self, cx, cy, r, wall):
        """Спасательный круг: красный с четырьмя белыми вставками; wall — цвет стены в отверстии."""
        self.ellipse(cx, cy, r, r, "#E5532F")
        self.ellipse(cx, cy, r * 0.48, r * 0.48, wall)
        for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
            self.ellipse(cx + dx * r * 0.74, cy + dy * r * 0.74, r * 0.28, r * 0.28, "#FFFFFF")


# ---------- Небо ----------
gradient(Y0, 270, [(0, "#3E9FE6"), (0.37, "#58B4EE"), (1, "#CDEBFA")])
ellipse(318, 128, 44, 44, "#FFFBE0", 0.28)
ellipse(318, 128, 26, 26, "#FFFBE0")
# Облака рисует игра: они плывут (LakeHud.CampLife.cs).

# ---------- Дальний берег ----------
hills = ridge(lambda x: 226 - 15 * math.sin(x * 0.028 + 1.0) - 8 * math.sin(x * 0.061 + 0.4))
poly(hills + [(X1, 270), (X0, 270)], "#78BE8C")
treeline = []
x = X0
while x <= X1 + 14:
    treeline.append((x, 252))
    treeline.append((x + 6.5, 234 + rng.random() * 12))
    x += 13
poly(treeline + [(X1 + 14, 272), (X0, 272)], "#2F7D4F")

# ---------- Озеро ----------
gradient(266, 420, [(0, "#6CC4E6"), (0.5, "#2A8EBF"), (1, "#14608B")])
reflection = [(p[0], 270 + (252 - p[1]) * 0.9 + 4) for p in treeline]
poly([(X0, 268)] + reflection + [(X1 + 14, 268)], "#2F7D4F", 0.3)
for cx, cy, rx, a in [(300, 292, 34, .85), (120, 312, 26, .55), (318, 330, 40, .5), (90, 356, 30, .35),
                      (-60, 300, 30, .5), (450, 316, 34, .5), (220, 372, 28, .3), (-30, 346, 24, .3), (470, 364, 26, .3)]:
    ellipse(cx, cy, rx, 2.4, "#FFFFFF", a)

# ---------- Берег и трава ----------
def shore(x):
    return 398 - 7 * math.sin(x * 0.021 + 0.6) - 3 * math.sin(x * 0.05)


land_top = ridge(shore)
gradient(380, Y1, [(0, "#7CC45C"), (0.36, "#4E9E46"), (0.82, "#2C6B34"), (1, "#245A2C")],
         land_top + [(X1, Y1), (X0, Y1)])
line([(x, y + 1.5) for x, y in land_top], "#E6D9A0", 3.2, 0.75)

# Тропинка: от причала к дверям домов, в обход самих домов. Те же точки — у прохожих в игре
# (LakeHud.CampLife.cs, CampWaypoints); остановки — причал, магазин, рынок, домик, мастерская.
ROAD = [(150, 412), (158, 470), (196, 540), (276, 552), (214, 556), (174, 572), (170, 612), (118, 622),
        (150, 656), (200, 690), (222, 744), (307, 758), (230, 772), (150, 790), (80, 788)]


def catmull(points, steps=12):
    out = []
    n = len(points)
    for i in range(n - 1):
        p0, p1, p2, p3 = points[max(i - 1, 0)], points[i], points[i + 1], points[min(i + 2, n - 1)]
        for k in range(steps):
            t = k / steps
            out.append(tuple(0.5 * (2 * p1[a] + (p2[a] - p0[a]) * t + (2 * p0[a] - 5 * p1[a] + 4 * p2[a] - p3[a]) * t * t
                                    + (3 * p1[a] - p0[a] - 3 * p2[a] + p3[a]) * t ** 3) for a in (0, 1)))
    out.append(points[-1])
    return out


road = catmull(ROAD)
line(road, "#CDBB86", 17, 0.55)
line(road, "#EAD9A6", 13, 0.95)

# Цветы в траве — до домов, чтобы не оказаться на стенах.
for _ in range(70):
    x = rng.uniform(X0 + 20, X1 - 20)
    y = rng.uniform(440, Y1 - 10)
    ellipse(x, y, 2.2, 2.2, rng.choice(["#FFE27A", "#FFFFFF", "#FFFFFF", "#FFB3C7"]), 0.95)

# ---------- Причал и лодка ----------
poly([(138, 412), (168, 405), (216, 330), (196, 326)], "#8A6240")
for t in (0.15, 0.32, 0.49, 0.66, 0.83):
    a = (138 + (196 - 138) * t, 412 + (326 - 412) * t)
    b = (168 + (216 - 168) * t, 405 + (330 - 405) * t)
    line([a, b], "#6E4C30", 1.1)
rect(160, 374, 5, 34, "#5E4028")
rect(187, 338, 5, 34, "#5E4028")
rect(208, 322, 5, 20, "#5E4028")
# Лодку рисует игра отдельной картинкой (см. конец файла): она качается.

# ---------- Лодочная станция: будка проката на берегу, лодки у мостков и на траве ----------
def rowboat_parts(cx, cy, w, color, dark, light):
    """Гребная лодка сбоку: борт, тёмное днище, светлый планширь, банки и весло. cy — линия воды."""
    h = w * 0.26
    hull = cubic((cx - w / 2, cy - h), (cx - w * 0.30, cy + h * 0.55), (cx + w * 0.30, cy + h * 0.55), (cx + w / 2, cy - h), 20)
    # Тёмный низ борта: дуга внутри корпуса, сходится с ним у днища.
    keel = cubic((cx - w * 0.33, cy - h * 0.25), (cx - w * 0.22, cy + h * 0.30), (cx + w * 0.22, cy + h * 0.30), (cx + w * 0.33, cy - h * 0.25), 14)
    return [
        ("poly", hull, color),
        ("poly", keel, dark),
        ("line", [(cx - w / 2, cy - h), (cx + w / 2, cy - h)], light, w * 0.05),
        ("line", [(cx - w * 0.16, cy - h), (cx - w * 0.16, cy - h * 0.45)], dark, w * 0.035),
        ("line", [(cx + w * 0.16, cy - h), (cx + w * 0.16, cy - h * 0.45)], dark, w * 0.035),
        ("line", [(cx - w * 0.05, cy - h * 1.25), (cx + w * 0.42, cy + h * 0.25)], "#8A6240", w * 0.035),
        ("line", [(cx + w * 0.36, cy + h * 0.02), (cx + w * 0.46, cy + h * 0.36)], "#C89A62", w * 0.07),
    ]


def rowboat(cx, cy, w, color, dark, light):
    ellipse(cx, cy + w * 0.10, w * 0.52, w * 0.07, "#0D4F74", 0.35)
    for kind, points, paint, *width in rowboat_parts(cx, cy, w, color, dark, light):
        if kind == "poly":
            poly(points, paint)
        else:
            line(points, paint, width[0])


# Мостки проката и лодки на воде.
rect(262, 370, 8, 30, "#8A6240")
for y in (376, 383, 390):
    line([(262, y), (270, y)], "#6E4C30", 1)
rect(260, 368, 3, 12, "#5E4028")
rect(269, 368, 3, 12, "#5E4028")
rowboat(242, 384, 34, "#F4EDE0", "#C4473A", "#FFFFFF")
rowboat(291, 381, 32, "#E8B93A", "#B8892A", "#FBE9A8")
text(232, 379.5, "12", 4.6, "#C4473A")
text(282, 376.8, "07", 4.4, "#7A5A16")
# Перевёрнутая лодка сушится на траве.
shadow(252, 420, 26, 4)
rect(236, 416, 5, 4, "#5E4028")
rect(264, 416, 5, 4, "#5E4028")
poly(cubic((228, 417), (236, 400), (268, 400), (276, 417), 20), "#3F9A5A")
poly(cubic((240, 409), (246, 402), (262, 402), (268, 410), 14) + [(252, 406)], "#57B56F", 0.9)
line([(230, 416.5), (274, 416.5)], "#2F7A45", 1.6)
# Будка проката: белый верх, синий низ, шиферная крыша, окошко кассы, спасательный круг и табличка.
shadow(329, 426.5, 30, 5)
P = Local(268.2, 376.4, 0.62)
P.rect(62, 28, 72, 52, "#FFFFFF")
P.rect(62, 56, 72, 24, "#3F86C6")
P.poly([(54, 30), (98, 6), (142, 30)], "#A9B1B5")
P.poly([(98, 6), (142, 30), (98, 30)], "#949DA2")
P.rect(54, 28, 88, 4, "#8B9499")
P.rect(70, 36, 30, 16, "#2B3A42", radius=1.5)
P.rect(68, 52, 34, 3, "#C9A06B")
P.ring(118, 44, 9, "#FFFFFF")
P.rect(66, 61, 64, 14, "#FFFFFF", radius=2)
P.text(98, 67.6, "ПРОКАТ", 10.5, "#2D6CA8")
line([(304, 424), (303, 394)], "#8A6240", 1.3)
ellipse(302.8, 392, 1.9, 4.2, "#C89A62")
line([(355, 425), (357, 396)], "#8A6240", 1.3)
ellipse(357.2, 394, 1.9, 4.2, "#C89A62")

# ---------- Дома: советская турбаза (вариант А с листа «Дома турбазы») ----------
# Щитовые домики семидесятых: крашеная вагонка, серый шифер, белые рамы, тёмно-зелёные двери и цоколь.
SLATE, SLATE_DARK, EAVE = "#A9B1B5", "#949DA2", "#8B9499"
GLASS, WHITE, GREEN = "#BFE3F5", "#FFFFFF", "#3F6B4F"

# Флагшток у магазина.
rect(361, 452, 2, 90, "#DDE3E6")
poly([(363, 454), (382, 459), (363, 465)], "#E5532F")

# Магазин снастей: голубая вагонка, веранда в мелкую расстекловку, круглое чердачное окно.
shadow(276, 538.5, 88, 7)
S = Local(192.3, 438.8, 0.62)
S.rect(10, 146, 250, 14, "#5E6B63")
S.rect(10, 66, 250, 80, "#7FB7D9")
for y in (78, 90, 102, 114, 126, 138):
    S.line([(16, y), (254, y)], "#6CA6CB", 1.5)
S.rect(10, 66, 6, 80, WHITE)
S.rect(254, 66, 6, 80, WHITE)
S.poly([(-4, 70), (135, 10), (274, 70)], SLATE)
S.poly([(135, 10), (274, 70), (135, 70)], SLATE_DARK)
S.rect(-4, 66, 278, 6, EAVE)
S.ellipse(135, 46, 12, 12, WHITE)
S.ellipse(135, 46, 8.5, 8.5, "#EAF3F8")
S.line([(135, 38), (135, 54)], WHITE, 2)
S.line([(127, 46), (143, 46)], WHITE, 2)
S.rect(24, 84, 96, 62, "#F3E9D2")
S.rect(30, 90, 84, 34, GLASS)
for x in (44, 58, 72, 86, 100):
    S.line([(x, 90), (x, 124)], WHITE, 2)
for y in (101, 112):
    S.line([(30, y), (114, y)], WHITE, 2)
S.rect(142, 92, 34, 54, GREEN, radius=2)
S.rect(148, 98, 22, 16, GLASS)
S.ellipse(170, 122, 2.2, 2.2, "#F2D06B")
S.rect(136, 146, 46, 6, "#C9BFA8")
S.rect(130, 152, 58, 8, "#B8AE98")
S.rect(196, 90, 44, 36, WHITE)
S.rect(200, 94, 36, 28, GLASS)
S.line([(218, 94), (218, 122)], WHITE, 3)
S.line([(200, 106), (236, 106)], WHITE, 3)
S.rect(194, 126, 48, 4, WHITE)

# Рынок: шиферный навес на крашеных стойках, зелёный прилавок, ящик рыбы, ведро и весы.
shadow(102, 602.5, 70, 6)
M = Local(37.6, 529.2, 0.56)
for x in (10, 112, 214):
    M.rect(x, 40, 6, 90, "#6E7C74")
M.poly([(-6, 40), (236, 40), (224, 14), (6, 14)], SLATE)
for x in range(18, 224, 12):
    M.line([(x + (x - 115) * 0.05, 16), (x + (x - 115) * 0.10, 39)], "#98A1A6", 1.2)
M.rect(-6, 40, 242, 6, EAVE)
M.rect(66, 17, 98, 21, GREEN, radius=2)
M.text(115, 27, "РЫНОК", 14.5, WHITE)
M.rect(16, 84, 198, 12, "#C9A06B")
M.rect(16, 96, 198, 34, "#4E8A63")
for x in (40, 64, 88, 112, 136, 160, 184):
    M.line([(x, 97), (x, 129)], "#437856", 2)
M.rect(28, 70, 48, 14, "#B98654")
M.ellipse(42, 70, 11, 3.5, "#DCE8EF")
M.ellipse(60, 71, 11, 3.5, "#C9D8E2")
M.rect(94, 68, 18, 16, "#8FA3AE", radius=2)
M.ellipse(103, 68, 9, 3, "#B7C7D0")
M.rect(150, 68, 34, 16, "#3F86C6", radius=3)
M.ellipse(167, 60, 11.5, 11.5, "#3F86C6")
M.ellipse(167, 60, 8.5, 8.5, WHITE)
M.line([(167, 60), (172, 54)], "#E5532F", 2)

# Доска объявлений: зелёная рама, красный козырёк, листки на кнопках.
shadow(208, 624.5, 28, 3.5)
B = Local(181.9, 566.0, 0.58)
B.rect(12, 30, 6, 70, "#8A6240")
B.rect(72, 30, 6, 70, "#8A6240")
B.rect(4, 22, 82, 52, GREEN, radius=2)
B.rect(9, 27, 72, 42, "#EFE6CF")
B.poly([(0, 24), (45, 6), (90, 24)], "#E5532F")
B.poly([(45, 6), (90, 24), (45, 24)], "#C8431F")
B.rect(14, 32, 22, 30, WHITE)
B.rect(40, 32, 18, 14, "#FFE27A")
B.rect(40, 50, 36, 14, WHITE)
B.rect(62, 32, 14, 14, "#CFE8F6")
for x, y in ((25, 35), (49, 35), (58, 53)):
    B.ellipse(x, y, 2, 2, "#E5532F")

# Домик: мятная вагонка на блоках, номер у двери, телеантенна и труба печки (дым рисует игра).
shadow(296, 748.5, 78, 7)
C = Local(225.2, 676.0, 0.60)
for x in (30, 110, 190):
    C.rect(x, 112, 18, 8, "#9AA3A8")
C.line([(190, 40), (190, 4)], "#5E6B63", 2)
C.line([(180, 8), (200, 8)], "#5E6B63", 2)
C.line([(183, 15), (197, 15)], "#5E6B63", 2)
C.rect(56, 14, 7, 28, "#7E8A90")
C.rect(53, 11, 13, 4, "#5E6B63")
C.rect(20, 52, 196, 60, "#9CCFB0")
for x in range(34, 216, 14):
    C.line([(x, 57), (x, 111)], "#88BD9D", 1.5)
C.poly([(8, 56), (118, 14), (228, 56)], SLATE)
C.poly([(118, 14), (228, 56), (118, 56)], SLATE_DARK)
C.rect(8, 52, 220, 5, EAVE)
C.poly([(34, 66), (76, 66), (72, 58), (38, 58)], SLATE_DARK)
C.rect(38, 64, 34, 48, WHITE)
C.rect(40, 66, 30, 46, "#F3E9D2")
C.ellipse(64, 90, 2.2, 2.2, "#5E6B63")
C.rect(78, 69, 19, 14, "#2D5F8B", radius=2)
C.text(87.5, 75.6, "7", 11, WHITE)
C.rect(34, 112, 42, 5, "#C9A06B")
C.rect(118, 66, 64, 34, WHITE)
C.rect(122, 70, 56, 26, GLASS)
C.poly([(122, 70), (142, 70), (122, 94)], WHITE, 0.85)
C.poly([(178, 70), (158, 70), (178, 94)], WHITE, 0.85)
C.line([(150, 70), (150, 96)], WHITE, 3)
C.rect(116, 100, 68, 4, WHITE)

# Мастерская: силикатный кирпич, зелёные ворота «ёлочкой», фонарь, окно с решёткой-«солнышком», покрышка.
shadow(96, 774.5, 66, 6)
Wk = Local(34.4, 706.8, 0.56)
Wk.rect(10, 40, 200, 80, "#D6CDB6")
for y in (56, 72, 88, 104):
    Wk.line([(11, y), (209, y)], "#C2B8A0", 1.5)
Wk.rect(4, 32, 212, 10, "#6E7C74")
Wk.rect(34, 54, 100, 66, GREEN)
Wk.line([(84, 55), (84, 119)], "#2F5540", 3)
for y in (58, 74, 90, 106):
    Wk.line([(38, y + 12), (80, y)], "#35604A", 2)
    Wk.line([(88, y), (130, y + 12)], "#35604A", 2)
for x, y in ((34, 62), (34, 106), (126, 62), (126, 106)):
    Wk.rect(x, y, 8, 4, "#2B2B2B")
Wk.line([(84, 42), (84, 48)], "#5E6B63", 2)
Wk.poly([(76, 54), (92, 54), (88, 48), (80, 48)], "#5E6B63")
Wk.rect(152, 60, 44, 30, WHITE)
Wk.rect(156, 64, 36, 22, GLASS)
for x, y in ((192, 64), (192, 74), (180, 64), (166, 64), (192, 84)):
    Wk.line([(156, 86), (x, y)], "#5E6B63", 1.5)
Wk.ellipse(24, 106, 14, 14, "#2B2B2B")
Wk.ellipse(24, 106, 8, 8, "#D6CDB6")

# ---------- Деревья и кусты ----------
trees = [(-104, 452, 86), (-70, 430, 64), (-32, 470, 96), (10, 446, 70), (-86, 560, 110), (-40, 600, 84),
         (-96, 700, 120), (-50, 760, 90), (-100, 880, 130), (-20, 900, 80),
         (486, 448, 84), (452, 428, 62), (418, 468, 94), (382, 444, 66), (470, 566, 108), (424, 606, 82),
         (488, 706, 122), (440, 770, 92), (494, 884, 128), (410, 910, 84), (366, 620, 70)]
for x, base, height in sorted(trees, key=lambda t: t[1]):
    spruce(x, base, height, "#2A7A45", "#1F5E38")
for x, y, r in [(110, 468, 12), (356, 560, 13), (30, 660, 14), (120, 700, 11), (170, 812, 13), (350, 800, 15),
                (60, 880, 16), (250, 900, 18), (330, 884, 14), (140, 906, 15)]:
    bush(x, y, r, "#4FA34A", "#2F7A3A")

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "client", "Assets", "_Game", "Resources", "Art", "CampDay.png")
os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
final = img.resize((W // SUPER, H // SUPER), Image.LANCZOS)
final.save(out, optimize=True)
print(os.path.abspath(out), final.size)

# ---------- Лодка: отдельная картинка 64×44 единицы, точка опоры — середина днища (32, 38) ----------
BW, BH = 64, 44
boat = Image.new("RGBA", (BW * K, BH * K), (0, 0, 0, 0))
bd = ImageDraw.Draw(boat)


def bp(x, y):
    return (x * K, y * K)


hull = [bp(*p) for p in cubic((4, 28), (18, 38), (46, 38), (60, 28), 20)] + [bp(54, 40), bp(10, 40)]
bd.polygon(hull, fill=rgba("#C4473A"))
bd.polygon([bp(10, 40), bp(54, 40), bp(52, 43), bp(12, 43)], fill=rgba("#8E2F27"))
bd.line([bp(32, 30), bp(32, 4)], fill=rgba("#5E4028"), width=2 * K)
bd.polygon([bp(34, 6), bp(34, 26), bp(52, 23)], fill=rgba("#FFFFFF"))
bd.polygon([bp(30, 9), bp(30, 26), bp(18, 24)], fill=rgba("#F1EEE4"))
boat_out = os.path.join(os.path.dirname(os.path.abspath(out)), "CampBoat.png")
boat.resize((BW * SCALE, BH * SCALE), Image.LANCZOS).save(boat_out, optimize=True)
print(boat_out)

# ---------- Гребная лодка: отдельная картинка 120×60 единиц для экрана «Лодки» ----------
RW, RH = 120, 60
row = Image.new("RGBA", (RW * K, RH * K), (0, 0, 0, 0))
rd = ImageDraw.Draw(row)
for kind, points, paint, *width in rowboat_parts(60, 40, 104, "#2D7FB8", "#1F5C87", "#BFE2F5"):
    pts = [bp(*q) for q in points]
    if kind == "poly":
        rd.polygon(pts, fill=rgba(paint))
    else:
        w = max(1, round(width[0] * K))
        rd.line(pts, fill=rgba(paint), width=w)
        for x, y in (pts[0], pts[-1]):
            rd.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=rgba(paint))
row_out = os.path.join(os.path.dirname(os.path.abspath(out)), "Rowboat.png")
row.resize((RW * SCALE, RH * SCALE), Image.LANCZOS).save(row_out, optimize=True)
print(row_out)

"""Карта озера «Тихое» для выбора места ловли, в теме «Летний день».

    python tools/art/lake_map.py [путь.png]
По умолчанию — client/Assets/_Game/Resources/Art/LakeMap.png.

Вид сверху: озеро с островом и заросшим затоном, тропа вокруг него, лес, ручей, турбаза внизу. К местам
на воде (центр озера, остров, затон) от причала турбазы идёт пунктир — туда добираются на лодке. Геометрия та же, что у картинки турбазы
(camp_day.py): сцена 390×844 и запас вокруг. Метки мест и подписи рисует игра (LakeHud.Map.cs) — их положения
в SPOTS должны совпадать с SpotPlace там.
"""
import math
import os
import random
import sys

from PIL import Image, ImageDraw

SCENE_W, SCENE_H = 390, 844
PAD_X, PAD_TOP, PAD_BOTTOM = 125, 160, 96
SCALE, SUPER = 3, 2
K = SCALE * SUPER
W = (SCENE_W + 2 * PAD_X) * K
H = (SCENE_H + PAD_TOP + PAD_BOTTOM) * K
X0, X1 = -PAD_X, SCENE_W + PAD_X
Y0, Y1 = -PAD_TOP, SCENE_H + PAD_BOTTOM

SPOTS = {"maple": (88, 612), "reeds": (30, 400), "old_pier": (356, 470), "pit": (205, 128), "stream": (348, 180),
         "center": (192, 404), "island": (270, 336), "backwater": (48, 222)}
# Берег по часовой стрелке от затона (левый верхний угол): затон — узкий залив, с берега к нему не подойти.
LAKE = [(52, 296), (20, 254), (14, 204), (44, 168), (90, 180), (140, 165), (250, 140), (335, 195), (352, 320),
        (338, 460), (290, 560), (200, 605), (105, 585), (48, 480), (36, 350)]
ISLAND = (270, 312)
CENTER = (195, 375)

rng = random.Random(11)
img = Image.new("RGB", (W, H), "#5BAA4A")
draw = ImageDraw.Draw(img, "RGBA")


def px(x, y):
    return ((x + PAD_X) * K, (y + PAD_TOP) * K)


def rgba(color, alpha=1.0):
    color = color.lstrip("#")
    return (int(color[0:2], 16), int(color[2:4], 16), int(color[4:6], 16), round(255 * alpha))


def poly(points, color, alpha=1.0):
    draw.polygon([px(*p) for p in points], fill=rgba(color, alpha))


def ellipse(cx, cy, rx, ry, color, alpha=1.0):
    draw.ellipse([px(cx - rx, cy - ry), px(cx + rx, cy + ry)], fill=rgba(color, alpha))


def rect(x, y, w, h, color, radius=0.0):
    draw.rounded_rectangle([px(x, y), px(x + w, y + h)], radius=radius * K, fill=rgba(color))


def line(points, color, width, alpha=1.0):
    pts = [px(*p) for p in points]
    w = max(1, round(width * K))
    draw.line(pts, fill=rgba(color, alpha), width=w, joint="curve")
    for x, y in (pts[0], pts[-1]):
        draw.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=rgba(color, alpha))


def closed_curve(points, steps=14):
    out = []
    n = len(points)
    for i in range(n):
        p0, p1, p2, p3 = points[(i - 1) % n], points[i], points[(i + 1) % n], points[(i + 2) % n]
        for k in range(steps):
            t = k / steps
            out.append(tuple(0.5 * (2 * p1[a] + (p2[a] - p0[a]) * t + (2 * p0[a] - 5 * p1[a] + 4 * p2[a] - p3[a]) * t * t
                                    + (3 * p1[a] - p0[a] - 3 * p2[a] + p3[a]) * t ** 3) for a in (0, 1)))
    return out


def scaled(points, factor):
    return [(CENTER[0] + (x - CENTER[0]) * factor, CENTER[1] + (y - CENTER[1]) * factor) for x, y in points]


def inside(point, polygon):
    x, y = point
    hit = False
    j = len(polygon) - 1
    for i in range(len(polygon)):
        xi, yi = polygon[i]
        xj, yj = polygon[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            hit = not hit
        j = i
    return hit


shore = closed_curve(LAKE)

# ---------- Земля: трава пятнами ----------
for _ in range(260):
    x, y = rng.uniform(X0, X1), rng.uniform(Y0, Y1)
    r = rng.uniform(18, 46)
    ellipse(x, y, r, r * 0.7, rng.choice(["#66B552", "#52A044", "#6FBF58"]), 0.5)

# ---------- Ручей: впадает в озеро справа сверху ----------
stream = [(520, -40), (470, 30), (430, 80), (402, 132), (370, 168), (340, 200)]
line(stream, "#E6D9A0", 13, 0.7)
line(stream, "#6CC4E6", 8)

# ---------- Тропа вокруг озера и от турбазы ----------
trail = closed_curve(scaled(LAKE, 1.16))
line(trail + [trail[0]], "#CDBB86", 8, 0.6)
line(trail + [trail[0]], "#EAD9A6", 5.5, 0.95)
line([(200, 704), (200, 660), (196, 640)], "#EAD9A6", 6, 0.95)

# ---------- Топь у затона: тропа здесь пропадает ----------
for _ in range(70):
    x, y = rng.uniform(-22, 112), rng.uniform(132, 318)
    r = rng.uniform(9, 20)
    ellipse(x, y, r, r * 0.7, rng.choice(["#4C8C46", "#5E9A4C", "#3F7D40"]), 0.85)
for _ in range(26):
    x, y = rng.uniform(-18, 108), rng.uniform(136, 314)
    ellipse(x, y, rng.uniform(4, 8), rng.uniform(2, 3.5), "#3E7FA0", 0.55)

# ---------- Озеро ----------
poly(closed_curve(scaled(LAKE, 1.035)), "#E6D9A0")
poly(shore, "#6CC4E6")
poly(closed_curve(scaled(LAKE, 0.93)), "#48A9D8")
poly(closed_curve(scaled(LAKE, 0.80)), "#2A8EBF")
ellipse(205, 262, 86, 58, "#1F7AAB", 0.9)
ellipse(205, 258, 56, 36, "#14608B", 0.9)      # яма
for cx, cy, rx, a in [(120, 300, 22, .6), (270, 380, 26, .5), (160, 470, 20, .5), (240, 520, 18, .4), (300, 270, 16, .5),
                      (110, 400, 16, .4), (200, 200, 18, .35)]:
    ellipse(cx, cy, rx, 1.8, "#FFFFFF", a)

# Путь лодки: пунктир от причала турбазы к местам на воде.
def dashed(points, dash=6.0, gap=5.0):
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        length = math.hypot(x1 - x0, y1 - y0)
        steps = int(length // (dash + gap))
        for i in range(steps + 1):
            a = i * (dash + gap) / length
            b = min(1.0, (i * (dash + gap) + dash) / length)
            if a >= 1.0:
                break
            line([(x0 + (x1 - x0) * a, y0 + (y1 - y0) * a), (x0 + (x1 - x0) * b, y0 + (y1 - y0) * b)], "#FFFFFF", 1.7, 0.6)


dashed([(222, 560), (204, 470), SPOTS["center"]])
dashed([SPOTS["center"], (236, 372), SPOTS["island"]])
dashed([SPOTS["center"], (120, 330), (70, 268), SPOTS["backwater"]])

# Остров: отмель, песок, трава и три дерева.
ix, iy = ISLAND
ellipse(ix, iy + 2, 38, 25, "#6CC4E6", 0.9)
ellipse(ix, iy, 30, 18, "#E6D9A0")
ellipse(ix, iy - 1, 25, 14, "#5BAA4A")
for dx, dy, r in [(-10, -3, 9), (7, -5, 10), (0, 3, 7)]:
    ellipse(ix + dx + 1.5, iy + dy + 2, r, r * 0.8, "#1D4A24", 0.3)
    ellipse(ix + dx, iy + dy, r, r, "#1F5E38")
    ellipse(ix + dx - r * 0.25, iy + dy - r * 0.28, r * 0.62, r * 0.62, "#2F8048")

# Затон зарос камышом и кувшинками.
for _ in range(60):
    x = rng.uniform(14, 92)
    y = rng.uniform(170, 292)
    if inside((x, y), shore) and math.hypot(x - SPOTS["backwater"][0], y - SPOTS["backwater"][1]) > 14:
        if rng.random() < 0.6:
            line([(x, y), (x + rng.uniform(-2, 2), y - rng.uniform(5, 9))], "#9CCB5A", 1.4)
        else:
            ellipse(x, y, 3.2, 2.2, "#7FC25A", 0.95)

# Камыши у левого берега.
for _ in range(46):
    x = rng.uniform(40, 76)
    y = rng.uniform(350, 452)
    if inside((x, y), shore):
        line([(x, y), (x + rng.uniform(-2, 2), y - rng.uniform(5, 9))], "#9CCB5A", 1.4)

# Старый причал у правого берега.
rect(300, 466, 46, 8, "#8A6240", 1.5)
for x in (304, 318, 332):
    line([(x, 466), (x, 474)], "#6E4C30", 1)
ellipse(300, 470, 3, 3, "#5E4028")

# Причал турбазы и лодка.
rect(196, 578, 8, 34, "#8A6240", 1.5)
poly([(214, 566), (232, 566), (228, 574), (218, 574)], "#C4473A")
poly([(222, 566), (222, 552), (232, 564)], "#FFFFFF")

# ---------- Лес ----------
keep_out = closed_curve(scaled(LAKE, 1.24))
trees = []
attempts = 0
while len(trees) < 330 and attempts < 9000:
    attempts += 1
    x, y = rng.uniform(X0, X1), rng.uniform(Y0, Y1)
    if inside((x, y), keep_out):
        continue
    if 104 < x < 296 and 628 < y < 800:           # турбаза
        continue
    if any(math.hypot(x - sx, y - sy) < 30 for sx, sy in SPOTS.values()):
        continue
    if 380 < x < 540 and -60 < y < 150 and abs((x - 340) * 0.9 + (y - 200)) < 34:   # вдоль ручья
        continue
    trees.append((x, y, rng.uniform(8, 14)))
for x, y, r in sorted(trees, key=lambda t: t[1]):
    ellipse(x + 2, y + 3, r, r * 0.8, "#1D4A24", 0.3)
    ellipse(x, y, r, r, "#1F5E38")
    ellipse(x - r * 0.25, y - r * 0.28, r * 0.62, r * 0.62, "#2F8048")

# Клён у места «Под клёном».
ellipse(70, 600, 21, 17, "#1D4A24", 0.3)
ellipse(66, 594, 20, 20, "#7FA83A")
ellipse(60, 587, 13, 13, "#B6D34A")
ellipse(76, 600, 8, 8, "#9CC243")

# ---------- Турбаза: дома сверху ----------
ellipse(200, 716, 96, 60, "#7CC45C", 0.9)
line([(150, 700), (200, 704), (250, 696)], "#EAD9A6", 5, 0.95)
for x, y, w, h, roof, ridge in [(228, 672, 46, 32, "#2D5F8B", "#234D73"), (140, 706, 38, 30, "#C4473A", "#8E2F27"),
                                (118, 664, 36, 26, "#8A6240", "#5E4028")]:
    rect(x + 2, y + 3, w, h, "#1D4A24", 2)
    rect(x, y, w, h, roof, 2)
    line([(x + 3, y + h / 2), (x + w - 3, y + h / 2)], ridge, 1.6)
poly([(168, 652), (212, 652), (212, 668), (168, 668)], "#FF6B4A")
for x in (176, 192, 204):
    rect(x, 652, 5, 16, "#FFFFFF")

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "client", "Assets", "_Game", "Resources", "Art", "LakeMap.png")
os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
final = img.resize((W // SUPER, H // SUPER), Image.LANCZOS)
final.save(out, optimize=True)
print(os.path.abspath(out), final.size)

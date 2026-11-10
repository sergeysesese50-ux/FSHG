"""Картинки наживок для коробки с наживкой и магазина: по одной на наживку, на прозрачном фоне.

    python tools/art/bait_icons.py [папка]
По умолчанию — client/Assets/_Game/Resources/Art/Bait/<id наживки или приманки>.png (256×256).

Те же id, что в shared/data/tackle.json: наживка для поплавочной и донной удочки и приманки спиннинга. Картинки мелкие
(в интерфейсе — 26–40 пикселей), поэтому формы крупные, с тёмной каймой, без мелких деталей.
"""
import math
import os
import sys

from PIL import Image, ImageDraw

SIZE, SS = 256, 4
C = SIZE * SS


def rgba(color, alpha=1.0):
    color = color.lstrip("#")
    return (int(color[0:2], 16), int(color[2:4], 16), int(color[4:6], 16), round(255 * alpha))


def px(v):
    return v * SS


def curve(points, steps=24):
    """Сглаженная линия через точки (Катмулл — Ром)."""
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


class Icon:
    """Холст 256×256 (рисуется вчетверо крупнее). Полупрозрачное кладётся отдельным слоем: ImageDraw на RGBA
    не смешивает цвета, а заменяет пиксели."""

    def __init__(self):
        self.img = Image.new("RGBA", (C, C), (0, 0, 0, 0))

    def _draw(self, fn, alpha):
        if alpha >= 1.0:
            fn(ImageDraw.Draw(self.img, "RGBA"))
            return
        layer = Image.new("RGBA", (C, C), (0, 0, 0, 0))
        fn(ImageDraw.Draw(layer, "RGBA"))
        self.img.alpha_composite(layer)

    def ellipse(self, cx, cy, rx, ry, color, alpha=1.0, angle=0.0):
        a = math.radians(angle)
        pts = []
        for k in range(48):
            t = 2 * math.pi * k / 48
            x, y = rx * math.cos(t), ry * math.sin(t)
            pts.append((px(cx + x * math.cos(a) - y * math.sin(a)), px(cy + x * math.sin(a) + y * math.cos(a))))
        self._draw(lambda d: d.polygon(pts, fill=rgba(color, alpha)), alpha)

    def polygon(self, points, color, alpha=1.0):
        pts = [(px(x), px(y)) for x, y in points]
        self._draw(lambda d: d.polygon(pts, fill=rgba(color, alpha)), alpha)

    def line(self, a, b, width, color, alpha=1.0):
        self._draw(lambda d: d.line([(px(a[0]), px(a[1])), (px(b[0]), px(b[1]))], fill=rgba(color, alpha), width=round(px(width))), alpha)

    def stroke(self, points, radius, color, alpha=1.0, shift=(0.0, 0.0), start=0.0, end=1.0):
        """Толстая линия со скруглёнными концами: кружки вдоль точек. radius — число или функция от доли пути 0…1."""
        n = len(points)

        def paint(d):
            for i, (x, y) in enumerate(points):
                t = i / max(1, n - 1)
                if t < start or t > end:
                    continue
                r = radius(t) if callable(radius) else radius
                if r <= 0:
                    continue
                x, y = x + shift[0], y + shift[1]
                d.ellipse([px(x - r), px(y - r), px(x + r), px(y + r)], fill=rgba(color, alpha))

        self._draw(paint, alpha)

    def save(self, path):
        self.img.resize((SIZE, SIZE), Image.LANCZOS).save(path, optimize=True)


def ball(icon, cx, cy, r, fill, rim, shade, edge=3.0):
    """Шарик: кайма, цвет, тень снизу справа, блик сверху слева."""
    icon.ellipse(cx, cy, r + edge, r + edge, rim)
    icon.ellipse(cx, cy, r, r, fill)
    icon.ellipse(cx + r * 0.16, cy + r * 0.20, r * 0.80, r * 0.76, shade, 0.55)
    icon.ellipse(cx - r * 0.10, cy - r * 0.12, r * 0.78, r * 0.74, fill)
    icon.ellipse(cx - r * 0.34, cy - r * 0.38, r * 0.26, r * 0.18, "#FFFFFF", 0.65, angle=-35)


def segments(icon, points, radius, color, alpha, every, start=0.08, end=0.92):
    """Поперечные складки вдоль тела: короткие штрихи поперёк линии."""
    n = len(points)
    for i in range(every, n - every, every):
        t = i / (n - 1)
        if t < start or t > end:
            continue
        (x0, y0), (x1, y1) = points[i - 1], points[i + 1]
        length = math.hypot(x1 - x0, y1 - y0) or 1.0
        nx, ny = -(y1 - y0) / length, (x1 - x0) / length
        r = (radius(t) if callable(radius) else radius) * 0.82
        x, y = points[i]
        icon.line((x - nx * r, y - ny * r), (x + nx * r, y + ny * r), 2.2, color, alpha)


# ---------- Наживки ----------

def worm():
    icon = Icon()
    path = curve([(44, 176), (70, 120), (112, 124), (140, 170), (184, 172), (214, 96)])
    radius = lambda t: 17.0 * (0.62 + 0.38 * math.sin(math.pi * min(1.0, max(0.0, t))) ** 0.6)
    icon.stroke(path, lambda t: radius(t) + 3.5, "#7C2E2C")
    icon.stroke(path, radius, "#CC675B")
    # Поясок — светлее и чуть толще.
    icon.stroke(path, lambda t: radius(t) * 1.06, "#E2A08F", start=0.60, end=0.72)
    icon.stroke(path, lambda t: radius(t) * 0.42, "#EBAA9B", 0.85, shift=(-3.0, -5.0), start=0.04, end=0.96)
    segments(icon, path, radius, "#7C2E2C", 0.30, 6)
    return icon


def maggot():
    icon = Icon()

    def one(points, size):
        path = curve(points)
        radius = lambda t: size * (0.45 + 0.55 * math.sin(math.pi * (0.12 + 0.70 * t)) ** 0.8)
        icon.stroke(path, lambda t: radius(t) + 3.2, "#8E7A45")
        icon.stroke(path, radius, "#F3E6C0")
        icon.stroke(path, lambda t: radius(t) * 0.40, "#FFFBEA", 0.9, shift=(-2.5, -4.0), start=0.10, end=0.90)
        segments(icon, path, radius, "#8E7A45", 0.38, 5)
        # Тёмная точка на остром конце — голова.
        hx, hy = path[1]
        icon.ellipse(hx, hy, 3.2, 3.2, "#4A3B1C")

    one([(52, 104), (92, 80), (136, 86), (172, 112)], 19)
    one([(92, 196), (130, 168), (172, 168), (208, 190)], 18)
    one([(48, 168), (62, 140), (88, 126), (118, 132)], 15)
    return icon


def bloodworm():
    icon = Icon()
    # Пучок: четыре волнистых личинки, расходятся веером от левого края.
    lines = [
        [(44, 118), (84, 82), (124, 96), (166, 62), (212, 72)],
        [(40, 136), (86, 116), (128, 134), (172, 106), (218, 118)],
        [(44, 152), (88, 150), (130, 172), (174, 150), (216, 166)],
        [(52, 168), (90, 186), (130, 206), (170, 192), (206, 208)],
    ]
    for points in lines:
        path = curve(points)
        radius = lambda t: 8.0 * (0.70 + 0.30 * math.sin(math.pi * t))
        icon.stroke(path, lambda t: radius(t) + 3.0, "#6E0D16")
        icon.stroke(path, radius, "#C9212C")
        icon.stroke(path, lambda t: radius(t) * 0.38, "#F07078", 0.9, shift=(-1.2, -2.0), start=0.06, end=0.94)
    return icon


def bread():
    icon = Icon()

    def slice_shape(grow, color):
        icon.ellipse(128, 104, 86 + grow, 50 + grow, color)
        icon.polygon([(52 - grow, 112), (204 + grow, 112), (196 + grow, 212 + grow), (60 - grow, 212 + grow)], color)
        icon.ellipse(64, 204, 12 + grow, 12 + grow, color)
        icon.ellipse(192, 204, 12 + grow, 12 + grow, color)

    slice_shape(4, "#7C4A20")
    slice_shape(0, "#C07E3E")
    # Мякиш.
    icon.ellipse(128, 108, 74, 40, "#F5E5BC")
    icon.polygon([(64, 114), (192, 114), (186, 200), (70, 200)], "#F5E5BC")
    icon.ellipse(128, 100, 62, 26, "#FBF1D6", 0.8)
    for cx, cy, rx, ry in [(98, 132, 9, 6), (150, 122, 7, 5), (126, 160, 10, 7), (168, 164, 7, 6), (92, 176, 6, 5), (146, 186, 6, 4)]:
        icon.ellipse(cx, cy, rx, ry, "#DEC58C")
    return icon


def dough():
    icon = Icon()
    ball(icon, 96, 150, 50, "#F2E1B4", "#9C8650", "#D8C18A")
    ball(icon, 172, 164, 38, "#F2E1B4", "#9C8650", "#D8C18A")
    ball(icon, 146, 88, 32, "#F6E8C2", "#9C8650", "#DCC792")
    return icon


def corn():
    icon = Icon()

    def kernel(cx, cy, size, angle):
        a = math.radians(angle)
        shape = curve([(0, -1.0), (0.62, -0.78), (0.80, -0.10), (0.46, 0.72), (0, 1.0), (-0.46, 0.72), (-0.80, -0.10), (-0.62, -0.78), (0, -1.0)], 10)

        def place(points, scale):
            return [(cx + (x * math.cos(a) - y * math.sin(a)) * size * scale, cy + (x * math.sin(a) + y * math.cos(a)) * size * scale)
                    for x, y in points]

        icon.polygon(place(shape, 1.10), "#9C6A0E")
        icon.polygon(place(shape, 1.0), "#F6C42A")
        # Светлый носик зерна и блик.
        tip = (cx - math.sin(a) * size * 0.66, cy + math.cos(a) * size * 0.66)
        icon.ellipse(tip[0], tip[1], size * 0.34, size * 0.26, "#FCEBAA", angle=angle)
        top = (cx + math.sin(a) * size * 0.40 - size * 0.18, cy - math.cos(a) * size * 0.40 - size * 0.10)
        icon.ellipse(top[0], top[1], size * 0.20, size * 0.13, "#FFFFFF", 0.7, angle=angle - 30)

    kernel(86, 96, 38, -18)
    kernel(168, 92, 36, 22)
    kernel(128, 158, 40, 4)
    kernel(62, 176, 30, -40)
    kernel(196, 174, 30, 38)
    return icon


def pearl_barley():
    icon = Icon()
    grains = [(82, 84, 14), (140, 70, -22), (192, 98, 30), (60, 138, -34), (118, 126, 8), (176, 150, -14),
              (86, 188, 20), (146, 190, -6), (204, 196, 26)]
    for cx, cy, angle in grains:
        icon.ellipse(cx, cy, 31, 21, "#8E7C4E", angle=angle)
        icon.ellipse(cx, cy, 28, 18, "#E8DDBC", angle=angle)
        a = math.radians(angle)
        dx, dy = math.cos(a) * 19, math.sin(a) * 19
        icon.line((cx - dx, cy - dy), (cx + dx, cy + dy), 3.0, "#8E7C4E", 0.55)
        icon.ellipse(cx - 6 + math.sin(a) * 8, cy - 8 - math.cos(a) * 2, 9, 4, "#FFFFFF", 0.6, angle=angle)
    return icon


def peas():
    icon = Icon()
    for cx, cy, r in [(88, 96, 36), (170, 100, 33), (128, 164, 38), (60, 172, 26), (198, 176, 27)]:
        ball(icon, cx, cy, r, "#78B93E", "#3C6E1E", "#55962A")
    return icon


def livebait():
    icon = Icon()
    top = curve([(52, 128), (78, 112), (118, 100), (160, 100), (194, 110), (220, 128)])
    bottom = curve([(52, 128), (78, 142), (118, 156), (160, 156), (196, 146), (220, 128)])
    body = top + list(reversed(bottom))

    def grown(points, grow):
        return [(128 + (x - 128) * (1 + grow / 92.0), 128 + (y - 128) * (1 + grow / 28.0)) for x, y in points]

    tail = [(62, 128), (22, 92), (34, 128), (22, 164)]
    # Кайма: хвост, плавники и тело чуть крупнее.
    icon.polygon([(66, 128), (16, 84), (30, 128), (16, 172)], "#34505F")
    icon.polygon([(118, 100), (140, 70), (172, 76), (168, 102)], "#34505F")
    icon.polygon([(120, 154), (132, 178), (154, 172), (150, 154)], "#34505F")
    icon.polygon(grown(body, 5), "#34505F")
    icon.polygon(tail, "#7E9DB2")
    icon.polygon([(122, 102), (141, 76), (166, 81), (164, 102)], "#7E9DB2")
    icon.polygon([(124, 152), (134, 172), (150, 168), (148, 152)], "#9DB6C6")
    # Тело: серебристый бок, тёмная спина, светлое брюхо.
    icon.polygon(body, "#C6D8E3")
    back = top + [(220, 128), (194, 120), (160, 114), (118, 114), (78, 122), (52, 128)]
    icon.polygon(back, "#5E7F96")
    belly = bottom + [(220, 128), (196, 138), (160, 146), (118, 146), (78, 136), (52, 128)]
    icon.polygon(belly, "#F3F7F9", 0.85)
    icon.line((84, 128), (190, 126), 2.4, "#FFFFFF", 0.55)
    # Жаберная крышка и глаз.
    icon.line((186, 112), (182, 144), 2.4, "#34505F", 0.45)
    icon.ellipse(200, 122, 8.5, 8.5, "#FFFFFF")
    icon.ellipse(201, 122, 4.8, 4.8, "#1C2A32")
    return icon


# ---------- Ещё наживка ----------

def nightcrawler():
    """Выползок: крупный тёмный червь, свернувшийся петлёй."""
    icon = Icon()
    path = curve([(50, 96), (96, 54), (160, 60), (200, 108), (186, 166), (130, 190), (84, 164), (92, 122), (136, 112), (160, 140)])
    radius = lambda t: 17.0 * (0.55 + 0.45 * math.sin(math.pi * min(1.0, max(0.0, t))) ** 0.5)
    icon.stroke(path, lambda t: radius(t) + 3.5, "#5A2020")
    icon.stroke(path, radius, "#A64C46")
    icon.stroke(path, lambda t: radius(t) * 1.06, "#C98478", start=0.16, end=0.26)
    icon.stroke(path, lambda t: radius(t) * 0.40, "#D58E84", 0.85, shift=(-3.0, -5.0), start=0.03, end=0.97)
    segments(icon, path, radius, "#5A2020", 0.32, 7)
    return icon


def caddis():
    """Ручейник: личинка выглядывает из домика, склеенного из палочек."""
    icon = Icon()
    case = curve([(56, 190), (110, 150), (164, 108)])
    icon.stroke(case, 30, "#3E2C18")
    icon.stroke(case, 26, "#8A6A40")
    sticks = [(60, 178, 34, 26, "#5A4426"), (84, 190, -20, 30, "#A88856"), (92, 150, 50, 28, "#6E5030"), (118, 164, 12, 30, "#B89868"),
              (124, 128, 62, 26, "#5A4426"), (146, 142, -8, 28, "#A88856"), (150, 106, 40, 24, "#6E5030"), (72, 206, 6, 24, "#6E5030"),
              (104, 176, -34, 22, "#5A4426"), (136, 114, 20, 22, "#B89868")]
    for x, y, angle, length, color in sticks:
        a = math.radians(angle)
        dx, dy = math.cos(a) * length / 2, -math.sin(a) * length / 2
        icon.line((x - dx, y - dy), (x + dx, y + dy), 6.5, color)
    larva = curve([(160, 110), (186, 88), (206, 62)])
    icon.stroke(larva, 17, "#8E7A45")
    icon.stroke(larva, 13.5, "#F0E2B8")
    for x, y in ((176, 104), (188, 92), (198, 80)):
        icon.line((x, y), (x + 16, y + 10), 3.2, "#4A3418")
    icon.ellipse(210, 56, 13.5, 12.5, "#2E2010")
    icon.ellipse(210, 56, 10.5, 9.5, "#5E4220")
    return icon


def bark_grub():
    """Личинка короеда: толстая белая личинка, согнутая буквой «С», с коричневой головой."""
    icon = Icon()
    path = curve([(178, 76), (118, 58), (70, 100), (66, 160), (112, 198), (176, 184)])
    radius = lambda t: 25.0 * (0.72 + 0.28 * math.sin(math.pi * min(1.0, t * 1.05)))
    icon.stroke(path, lambda t: radius(t) + 3.5, "#8E7A45")
    icon.stroke(path, radius, "#F5EAC8")
    icon.stroke(path, lambda t: radius(t) * 0.38, "#FFFBEA", 0.9, shift=(-4.0, -6.0), start=0.10, end=0.92)
    segments(icon, path, radius, "#8E7A45", 0.40, 6, start=0.12, end=0.95)
    icon.ellipse(184, 78, 21, 20, "#4A2E12")
    icon.ellipse(184, 78, 17, 16, "#8A5A2A")
    icon.ellipse(178, 72, 6, 4.5, "#B88A52", 0.9, angle=-30)
    return icon


def grasshopper():
    icon = Icon()
    # Задняя нога — под телом: толстое бедро вверх и тонкая голень вниз.
    femur = curve([(112, 152), (92, 118), (72, 80)])
    icon.stroke(femur, lambda t: 11.0 - 6.0 * t + 3.0, "#2C5416")
    icon.stroke(femur, lambda t: 11.0 - 6.0 * t, "#5E9E30")
    icon.line((72, 80), (46, 196), 5.5, "#2C5416")
    icon.line((46, 196), (30, 200), 5.5, "#2C5416")
    # Тело, крыло и голова.
    icon.ellipse(124, 146, 78, 27, "#2C5416", angle=-6)
    icon.ellipse(124, 146, 74, 23, "#6FB23A", angle=-6)
    icon.ellipse(124, 156, 62, 10, "#A6D46A", 0.9, angle=-6)
    icon.ellipse(102, 132, 62, 14, "#2C5416", angle=-9)
    icon.ellipse(102, 132, 59, 11, "#8FCB52", angle=-9)
    icon.ellipse(198, 124, 25, 27, "#2C5416")
    icon.ellipse(198, 124, 21, 23, "#6FB23A")
    icon.ellipse(206, 116, 7, 8, "#1C2A10")
    icon.ellipse(208, 114, 2.2, 2.2, "#FFFFFF")
    # Передние ноги и усики.
    icon.line((168, 162), (178, 204), 4.5, "#2C5416")
    icon.line((142, 166), (134, 208), 4.5, "#2C5416")
    icon.line((206, 102), (238, 56), 3.0, "#2C5416")
    icon.line((214, 106), (250, 78), 3.0, "#2C5416")
    return icon


def semolina():
    """Манка: катышек густо замешанной манной каши с крупинками."""
    icon = Icon()
    ball(icon, 128, 138, 76, "#F7EFD8", "#A8966A", "#DCCDA0", edge=4.0)
    grains = [(96, 104), (140, 92), (172, 122), (110, 148), (150, 152), (188, 158), (84, 164), (126, 186), (162, 186), (68, 128),
              (118, 122), (148, 120), (100, 190), (176, 96)]
    for k, (x, y) in enumerate(grains):
        icon.ellipse(x, y, 5.0, 4.0, "#FFFFFF" if k % 2 else "#D8C89A", 0.9)
    return icon


def oats():
    """Геркулес: плоские овсяные хлопья."""
    icon = Icon()
    flakes = [(86, 86, 20), (156, 76, -24), (200, 122, 34), (68, 150, -30), (132, 138, 6), (186, 186, -12), (104, 196, 24)]
    for cx, cy, angle in flakes:
        icon.ellipse(cx, cy, 40, 26, "#9A8450", angle=angle)
        icon.ellipse(cx, cy, 36.5, 22.5, "#EFE4C4", angle=angle)
        icon.ellipse(cx, cy, 26, 13, "#E2D4AC", 0.9, angle=angle)
        a = math.radians(angle)
        dx, dy = math.cos(a) * 22, math.sin(a) * 22
        icon.line((cx - dx, cy - dy), (cx + dx, cy + dy), 2.6, "#9A8450", 0.5)
    return icon


def pasta():
    """Макароны: короткие гнутые трубочки-рожки."""
    icon = Icon()

    def tube(points):
        path = curve(points)
        icon.stroke(path, 17.5, "#A8791E")
        icon.stroke(path, 14.0, "#F3CE6A")
        icon.stroke(path, 5.0, "#FBE6A2", 0.9, shift=(-3.0, -5.0), start=0.08, end=0.9)
        x, y = path[-1]
        icon.ellipse(x, y, 14.0, 14.0, "#A8791E")
        icon.ellipse(x, y, 8.5, 8.5, "#7A5612")

    tube([(46, 96), (76, 62), (120, 60), (148, 88)])
    tube([(110, 142), (146, 122), (190, 132), (210, 166)])
    tube([(48, 170), (62, 208), (104, 220), (136, 198)])
    return icon


def oilcake():
    """Макуха: тёмный прессованный брикет жмыха с семечками."""
    icon = Icon()
    block = [(58, 92), (182, 70), (206, 158), (82, 186)]
    cx, cy = 132, 127
    grown = [(cx + (x - cx) * 1.07, cy + (y - cy) * 1.09) for x, y in block]
    icon.polygon(grown, "#3A2812")
    icon.polygon(block, "#7A5A36")
    icon.polygon([(58, 92), (182, 70), (188, 92), (64, 114)], "#93714A", 0.9)
    specks = [(88, 124, "#3E2C18"), (120, 110, "#B89868"), (152, 104, "#3E2C18"), (176, 118, "#B89868"), (104, 150, "#B89868"),
              (136, 138, "#3E2C18"), (166, 146, "#3E2C18"), (124, 166, "#3E2C18"), (156, 168, "#B89868"), (186, 146, "#B89868"),
              (96, 168, "#3E2C18")]
    for k, (x, y, color) in enumerate(specks):
        icon.ellipse(x, y, 6.5, 3.6, color, 0.95, angle=(k * 37) % 180)
    for x, y, r in ((214, 196, 9), (190, 208, 6), (228, 176, 5)):
        icon.ellipse(x, y, r + 2, r * 0.8 + 2, "#3A2812")
        icon.ellipse(x, y, r, r * 0.8, "#7A5A36")
    return icon


def leech():
    """Пиявка: тёмная, плоская, с присоской на конце и светлой полосой вдоль спины."""
    icon = Icon()
    path = curve([(40, 150), (80, 100), (126, 128), (168, 180), (214, 132)])
    radius = lambda t: 17.0 * (0.30 + 0.70 * math.sin(math.pi * min(1.0, 0.18 + t * 0.62)) ** 0.8)
    icon.stroke(path, lambda t: radius(t) + 3.5, "#1C2016")
    icon.stroke(path, radius, "#56633E")
    icon.stroke(path, lambda t: radius(t) * 0.36, "#D2A84A", 0.95, start=0.05, end=0.94)
    icon.stroke(path, lambda t: radius(t) * 0.20, "#A9B77E", 0.8, shift=(-2.0, -7.0), start=0.10, end=0.90)
    segments(icon, path, radius, "#1C2016", 0.30, 5, start=0.10, end=0.92)
    icon.ellipse(216, 130, 18.5, 17.5, "#1C2016")
    icon.ellipse(216, 130, 14.5, 13.5, "#56633E")
    icon.ellipse(216, 130, 7.0, 6.5, "#2A3020")
    return icon


def frog():
    icon = Icon()
    for cx, angle in ((74, 24), (182, -24)):
        icon.ellipse(cx, 180, 46, 24, "#2E5A1C", angle=angle)
        icon.ellipse(cx, 180, 42, 20, "#5FA83A", angle=angle)
    for cx in (88, 168):
        icon.ellipse(cx, 204, 20, 9, "#2E5A1C")
        icon.ellipse(cx, 204, 16.5, 6, "#7CC050")
    icon.ellipse(128, 152, 66, 50, "#2E5A1C")
    icon.ellipse(128, 152, 62, 46, "#5FA83A")
    icon.ellipse(128, 172, 44, 24, "#BFE08E")
    icon.ellipse(128, 104, 54, 40, "#2E5A1C")
    icon.ellipse(128, 104, 50, 36, "#6CB644")
    for cx in (98, 158):
        icon.ellipse(cx, 78, 20, 20, "#2E5A1C")
        icon.ellipse(cx, 78, 16.5, 16.5, "#6CB644")
        icon.ellipse(cx, 78, 11.5, 11.5, "#FFFFFF")
        icon.ellipse(cx + 1.5, 79, 6.0, 7.0, "#16240E")
    mouth = curve([(92, 112), (110, 122), (146, 122), (164, 112)], 10)
    icon.stroke(mouth, 2.2, "#2E5A1C")
    for x, y, r in ((104, 146, 7), (150, 140, 8), (128, 164, 5), (86, 160, 5)):
        icon.ellipse(x, y, r, r * 0.8, "#3F8A28", 0.8)
    return icon


# ---------- Приманки для спиннинга ----------

STEEL = "#3A4046"


def hook(icon, x, y, size, side=1):
    """Одинарный крючок: цевьё вниз от (x, y) и загиб в сторону side (1 — вправо, −1 — влево)."""
    path = curve([(x, y), (x, y + size * 0.85), (x + side * size * 0.42, y + size * 1.22), (x + side * size * 0.86, y + size * 0.86),
                  (x + side * size * 0.84, y + size * 0.48)], 10)
    icon.stroke(path, max(1.8, size * 0.085), STEEL)


def treble(icon, x, y, size):
    """Тройник: два крючка в стороны и короткий посередине."""
    hook(icon, x, y, size, 1)
    hook(icon, x, y, size, -1)
    icon.line((x, y), (x, y + size * 1.05), max(3.6, size * 0.17), STEEL)


def ring(icon, x, y, r):
    """Заводное колечко."""
    circle = [(x + r * math.cos(2 * math.pi * k / 24), y + r * math.sin(2 * math.pi * k / 24)) for k in range(25)]
    icon.stroke(circle, 2.0, "#6A7078")


def spinner_icon(k, blade, blade_dark, body):
    """Вертушка: кольцо, бусина, лепесток на оси, сердечник и тройник. k — размер (номер блесны)."""
    icon = Icon()
    top, bottom = (88, 34), (166, 172)
    ax, ay = bottom[0] - top[0], bottom[1] - top[1]
    at = lambda t: (top[0] + ax * t, top[1] + ay * t)
    tilt = -math.degrees(math.atan2(ax, ay))
    icon.line(top, bottom, 4.2, "#6A7078")
    ring(icon, top[0] - 3, top[1] - 8, 8)
    x, y = at(0.16)
    icon.ellipse(x, y, 10, 10, "#B8261C")
    icon.ellipse(x - 3, y - 3, 3.2, 3.2, "#FFFFFF", 0.7)
    # Сердечник.
    core = [at(0.58 + 0.30 * i / 12) for i in range(13)]
    icon.stroke(core, 15.0 * k, "#5E4A16")
    icon.stroke(core, 12.0 * k, body)
    for t in (0.66, 0.76):
        x, y = at(t)
        icon.ellipse(x, y, 13.0 * k, 3.4, "#B8261C", angle=tilt + 90)
    # Лепесток — чуть в стороне от оси, под углом.
    x, y = at(0.40)
    px_, py_ = x - 24 * k, y + 2
    icon.ellipse(px_, py_, 37 * k, 62 * k, blade_dark, angle=tilt + 16)
    icon.ellipse(px_, py_, 33 * k, 58 * k, blade, angle=tilt + 16)
    icon.ellipse(px_ - 9 * k, py_ - 10 * k, 9 * k, 36 * k, "#FFFFFF", 0.6, angle=tilt + 16)
    treble(icon, bottom[0], bottom[1], 40 + 8 * k)
    return icon


def spoon_icon(k, metal, metal_dark, stripe):
    """Колебалка: вытянутая металлическая пластина-капля с тройником внизу."""
    icon = Icon()
    cx, cy, angle = 122, 108, math.radians(-22)
    shape = curve([(0, -1.0), (0.34, -0.72), (0.52, -0.05), (0.42, 0.66), (0, 1.0), (-0.42, 0.66), (-0.52, -0.05), (-0.34, -0.72), (0, -1.0)], 10)

    def place(points, sx, sy):
        return [(cx + (x * sx * math.cos(angle) - y * sy * math.sin(angle)), cy + (x * sx * math.sin(angle) + y * sy * math.cos(angle)))
                for x, y in points]

    w, h = 62 * k, 86 * k
    icon.polygon(place(shape, w + 7, h + 5), metal_dark)
    icon.polygon(place(shape, w, h), metal)
    icon.polygon(place([(x * 0.34 - 0.18, y * 0.82) for x, y in shape], w, h), "#FFFFFF", 0.55)
    for t in (-0.28, 0.0, 0.28):
        a = place([(-0.30, t + 0.30), (0.34, t + 0.16)], w, h)
        icon.line(a[0], a[1], 5.0, stripe, 0.85)
    tip = place([(0, -1.0)], w, h + 12)[0]
    ring(icon, tip[0], tip[1], 8)
    end = place([(0, 1.0)], w, h + 4)[0]
    treble(icon, end[0], end[1], 34)
    return icon


def soft_icon(k, body, body_dark, tail, foam=False):
    """Силикон на джиг-головке: свинцовая головка с крючком, ребристое тело и хвост-серп; foam — поролоновая рыбка."""
    icon = Icon()
    head = (66, 118)
    # Крючок: цевьё внутри тела, жало выходит из спины.
    shank = curve([head, (120, 124), (168, 126), (190, 108), (182, 86)], 12)
    icon.stroke(shank, 2.6, STEEL)
    if foam:
        fish = curve([(72, 118), (104, 92), (156, 96), (200, 118), (156, 142), (104, 146), (72, 118)], 10)
        cx, cy = 136, 118
        icon.polygon([(cx + (x - cx) * 1.08, cy + (y - cy) * 1.16) for x, y in fish], "#8A8F96")
        icon.polygon(fish, body)
        icon.polygon(curve([(80, 112), (106, 96), (156, 100), (194, 114), (150, 110), (104, 110), (80, 112)], 8), tail, 0.9)
        icon.polygon([(196, 118), (236, 92), (226, 118), (236, 146)], "#8A8F96")
        icon.polygon([(200, 118), (230, 100), (222, 118), (230, 138)], body)
        icon.ellipse(98, 114, 5, 5, "#1C2A32")
    else:
        spine = curve([(78, 120), (120, 124), (164, 126), (190, 128)])
        radius = lambda t: (20.0 - 9.0 * t) * k
        icon.stroke(spine, lambda t: radius(t) + 3.0, body_dark)
        icon.stroke(spine, radius, body)
        icon.stroke(spine, lambda t: radius(t) * 0.34, "#FFFFFF", 0.45, shift=(-2.0, -5.0 * k), start=0.05, end=0.9)
        segments(icon, spine, radius, body_dark, 0.55, 5, start=0.10, end=0.95)
        # Хвост-серп.
        curl = curve([(188, 128), (220, 132), (236, 160), (220, 190), (190, 196), (176, 176), (190, 160)], 12)
        icon.stroke(curl, lambda t: 9.5 * k * (1.0 - 0.55 * t) + 2.5, body_dark)
        icon.stroke(curl, lambda t: 9.5 * k * (1.0 - 0.55 * t), tail)
    # Головка и ушко.
    icon.ellipse(head[0], head[1], 21, 21, "#4A5058")
    icon.ellipse(head[0], head[1], 17.5, 17.5, "#9AA3AC")
    icon.ellipse(head[0] - 5, head[1] - 6, 6, 4.5, "#FFFFFF", 0.6, angle=-30)
    ring(icon, head[0] - 6, head[1] - 26, 7)
    return icon


def wobbler_icon(k, back, side, stripe, lip, mouth=False):
    """Воблер: пластиковая рыбка с лопастью у носа (lip — её длина; 0 — без лопасти) и двумя тройниками.
    mouth — поппер: вместо лопасти вогнутая «чашка» спереди."""
    icon = Icon()
    cx, cy = 126, 108
    top = curve([(40, cy), (70, cy - 26 * k), (120, cy - 36 * k), (170, cy - 30 * k), (206, cy - 12 * k), (214, cy)])
    bottom = curve([(40, cy), (70, cy + 20 * k), (120, cy + 30 * k), (170, cy + 26 * k), (206, cy + 10 * k), (214, cy)])
    body = top + list(reversed(bottom))
    # Лопасть — под носом, вперёд и вниз.
    if lip > 0:
        blade = [(196, cy + 6), (214, cy + 2), (214 + 20 * lip, cy + 34 * lip), (200 + 14 * lip, cy + 40 * lip)]
        icon.polygon([(x + (2 if n > 1 else 0), y + (2 if n > 1 else 0)) for n, (x, y) in enumerate(blade)], "#5E8FA8")
        icon.polygon(blade, "#CFEAF6", 0.9)
    icon.polygon([(40, cy), (16, cy - 22 * k), (24, cy), (16, cy + 22 * k)], "#34505F")
    icon.polygon([(42, cy), (20, cy - 17 * k), (27, cy), (20, cy + 17 * k)], back)
    icon.polygon([(cx + (x - cx) * 1.045, cy + (y - cy) * 1.14) for x, y in body], "#34505F")
    icon.polygon(body, side)
    icon.polygon(top + [(214, cy), (190, cy - 8 * k), (150, cy - 14 * k), (100, cy - 14 * k), (60, cy - 8 * k), (40, cy)], back)
    for x in (84, 112, 140, 168):
        icon.line((x, cy - 30 * k + abs(x - 126) * 0.12), (x + 8, cy + 6 * k), 5.5, stripe, 0.8)
    icon.polygon(bottom + [(214, cy), (190, cy + 8 * k), (150, cy + 16 * k), (100, cy + 16 * k), (60, cy + 8 * k), (40, cy)], "#FFFFFF", 0.55)
    if mouth:
        icon.ellipse(212, cy, 9, 17 * k, "#34505F")
        icon.ellipse(212, cy, 6, 13.5 * k, "#C8372D")
    icon.ellipse(186, cy - 9 * k, 10, 10, "#FFFFFF")
    icon.ellipse(187, cy - 9 * k, 5.5, 5.5, "#1C2A32")
    ring(icon, 222 if not mouth else 226, cy - 3, 6)
    treble(icon, 132, cy + 30 * k, 30)
    treble(icon, 44, cy + 6, 28)
    return icon


BAITS = {
    "worm": worm, "maggot": maggot, "bloodworm": bloodworm, "bread": bread, "dough": dough, "corn": corn,
    "pearl_barley": pearl_barley, "peas": peas, "livebait": livebait,
    "nightcrawler": nightcrawler, "caddis": caddis, "bark_grub": bark_grub, "grasshopper": grasshopper, "semolina": semolina,
    "oats": oats, "pasta": pasta, "oilcake": oilcake, "leech": leech, "frog": frog,
    # Вертушки №1–3: серебро, латунь, медь.
    "spinner_1": lambda: spinner_icon(0.78, "#DDE4EA", "#7C8791", "#C9A24A"),
    "spinner": lambda: spinner_icon(0.92, "#E8C25A", "#8A6A1E", "#C9A24A"),
    "spinner_3": lambda: spinner_icon(1.08, "#D98A56", "#7A4424", "#B8261C"),
    # Колебалки.
    "spoon_1": lambda: spoon_icon(0.82, "#DDE4EA", "#7C8791", "#C8372D"),
    "spoon_2": lambda: spoon_icon(1.0, "#E8C25A", "#8A6A1E", "#2D6CA8"),
    # Силикон.
    "twister_1": lambda: soft_icon(0.85, "#C8E04A", "#6E8A1E", "#E8F08A"),
    "twister_2": lambda: soft_icon(1.1, "#F08A3C", "#9A4A14", "#F6C24A"),
    "jig": lambda: soft_icon(1.0, "#F6F1E4", "#8A8F96", "#F0A0B0", foam=True),
    # Воблеры и поппер.
    "wobbler_1": lambda: wobbler_icon(0.8, "#3F7FB0", "#DDE8EF", "#2D5F8B", 0.8),
    "wobbler": lambda: wobbler_icon(1.0, "#5FA83A", "#F2D24A", "#2E5A1C", 1.0),
    "wobbler_3": lambda: wobbler_icon(1.12, "#2D4A7A", "#C9D8E6", "#1C2E4E", 1.7),
    "popper": lambda: wobbler_icon(0.92, "#C8372D", "#F6F1E4", "#8E2F27", 0.0, mouth=True),
}

folder = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "client", "Assets", "_Game", "Resources", "Art", "Bait")
os.makedirs(folder, exist_ok=True)
for name, draw in BAITS.items():
    path = os.path.join(folder, name + ".png")
    draw().save(path)
    print(os.path.abspath(path))

"""Картинки рыб для садка и карточек: по одной на вид, на прозрачном фоне, головой вправо.

    python tools/art/fish_icons.py [папка]
По умолчанию — client/Assets/_Game/Resources/Art/Fish/<id вида>.png (512×256).

Рыба задаётся числами: профиль спины и брюха (доли длины тела), цвета спины, бока и брюха, узор (полосы, пятна),
плавники. Виды — те же id, что в shared/data/fish.json.
"""
import os
import sys

from PIL import Image, ImageDraw

W, H, SS = 512, 256, 4
CW, CH = W * SS, H * SS
BODY_X0, BODY_X1 = 0.17 * CW, 0.95 * CW        # корень хвоста и кончик рыла
LENGTH = BODY_X1 - BODY_X0
MID = 0.5 * CH


def rgba(color, alpha=1.0):
    color = color.lstrip("#")
    return (int(color[0:2], 16), int(color[2:4], 16), int(color[4:6], 16), round(255 * alpha))


def P(x, y):
    """Точка рыбы: x — доля длины тела от хвоста (0) к рылу (1), y — вверх от оси, в долях длины."""
    return (BODY_X0 + x * LENGTH, MID - y * LENGTH)


def curve(points, steps=16):
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


def at(profile, x):
    """Высота профиля в точке x (линейно между точками сглаженной кривой)."""
    for (x0, y0), (x1, y1) in zip(profile, profile[1:]):
        if x0 <= x <= x1:
            return y0 + (y1 - y0) * (x - x0) / max(1e-6, x1 - x0)
    return profile[0][1] if x < profile[0][0] else profile[-1][1]


def fin(profile, x0, x1, height, peak, sign, lean=0.0):
    """Плавник на спине (sign=1) или брюхе (sign=-1): основание от x0 до x1, вершина на доле peak, сдвинута на lean."""
    xp = x0 + (x1 - x0) * peak
    top = at(profile, xp) + sign * height
    return [(x0, at(profile, x0)), (x0 + (xp - x0) * 0.55 + lean, at(profile, x0) + sign * height * 0.92), (xp + lean, top),
            (xp + (x1 - xp) * 0.5 + lean * 0.5, top - sign * height * 0.45), (x1, at(profile, x1))]


def draw_fish(spec):
    top = curve(spec["top"])
    bottom = curve(spec["bottom"])
    body = [P(x, y) for x, y in top] + [P(x, y) for x, y in reversed(bottom)]

    img = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
    d = ImageDraw.Draw(img, "RGBA")
    # Полупрозрачные штрихи рисуются на отдельном слое и накладываются: на RGBA-картинке ImageDraw
    # не смешивает цвета, а заменяет пиксели, и штрих «прорезал» бы рыбу.
    soft = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
    sd = ImageDraw.Draw(soft, "RGBA")

    def poly(points, color, alpha=1.0):
        (d if alpha >= 1.0 else sd).polygon([P(x, y) for x, y in points], fill=rgba(color, alpha))

    # Хвост и плавники под телом.
    tail = spec.get("tail", 0.15)
    poly([(0.07, 0.035), (-0.14, tail), (-0.07, 0.0), (-0.14, -tail), (0.07, -0.035)], spec["tail_color"])
    poly([(0.07, 0.02), (-0.10, tail * 0.55), (-0.07, 0.0), (-0.10, -tail * 0.55), (0.07, -0.02)], spec["fin_dark"], 0.25)
    for f in spec["fins"]:
        profile = top if f["side"] > 0 else bottom
        shape = fin(profile, f["x0"], f["x1"], f["h"], f.get("peak", 0.3), f["side"], f.get("lean", 0.0))
        poly(shape, f["color"])
        # Лучи плавника.
        for k in range(1, 6):
            xr = f["x0"] + (f["x1"] - f["x0"]) * k / 6
            tip = (xr + f.get("lean", 0.0) * 0.35, at(profile, xr) + f["side"] * f["h"] * 0.42)
            sd.line([P(xr, at(profile, xr)), P(*tip)], fill=rgba(spec["fin_dark"], 0.35), width=SS)
        if f.get("spot"):
            cx, cy = P(f["x0"] + 0.04, at(profile, f["x0"] + 0.04) + f["side"] * f["h"] * 0.30)
            sd.ellipse([cx - 5 * SS, cy - 4 * SS, cx + 5 * SS, cy + 4 * SS], fill=rgba("#141414", 0.75))
    img = Image.alpha_composite(img, soft)
    d = ImageDraw.Draw(img, "RGBA")
    soft = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
    sd = ImageDraw.Draw(soft, "RGBA")

    # Тело: градиент от спины к брюху по маске.
    mask = Image.new("L", (CW, CH), 0)
    ImageDraw.Draw(mask).polygon(body, fill=255)
    ys = [p[1] for p in body]
    y_top, y_bottom = min(ys), max(ys)
    grad = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grad)
    back, side, belly = rgba(spec["back"]), rgba(spec["side"]), rgba(spec["belly"])
    for y in range(int(y_top), int(y_bottom) + 1):
        t = (y - y_top) / max(1.0, y_bottom - y_top)
        if t < 0.42:
            k = t / 0.42
            c = tuple(round(back[i] + (side[i] - back[i]) * k) for i in range(3))
        else:
            k = (t - 0.42) / 0.58
            c = tuple(round(side[i] + (belly[i] - side[i]) * k) for i in range(3))
        gd.line([(0, y), (CW, y)], fill=c + (255,))
    img.paste(grad, (0, 0), mask)

    # Узор — только внутри тела.
    pattern = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
    pd = ImageDraw.Draw(pattern, "RGBA")
    for x, width, depth in spec.get("bars", []):
        y0 = at(top, x)
        pd.polygon([P(x - width, y0), P(x + width, y0), P(x + width * 0.45, y0 - depth), P(x - width * 0.45, y0 - depth)],
                   fill=rgba(spec["mark"], spec.get("mark_alpha", 0.5)))
    for x, y, rx, ry in spec.get("spots", []):
        cx, cy = P(x, y)
        pd.ellipse([cx - rx * LENGTH, cy - ry * LENGTH, cx + rx * LENGTH, cy + ry * LENGTH], fill=rgba(spec["mark"], spec.get("mark_alpha", 0.5)))
    # Блик вдоль бока и тень по брюху.
    hx0, hy0 = P(0.18, at(top, 0.5) * 0.55)
    hx1, hy1 = P(0.86, at(top, 0.5) * 0.18)
    pd.ellipse([hx0, hy0, hx1, hy1], fill=(255, 255, 255, 46))
    img.paste(pattern, (0, 0), Image.composite(pattern, Image.new("RGBA", (CW, CH), (0, 0, 0, 0)), mask).split()[3])
    d = ImageDraw.Draw(img, "RGBA")

    # Боковая линия, жаберная крышка, рот.
    lateral = [P(x, at(top, x) * 0.12 + at(bottom, x) * 0.02) for x in [i / 30 for i in range(4, 24)]]
    sd.line(lateral, fill=rgba(spec["back"], 0.45), width=SS)
    gx = spec.get("gill", 0.80)
    gill = [P(gx + 0.035 * (1 - (2 * k / 10 - 1) ** 2), at(bottom, gx) * 0.78 + (at(top, gx) * 0.78 - at(bottom, gx) * 0.78) * k / 10) for k in range(11)]
    sd.line(gill, fill=rgba(spec["back"], 0.6), width=round(1.6 * SS))
    mouth = spec.get("mouth", 0.05)
    sd.line([P(1.0 - mouth, -0.012), P(0.995, -0.004)], fill=rgba("#2A2A22", 0.8), width=round(1.6 * SS))
    img = Image.alpha_composite(img, soft)
    d = ImageDraw.Draw(img, "RGBA")

    # Грудной плавник — поверх тела.
    px = gx - 0.03
    d.polygon([P(px, at(bottom, px) * 0.45), P(px - 0.10, at(bottom, px) * 0.45 - 0.045), P(px - 0.115, at(bottom, px) * 0.45 - 0.015),
               P(px - 0.02, at(bottom, px) * 0.45 + 0.015)], fill=rgba(spec["pectoral"]))

    # Глаз.
    ex, ey = P(spec.get("eye_x", 0.91), at(top, spec.get("eye_x", 0.91)) * spec.get("eye_y", 0.38))
    er = spec.get("eye_r", 0.022) * LENGTH
    d.ellipse([ex - er * 1.18, ey - er * 1.18, ex + er * 1.18, ey + er * 1.18], fill=rgba("#20221C"))
    d.ellipse([ex - er, ey - er, ex + er, ey + er], fill=rgba(spec["eye"]))
    d.ellipse([ex - er * 0.52, ey - er * 0.52, ex + er * 0.52, ey + er * 0.52], fill=rgba("#0B0B08"))
    d.ellipse([ex - er * 0.55, ey - er * 0.62, ex - er * 0.12, ey - er * 0.2], fill=(255, 255, 255, 255))

    return img.resize((W, H), Image.LANCZOS)


def top_fin(x0, x1, h, color, **kw):
    return dict(x0=x0, x1=x1, h=h, color=color, side=1, **kw)


def low_fin(x0, x1, h, color, **kw):
    return dict(x0=x0, x1=x1, h=h, color=color, side=-1, **kw)


FISH = {
    "roach": dict(
        top=[(0.0, 0.035), (0.10, 0.060), (0.30, 0.115), (0.52, 0.150), (0.74, 0.125), (0.90, 0.075), (0.975, 0.030), (1.0, 0.0)],
        bottom=[(0.0, -0.035), (0.10, -0.055), (0.30, -0.105), (0.52, -0.140), (0.74, -0.120), (0.90, -0.075), (0.975, -0.035), (1.0, 0.0)],
        back="#58727E", side="#C6D3D9", belly="#F4F5F1", mark="#58727E", fin_dark="#3A2A22", tail_color="#B4573C",
        pectoral="#E8805A", eye="#F08A2A", tail=0.15,
        fins=[top_fin(0.42, 0.62, 0.085, "#7A7468", peak=0.25, lean=-0.02),
              low_fin(0.50, 0.60, 0.07, "#E0623A", peak=0.3, lean=-0.03), low_fin(0.24, 0.40, 0.065, "#E0623A", peak=0.25, lean=-0.02)]),
    "crucian": dict(
        top=[(0.0, 0.045), (0.10, 0.090), (0.28, 0.185), (0.50, 0.235), (0.72, 0.200), (0.88, 0.110), (0.97, 0.040), (1.0, 0.0)],
        bottom=[(0.0, -0.045), (0.10, -0.085), (0.28, -0.170), (0.50, -0.215), (0.72, -0.185), (0.88, -0.105), (0.97, -0.045), (1.0, 0.0)],
        back="#6A5820", side="#C8A545", belly="#EEDD9E", mark="#6A5820", fin_dark="#3E3010", tail_color="#7A5E22",
        pectoral="#B8923A", eye="#F2D06B", tail=0.16, gill=0.79,
        fins=[top_fin(0.22, 0.66, 0.085, "#7A6026", peak=0.2, lean=-0.01),
              low_fin(0.50, 0.60, 0.075, "#8A6A2A", peak=0.3, lean=-0.03), low_fin(0.20, 0.36, 0.07, "#8A6A2A", peak=0.25, lean=-0.02)]),
    "perch": dict(
        top=[(0.0, 0.035), (0.10, 0.065), (0.30, 0.125), (0.55, 0.170), (0.72, 0.160), (0.88, 0.090), (0.97, 0.035), (1.0, 0.0)],
        bottom=[(0.0, -0.035), (0.10, -0.060), (0.30, -0.105), (0.55, -0.135), (0.72, -0.125), (0.88, -0.080), (0.97, -0.040), (1.0, 0.0)],
        back="#3E5828", side="#A7B04A", belly="#F1EABC", mark="#22301A", mark_alpha=0.55, fin_dark="#2A1A12", tail_color="#D8502C",
        pectoral="#E8A05A", eye="#F2B23A", tail=0.14,
        bars=[(0.18, 0.030, 0.16), (0.30, 0.034, 0.19), (0.42, 0.036, 0.21), (0.54, 0.036, 0.22), (0.66, 0.034, 0.20), (0.77, 0.028, 0.15)],
        fins=[top_fin(0.48, 0.74, 0.105, "#5A6A3A", peak=0.3, spot=True), top_fin(0.24, 0.46, 0.075, "#7A8048", peak=0.25, lean=-0.01),
              low_fin(0.52, 0.62, 0.075, "#E0522E", peak=0.3, lean=-0.03), low_fin(0.24, 0.38, 0.07, "#E0522E", peak=0.25, lean=-0.02)]),
    "bream": dict(
        top=[(0.0, 0.040), (0.10, 0.090), (0.28, 0.180), (0.50, 0.225), (0.70, 0.195), (0.86, 0.110), (0.96, 0.042), (1.0, 0.0)],
        bottom=[(0.0, -0.040), (0.10, -0.085), (0.28, -0.165), (0.50, -0.205), (0.70, -0.178), (0.86, -0.100), (0.96, -0.042), (1.0, 0.0)],
        back="#57524A", side="#B7AE9E", belly="#EAE5DA", mark="#57524A", fin_dark="#26262A", tail_color="#4C4C4E",
        pectoral="#8A8478", eye="#D9C27A", tail=0.17, gill=0.83, eye_x=0.93, eye_r=0.019,
        fins=[top_fin(0.42, 0.58, 0.075, "#4E4E50", peak=0.2, lean=-0.03),
              low_fin(0.50, 0.60, 0.06, "#5A5A5C", peak=0.3, lean=-0.03), low_fin(0.12, 0.44, 0.055, "#4E4E50", peak=0.85, lean=0.0)]),
    "zander": dict(
        top=[(0.0, 0.030), (0.10, 0.050), (0.30, 0.085), (0.55, 0.105), (0.75, 0.098), (0.90, 0.062), (0.975, 0.026), (1.0, 0.0)],
        bottom=[(0.0, -0.030), (0.10, -0.048), (0.30, -0.078), (0.55, -0.095), (0.75, -0.088), (0.90, -0.060), (0.975, -0.030), (1.0, 0.0)],
        back="#525E46", side="#B5BB98", belly="#F1F0E3", mark="#2E3828", mark_alpha=0.38, fin_dark="#3A3A2A", tail_color="#8E9070",
        pectoral="#C8C49A", eye="#DDE4EA", tail=0.13, eye_r=0.025, mouth=0.085, gill=0.80,
        bars=[(0.16, 0.022, 0.09), (0.26, 0.024, 0.11), (0.36, 0.026, 0.12), (0.46, 0.026, 0.13), (0.56, 0.026, 0.13), (0.66, 0.024, 0.12),
              (0.75, 0.022, 0.10)],
        fins=[top_fin(0.52, 0.74, 0.085, "#7A8062", peak=0.3), top_fin(0.24, 0.48, 0.07, "#8E9070", peak=0.25, lean=-0.01),
              low_fin(0.54, 0.63, 0.06, "#B8B48A", peak=0.3, lean=-0.03), low_fin(0.24, 0.38, 0.06, "#B8B48A", peak=0.25, lean=-0.02)]),
    "pike": dict(
        top=[(0.0, 0.030), (0.10, 0.052), (0.30, 0.082), (0.55, 0.092), (0.74, 0.086), (0.86, 0.060), (0.94, 0.034), (1.0, 0.012)],
        bottom=[(0.0, -0.030), (0.10, -0.052), (0.30, -0.082), (0.55, -0.092), (0.74, -0.084), (0.86, -0.062), (0.94, -0.040), (1.0, -0.012)],
        back="#47582C", side="#8C984A", belly="#E9E7CA", mark="#E4DE9A", mark_alpha=0.62, fin_dark="#3A2412", tail_color="#9A5C30",
        pectoral="#B8823E", eye="#F2D24A", tail=0.14, eye_x=0.86, eye_r=0.02, mouth=0.14, gill=0.76,
        spots=[(0.14, 0.012, 0.022, 0.011), (0.22, -0.020, 0.024, 0.011), (0.29, 0.030, 0.026, 0.012), (0.36, -0.010, 0.026, 0.012),
               (0.43, 0.034, 0.028, 0.012), (0.50, -0.026, 0.028, 0.012), (0.57, 0.022, 0.026, 0.012), (0.64, -0.016, 0.026, 0.011),
               (0.70, 0.034, 0.022, 0.010), (0.47, 0.004, 0.020, 0.009), (0.32, -0.044, 0.020, 0.009), (0.60, -0.050, 0.018, 0.008)],
        fins=[top_fin(0.10, 0.26, 0.085, "#9A5C30", peak=0.3, lean=-0.02),
              low_fin(0.50, 0.59, 0.06, "#B8703A", peak=0.3, lean=-0.03), low_fin(0.10, 0.25, 0.075, "#9A5C30", peak=0.3, lean=-0.02)]),
}

folder = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "client", "Assets", "_Game", "Resources", "Art", "Fish")
os.makedirs(folder, exist_ok=True)
for name, spec in FISH.items():
    path = os.path.join(folder, name + ".png")
    draw_fish(spec).save(path, optimize=True)
    print(os.path.abspath(path))

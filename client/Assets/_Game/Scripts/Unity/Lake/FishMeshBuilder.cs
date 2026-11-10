using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>
    /// Процедурная рыба (план по моделям — docs/specs/fish-models.md). Ось тела — X: рыло в +0,5, корень хвоста в −0,5,
    /// длина тела 1; хвостовой плавник выходит за −0,5. Вся окраска — цветом вершин, текстур нет.
    ///
    /// Из чего собрана:
    /// - тело — 64 кольца по 40 вершин; у каждого вида свои кривые спины и брюха (те же, что у картинок рыбы в
    ///   tools/art/fish_icons.py) и своя толщина; сечение — не эллипс, а «капля»: шире в верхней половине;
    /// - окраска: спина — бок — брюхо, «чешуя» шахматным отливом, боковая линия, жаберная крышка, рот; полосы у окуня
    ///   и судака, светлые пятна у щуки;
    /// - плавники — веера лучей (видны отдельные лучи, край зубчатый); у окуня и судака первый спинной — колючий,
    ///   у окуня с чёрным пятном; хвост с вырезкой; грудные и брюшные — парные, отведены от тела;
    /// - глаза — выпуклые: ободок, радужка своего цвета, зрачок и блик.
    /// Анимацию (изгиб тела) делает шейдер VertexColorLit по координате X — она от сетки не зависит.
    /// </summary>
    public static class FishMeshBuilder
    {
        private const int Rings = 64;
        private const int Segments = 40;

        private sealed class Fin
        {
            public bool Top;
            /// <summary>Основание плавника по телу: от U0 (ближе к хвосту) до U1 (ближе к голове), доли длины.</summary>
            public float U0, U1;
            public float Height;
            public Color Color;
            /// <summary>Колючий плавник: лучи-колючки выступают над перепонкой.</summary>
            public bool Spiny;
            /// <summary>Чёрное пятно у заднего края (первый спинной окуня).</summary>
            public bool Spot;
        }

        private sealed class Look
        {
            public Vector2[] Top, Bottom;
            /// <summary>Толщина тела относительно его высоты: лещ плоский (0,26), щука круглая (0,62).</summary>
            public float Width = 0.42f;
            public Color Back, Flank, Belly, Tail, Pectoral, Eye, Mark;
            public float MarkAlpha = 0.5f;
            public float TailLength = 0.22f, TailSpread = 0.15f, Fork = 0.45f;
            public float Gill = 0.80f, EyeU = 0.91f, EyeY = 0.38f, EyeR = 0.022f, Mouth = 0.05f;
            /// <summary>Тёмные поперечные полосы: центры по длине, полуширина и до какой высоты бока доходят (0 — брюхо).</summary>
            public float[] Bars;
            public float BarWidth = 0.03f, BarFloor = 0.3f;
            public bool Spots;
            /// <summary>Сила шахматного отлива «чешуи».</summary>
            public float Scales = 0.035f;
            public Fin[] Fins;
            public Fin Pelvic;
        }

        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            ["roach"] = new Look
            {
                Top = P(0.0f, 0.035f, 0.10f, 0.060f, 0.30f, 0.115f, 0.52f, 0.150f, 0.74f, 0.125f, 0.90f, 0.075f, 0.975f, 0.030f, 1.0f, 0.0f),
                Bottom = P(0.0f, -0.035f, 0.10f, -0.055f, 0.30f, -0.105f, 0.52f, -0.140f, 0.74f, -0.120f, 0.90f, -0.075f, 0.975f, -0.035f, 1.0f, 0.0f),
                Width = 0.40f, Back = H("#4E6672"), Flank = H("#C6D3D9"), Belly = H("#F4F5F1"), Tail = H("#B4573C"),
                Pectoral = H("#E8805A"), Eye = H("#F08A2A"), Mark = H("#58727E"), TailSpread = 0.15f, Fork = 0.50f, Scales = 0.04f,
                Fins = new[]
                {
                    new Fin { Top = true, U0 = 0.42f, U1 = 0.62f, Height = 0.095f, Color = H("#7A7468") },
                    new Fin { Top = false, U0 = 0.24f, U1 = 0.40f, Height = 0.070f, Color = H("#E0623A") },
                },
                Pelvic = new Fin { U0 = 0.50f, U1 = 0.58f, Height = 0.085f, Color = H("#E0623A") },
            },
            ["crucian"] = new Look
            {
                Top = P(0.0f, 0.045f, 0.10f, 0.090f, 0.28f, 0.185f, 0.50f, 0.235f, 0.72f, 0.200f, 0.88f, 0.110f, 0.97f, 0.040f, 1.0f, 0.0f),
                Bottom = P(0.0f, -0.045f, 0.10f, -0.085f, 0.28f, -0.170f, 0.50f, -0.215f, 0.72f, -0.185f, 0.88f, -0.105f, 0.97f, -0.045f, 1.0f, 0.0f),
                Width = 0.38f, Back = H("#5E4E1C"), Flank = H("#C8A545"), Belly = H("#EEDD9E"), Tail = H("#7A5E22"),
                Pectoral = H("#B8923A"), Eye = H("#F2D06B"), Mark = H("#6A5820"), TailSpread = 0.16f, Fork = 0.22f, Gill = 0.79f,
                Scales = 0.055f,
                Fins = new[]
                {
                    new Fin { Top = true, U0 = 0.22f, U1 = 0.66f, Height = 0.085f, Color = H("#7A6026") },
                    new Fin { Top = false, U0 = 0.20f, U1 = 0.36f, Height = 0.075f, Color = H("#8A6A2A") },
                },
                Pelvic = new Fin { U0 = 0.50f, U1 = 0.58f, Height = 0.085f, Color = H("#8A6A2A") },
            },
            ["perch"] = new Look
            {
                Top = P(0.0f, 0.035f, 0.10f, 0.065f, 0.30f, 0.125f, 0.55f, 0.170f, 0.72f, 0.160f, 0.88f, 0.090f, 0.97f, 0.035f, 1.0f, 0.0f),
                Bottom = P(0.0f, -0.035f, 0.10f, -0.060f, 0.30f, -0.105f, 0.55f, -0.135f, 0.72f, -0.125f, 0.88f, -0.080f, 0.97f, -0.040f, 1.0f, 0.0f),
                Width = 0.46f, Back = H("#364E22"), Flank = H("#A7B04A"), Belly = H("#F1EABC"), Tail = H("#D8502C"),
                Pectoral = H("#E8A05A"), Eye = H("#F2B23A"), Mark = H("#1E2A16"), MarkAlpha = 0.62f, TailSpread = 0.14f, Fork = 0.35f,
                Bars = new[] { 0.18f, 0.30f, 0.42f, 0.54f, 0.66f, 0.77f }, BarWidth = 0.034f, BarFloor = 0.22f,
                Fins = new[]
                {
                    new Fin { Top = true, U0 = 0.48f, U1 = 0.76f, Height = 0.120f, Color = H("#5A6A3A"), Spiny = true, Spot = true },
                    new Fin { Top = true, U0 = 0.24f, U1 = 0.46f, Height = 0.085f, Color = H("#7A8048") },
                    new Fin { Top = false, U0 = 0.24f, U1 = 0.38f, Height = 0.075f, Color = H("#E0522E") },
                },
                Pelvic = new Fin { U0 = 0.52f, U1 = 0.60f, Height = 0.090f, Color = H("#E0522E") },
            },
            ["bream"] = new Look
            {
                Top = P(0.0f, 0.040f, 0.10f, 0.090f, 0.28f, 0.180f, 0.50f, 0.225f, 0.70f, 0.195f, 0.86f, 0.110f, 0.96f, 0.042f, 1.0f, 0.0f),
                Bottom = P(0.0f, -0.040f, 0.10f, -0.085f, 0.28f, -0.165f, 0.50f, -0.205f, 0.70f, -0.178f, 0.86f, -0.100f, 0.96f, -0.042f, 1.0f, 0.0f),
                Width = 0.26f, Back = H("#4A463E"), Flank = H("#B7AE9E"), Belly = H("#EAE5DA"), Tail = H("#4C4C4E"),
                Pectoral = H("#8A8478"), Eye = H("#D9C27A"), Mark = H("#57524A"), TailSpread = 0.18f, Fork = 0.58f, Gill = 0.83f,
                EyeU = 0.93f, EyeR = 0.019f, Scales = 0.045f,
                Fins = new[]
                {
                    new Fin { Top = true, U0 = 0.42f, U1 = 0.58f, Height = 0.110f, Color = H("#4E4E50") },
                    new Fin { Top = false, U0 = 0.12f, U1 = 0.44f, Height = 0.065f, Color = H("#4E4E50") },
                },
                Pelvic = new Fin { U0 = 0.50f, U1 = 0.58f, Height = 0.080f, Color = H("#5A5A5C") },
            },
            ["zander"] = new Look
            {
                Top = P(0.0f, 0.030f, 0.10f, 0.050f, 0.30f, 0.085f, 0.55f, 0.105f, 0.75f, 0.098f, 0.90f, 0.062f, 0.975f, 0.026f, 1.0f, 0.0f),
                Bottom = P(0.0f, -0.030f, 0.10f, -0.048f, 0.30f, -0.078f, 0.55f, -0.095f, 0.75f, -0.088f, 0.90f, -0.060f, 0.975f, -0.030f, 1.0f, 0.0f),
                Width = 0.56f, Back = H("#465240"), Flank = H("#B5BB98"), Belly = H("#F1F0E3"), Tail = H("#8E9070"),
                Pectoral = H("#C8C49A"), Eye = H("#C9C8A6"), Mark = H("#2E3828"), MarkAlpha = 0.42f, TailSpread = 0.13f, Fork = 0.40f,
                EyeR = 0.025f, Mouth = 0.085f,
                Bars = new[] { 0.16f, 0.26f, 0.36f, 0.46f, 0.56f, 0.66f, 0.75f }, BarWidth = 0.026f, BarFloor = 0.42f,
                Fins = new[]
                {
                    new Fin { Top = true, U0 = 0.52f, U1 = 0.76f, Height = 0.095f, Color = H("#7A8062"), Spiny = true },
                    new Fin { Top = true, U0 = 0.24f, U1 = 0.48f, Height = 0.075f, Color = H("#8E9070") },
                    new Fin { Top = false, U0 = 0.24f, U1 = 0.38f, Height = 0.062f, Color = H("#B8B48A") },
                },
                Pelvic = new Fin { U0 = 0.54f, U1 = 0.62f, Height = 0.075f, Color = H("#B8B48A") },
            },
            ["pike"] = new Look
            {
                Top = P(0.0f, 0.030f, 0.10f, 0.052f, 0.30f, 0.082f, 0.55f, 0.092f, 0.74f, 0.086f, 0.86f, 0.060f, 0.94f, 0.034f, 1.0f, 0.010f),
                Bottom = P(0.0f, -0.030f, 0.10f, -0.052f, 0.30f, -0.082f, 0.55f, -0.092f, 0.74f, -0.084f, 0.86f, -0.062f, 0.94f, -0.040f, 1.0f, -0.012f),
                Width = 0.62f, Back = H("#3C4C24"), Flank = H("#8C984A"), Belly = H("#E9E7CA"), Tail = H("#9A5C30"),
                Pectoral = H("#B8823E"), Eye = H("#F2D24A"), Mark = H("#E4DE9A"), MarkAlpha = 0.70f, TailSpread = 0.14f, Fork = 0.40f,
                Gill = 0.76f, EyeU = 0.86f, EyeR = 0.020f, Mouth = 0.14f, Spots = true, Scales = 0.03f,
                Fins = new[]
                {
                    new Fin { Top = true, U0 = 0.10f, U1 = 0.26f, Height = 0.095f, Color = H("#9A5C30") },
                    new Fin { Top = false, U0 = 0.10f, U1 = 0.25f, Height = 0.080f, Color = H("#9A5C30") },
                },
                Pelvic = new Fin { U0 = 0.44f, U1 = 0.52f, Height = 0.075f, Color = H("#B8703A") },
            },
        };

        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>Длина рыбы по весу, м: длина растёт как кубический корень массы.</summary>
        public static float LengthMeters(string speciesId, int weightG)
        {
            float k;
            switch (speciesId)
            {
                case "pike": k = 0.0215f; break;
                case "zander": k = 0.0200f; break;
                case "bream": k = 0.0160f; break;
                case "crucian": k = 0.0140f; break;
                case "roach": k = 0.0170f; break;
                default: k = 0.0158f; break;
            }
            return k * Mathf.Pow(weightG, 1f / 3f) * 2f;
        }

        public static Mesh Build(string speciesId)
        {
            // Кэш static переживает вход в Play Mode (домен не перезагружается), а сетка прошлого запуска может быть
            // уже уничтожена (Unity-null) — тогда строим заново.
            if (Cache.TryGetValue(speciesId, out var cached) && cached != null)
                return cached;
            if (!Looks.TryGetValue(speciesId, out var look))
                look = Looks["perch"];

            var b = new Builder();
            Body(b, look);
            Tail(b, look);
            foreach (var fin in look.Fins)
                MedianFin(b, look, fin);
            PairedFins(b, look);
            Eyes(b, look);
            var mesh = b.ToMesh(speciesId);
            Cache[speciesId] = mesh;
            return mesh;
        }

        // ---------- Профиль тела ----------

        /// <summary>Значение кривой профиля в точке u (0 — корень хвоста, 1 — рыло): сглаженная линия через точки.</summary>
        private static float Curve(Vector2[] points, float u)
        {
            u = Mathf.Clamp01(u);
            int n = points.Length;
            int i = 0;
            while (i < n - 2 && u > points[i + 1].x)
                i++;
            var p0 = points[Mathf.Max(i - 1, 0)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Mathf.Min(i + 2, n - 1)];
            float t = Mathf.InverseLerp(p1.x, p2.x, u);
            // Наклоны в узлах — по соседям (как у сплайна Катмулла — Рома), в координатах u.
            float m1 = (p2.y - p0.y) / Mathf.Max(1e-4f, p2.x - p0.x) * (p2.x - p1.x);
            float m2 = (p3.y - p1.y) / Mathf.Max(1e-4f, p3.x - p1.x) * (p2.x - p1.x);
            float t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * p1.y + (t3 - 2 * t2 + t) * m1 + (-2 * t3 + 3 * t2) * p2.y + (t3 - t2) * m2;
        }

        private static float TopY(Look look, float u) => Curve(look.Top, u);
        private static float BottomY(Look look, float u) => Curve(look.Bottom, u);

        /// <summary>Полутолщина тела в точке u: к хвосту рыба уплощается с боков.</summary>
        private static float HalfWidth(Look look, float u)
        {
            float height = TopY(look, u) - BottomY(look, u);
            float taper = Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.5f, u)));
            return 0.5f * look.Width * height * taper;
        }

        /// <summary>Угол вершины сечения по номеру: вершины гуще на боках, чтобы узор по высоте был ровнее.</summary>
        private static float SectionAngle(int j)
        {
            int half = Segments / 2;
            int k = j <= half ? j : Segments - j;
            float s = k / (float)half;
            float a = Mathf.Lerp(Mathf.PI * s, Mathf.Acos(1f - 2f * s), 0.6f);
            return j <= half ? a : -a;
        }

        /// <summary>Точка поверхности тела: u вдоль тела, a — угол в сечении (0 — хребет, ±π — киль).</summary>
        private static Vector3 Surface(Look look, float u, float a)
        {
            float top = TopY(look, u), bottom = BottomY(look, u);
            float yc = (top + bottom) * 0.5f, hh = (top - bottom) * 0.5f;
            float c = Mathf.Cos(a), sn = Mathf.Sin(a);
            // «Капля»: бока полнее эллипса, верхняя половина шире нижней.
            float z = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.85f) * HalfWidth(look, u) * (1f + 0.12f * c);
            return new Vector3(u - 0.5f, yc + c * hh, z);
        }

        private static void Body(Builder b, Look look)
        {
            int first = b.Count;
            int half = Segments / 2;
            // Ряды вершин, по которым идут боковая линия и рот (ближайшие к нужной высоте бока).
            int lateralRow = NearestRow(0.54f);
            int mouthRow = NearestRow(0.43f);
            var grid = new Vector3[Rings + 1, Segments];
            for (int i = 0; i <= Rings; i++)
            {
                float u = i / (float)Rings;
                for (int j = 0; j < Segments; j++)
                    grid[i, j] = Surface(look, u, SectionAngle(j));
            }

            for (int i = 0; i <= Rings; i++)
            {
                float u = i / (float)Rings;
                for (int j = 0; j < Segments; j++)
                {
                    var du = grid[Mathf.Min(i + 1, Rings), j] - grid[Mathf.Max(i - 1, 0), j];
                    var da = grid[i, (j + 1) % Segments] - grid[i, (j + Segments - 1) % Segments];
                    var normal = Vector3.Cross(da, du);
                    if (normal.sqrMagnitude < 1e-12f)
                        normal = new Vector3(i == Rings ? 1f : -1f, 0f, 0f);
                    int row = j <= half ? j : Segments - j;
                    float t = (Mathf.Cos(SectionAngle(j)) + 1f) * 0.5f;
                    b.Add(grid[i, j], normal.normalized, BodyColor(look, u, t, i, row, row == lateralRow, row == mouthRow));
                }
            }

            for (int i = 0; i < Rings; i++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    int a = first + i * Segments + j;
                    int c = first + i * Segments + (j + 1) % Segments;
                    b.Quad(a, c, a + Segments, c + Segments);
                }
            }
        }

        private static int NearestRow(float t)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int j = 0; j <= Segments / 2; j++)
            {
                float distance = Mathf.Abs((Mathf.Cos(SectionAngle(j)) + 1f) * 0.5f - t);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = j;
                }
            }

            return best;
        }

        /// <summary>Цвет вершины тела. t — высота в сечении: 1 — хребет, 0 — киль.</summary>
        private static Color BodyColor(Look look, float u, float t, int ring, int row, bool lateral, bool mouth)
        {
            var color = t > 0.62f
                ? Color.Lerp(look.Flank, look.Back, Mathf.SmoothStep(0f, 1f, (t - 0.62f) / 0.33f))
                : Color.Lerp(look.Belly, look.Flank, Mathf.SmoothStep(0f, 1f, t / 0.52f));

            bool head = u > look.Gill;
            // Чешуя: шахматный отлив на боках, слабее к брюху и к голове.
            if (!head && u > 0.06f && t > 0.12f && t < 0.92f)
            {
                float fade = Mathf.Clamp01((look.Gill - u) / 0.06f) * Mathf.Clamp01((u - 0.06f) / 0.06f);
                color *= 1f + (((ring + row) & 1) == 0 ? look.Scales : -look.Scales) * fade;
            }

            // Поперечные полосы (окунь, судак): шире у спины, сходят на нет к брюху.
            if (look.Bars != null && !head && t > look.BarFloor)
            {
                float reach = Mathf.Clamp01((t - look.BarFloor) / 0.22f);
                float width = look.BarWidth * (0.55f + 0.45f * t);
                foreach (float center in look.Bars)
                {
                    float d = Mathf.Abs(u - center);
                    if (d < width)
                        color = Color.Lerp(color, look.Mark, look.MarkAlpha * reach * Mathf.SmoothStep(0f, 1f, 1f - d / width));
                }
            }

            // Светлые пятна щуки: овалы в шахматном порядке по бокам.
            if (look.Spots && !head && u > 0.06f && t > 0.20f && t < 0.86f)
            {
                const float stepU = 0.07f, stepT = 0.165f;
                int rowIndex = Mathf.FloorToInt(t / stepT);
                float shifted = u + (rowIndex % 2 == 0 ? 0f : stepU * 0.5f);
                int column = Mathf.FloorToInt(shifted / stepU);
                float jitter = Mathf.Sin(column * 12.9898f + rowIndex * 78.233f) * 0.5f;
                float cu = (column + 0.5f + jitter * 0.3f) * stepU;
                float ct = (rowIndex + 0.5f) * stepT;
                float du = (shifted - cu) / 0.024f, dt = (t - ct) / 0.058f;
                if (du * du + dt * dt < 1f)
                    color = Color.Lerp(color, look.Mark, look.MarkAlpha);
            }

            // Боковая линия.
            if (lateral && !head && u > 0.08f)
                color *= 0.84f;

            // Жаберная крышка: тёмная дуга, выгнутая к хвосту; голова чуть темнее тела.
            float arc = look.Gill - 0.035f * (1f - (2f * t - 1f) * (2f * t - 1f));
            if (t > 0.16f && t < 0.84f)
            {
                float near = 1f - Mathf.Abs(u - arc) / (1.3f / Rings);
                if (near > 0f)
                    color *= 1f - 0.36f * Mathf.SmoothStep(0f, 1f, near);
            }
            if (head)
                color = Color.Lerp(color, look.Back, 0.10f + 0.14f * t);

            // Рот: тёмная щель от кончика рыла назад.
            if (mouth && u > 1f - look.Mouth && u < 0.995f)
                color = Color.Lerp(color, new Color(0.13f, 0.12f, 0.10f), 0.85f);
            return color;
        }

        // ---------- Плавники ----------

        /// <summary>Хвост: веер лучей от стебля, с вырезкой посередине.</summary>
        private static void Tail(Builder b, Look look)
        {
            const int rays = 17;
            float stem = TopY(look, 0f) * 0.95f;
            float maxAngle = Mathf.Atan2(look.TailSpread, look.TailLength * 0.9f);
            var bases = new Vector3[rays];
            var tips = new Vector3[rays];
            for (int i = 0; i < rays; i++)
            {
                float k = i / (float)(rays - 1) * 2f - 1f;          // −1 — нижний край, +1 — верхний
                float angle = k * maxAngle;
                float length = look.TailLength * (1f - look.Fork * (1f - Mathf.Pow(Mathf.Abs(k), 1.4f)));
                bases[i] = new Vector3(-0.49f, k * stem, 0f);
                tips[i] = bases[i] + new Vector3(-Mathf.Cos(angle), Mathf.Sin(angle), 0f) * length;
            }

            RayFan(b, bases, tips, look.Tail * 0.86f, look.Tail, 0.18f, false, 0f);
        }

        /// <summary>Спинной или анальный плавник: основание по хребту или килю, передний край (у головы) — самый высокий.</summary>
        private static void MedianFin(Builder b, Look look, Fin fin)
        {
            int rays = Mathf.Clamp(Mathf.RoundToInt((fin.U1 - fin.U0) / 0.018f), 7, 22);
            if (fin.Spiny)
                rays = rays * 2 - 1;        // между колючками — вершины перепонки
            var bases = new Vector3[rays];
            var tips = new Vector3[rays];
            float sign = fin.Top ? 1f : -1f;
            for (int i = 0; i < rays; i++)
            {
                float k = i / (float)(rays - 1);                    // 0 — у головы, 1 — у хвоста
                float u = Mathf.Lerp(fin.U1, fin.U0, k);
                float edge = fin.Top ? TopY(look, u) : BottomY(look, u);
                // Высота: быстро набирается у переднего края и плавно спадает к заднему.
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.16f));
                float fall = 1f - 0.72f * Mathf.Pow(k, 1.3f);
                float height = fin.Height * rise * fall;
                if (fin.Spiny && i % 2 == 1)
                    height *= 0.80f;
                else if (!fin.Spiny && i % 2 == 1)
                    height *= 0.95f;
                float lean = 0.35f * height + 0.02f * k;            // лучи наклонены к хвосту
                bases[i] = new Vector3(u - 0.5f, edge * 0.94f, 0f);
                tips[i] = new Vector3(u - 0.5f - lean, edge * 0.94f + sign * height, 0f);
            }

            RayFan(b, bases, tips, fin.Color * 0.88f, fin.Color, fin.Spiny ? 0.12f : 0.16f, fin.Spot, 0.72f);
        }

        /// <summary>Грудные и брюшные плавники: парные веера, отведены от тела в стороны.</summary>
        private static void PairedFins(Builder b, Look look)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                // Грудной — сразу за жаберной крышкой, ниже середины бока; смотрит назад, вниз и наружу.
                float u = look.Gill - 0.035f;
                float a = side * Mathf.PI * 0.66f;
                var root = Surface(look, u, a);
                var forward = new Vector3(-0.88f, -0.30f, 0.26f * side).normalized;
                var across = new Vector3(0.22f, -0.88f, -0.30f * side).normalized;
                Fan(b, root, forward, across, 0.135f + 0.06f * (TopY(look, u) - BottomY(look, u)), 0.022f, 19f, 9,
                    look.Pectoral * 0.92f, look.Pectoral);

                // Брюшной — под брюхом, чуть в стороне от киля; смотрит назад и вниз.
                var pelvic = look.Pelvic;
                float pu = (pelvic.U0 + pelvic.U1) * 0.5f;
                var pelvicRoot = Surface(look, pu, side * Mathf.PI * 0.90f);
                var back = new Vector3(-0.84f, -0.50f, 0.18f * side).normalized;
                var wide = new Vector3(0.50f, -0.84f, -0.18f * side).normalized;
                Fan(b, pelvicRoot, back, wide, pelvic.Height * 1.25f, 0.020f, 20f, 8, pelvic.Color * 0.90f, pelvic.Color);
            }
        }

        /// <summary>Веер лучей из узкого основания: длина length, раскрытие ±spreadDeg вокруг forward в сторону across.</summary>
        private static void Fan(Builder b, Vector3 root, Vector3 forward, Vector3 across, float length, float rootWidth, float spreadDeg,
            int rays, Color baseColor, Color tipColor)
        {
            var bases = new Vector3[rays];
            var tips = new Vector3[rays];
            for (int i = 0; i < rays; i++)
            {
                float k = i / (float)(rays - 1) * 2f - 1f;
                float angle = k * spreadDeg * Mathf.Deg2Rad;
                var direction = forward * Mathf.Cos(angle) + across * Mathf.Sin(angle);
                float reach = length * (1f - 0.48f * k * k) * (i % 2 == 1 ? 0.96f : 1f);
                bases[i] = root + across * (k * rootWidth * 0.5f);
                tips[i] = bases[i] + direction * reach;
            }

            RayFan(b, bases, tips, baseColor, tipColor, 0.14f, false, 0f);
        }

        /// <summary>
        /// Плавник из лучей: между соседними лучами — четырёхугольник; лучи через один темнее (видны как отдельные),
        /// у основания плавник темнее, к краю светлее. spot — чёрное пятно на лучах после доли spotFrom.
        /// </summary>
        private static void RayFan(Builder b, Vector3[] bases, Vector3[] tips, Color baseColor, Color tipColor, float rayShade, bool spot,
            float spotFrom)
        {
            int n = bases.Length;
            var normal = Vector3.up;
            int first = b.Count;
            for (int i = 0; i < n; i++)
            {
                float shade = i % 2 == 0 ? 1f - rayShade : 1f;
                var root = baseColor * shade;
                var tip = tipColor * shade;
                var middle = Color.Lerp(root, tip, 0.5f);
                float k = i / (float)(n - 1);
                if (spot && k > spotFrom)
                {
                    float strength = Mathf.SmoothStep(0f, 1f, (k - spotFrom) / 0.10f) * 0.9f;
                    var black = new Color(0.05f, 0.06f, 0.04f);
                    root = Color.Lerp(root, black, strength);
                    middle = Color.Lerp(middle, black, strength);
                    tip = Color.Lerp(tip, black, strength * 0.5f);
                }

                b.Add(bases[i], normal, root);
                b.Add(Vector3.Lerp(bases[i], tips[i], 0.55f), normal, middle);
                b.Add(tips[i], normal, tip * 1.06f);
            }

            for (int i = 0; i < n - 1; i++)
            {
                int a = first + i * 3;
                b.Quad(a, a + 3, a + 1, a + 4);
                b.Quad(a + 1, a + 4, a + 2, a + 5);
            }
        }

        // ---------- Глаза ----------

        /// <summary>Выпуклый глаз: тёмный ободок, радужка, зрачок и блик.</summary>
        private static void Eyes(Builder b, Look look)
        {
            const int around = 14, rings = 5;
            float u = look.EyeU;
            float top = TopY(look, u), bottom = BottomY(look, u);
            float y = top * look.EyeY;
            float yc = (top + bottom) * 0.5f, hh = Mathf.Max(1e-4f, (top - bottom) * 0.5f);
            float angle = Mathf.Acos(Mathf.Clamp((y - yc) / hh, -1f, 1f));
            float radius = look.EyeR * 1.25f;
            var dark = new Color(0.07f, 0.08f, 0.06f);
            var pupil = new Color(0.02f, 0.02f, 0.02f);
            for (int side = -1; side <= 1; side += 2)
            {
                var onBody = Surface(look, u, side * angle);
                var center = new Vector3(onBody.x, onBody.y, onBody.z - side * radius * 0.25f);
                int first = b.Count;
                for (int r = 0; r <= rings; r++)
                {
                    float lat = r / (float)rings * Mathf.PI * 0.5f;     // 0 — край глаза, π/2 — макушка
                    float ringRadius = radius * Mathf.Cos(lat);
                    float bulge = radius * 0.55f * Mathf.Sin(lat);
                    Color color = r == 0 ? dark : r <= 2 ? look.Eye : pupil;
                    if (r == 1)
                        color = Color.Lerp(look.Eye, dark, 0.25f);
                    for (int i = 0; i < around; i++)
                    {
                        float lon = i / (float)around * Mathf.PI * 2f;
                        var offset = new Vector3(Mathf.Cos(lon) * ringRadius, Mathf.Sin(lon) * ringRadius, side * bulge);
                        var normal = new Vector3(Mathf.Cos(lon) * Mathf.Cos(lat), Mathf.Sin(lon) * Mathf.Cos(lat), side * Mathf.Sin(lat) + side * 0.4f);
                        b.Add(center + offset, normal.normalized, color);
                    }
                }

                for (int r = 0; r < rings; r++)
                {
                    for (int i = 0; i < around; i++)
                    {
                        int a = first + r * around + i;
                        int c = first + r * around + (i + 1) % around;
                        b.Quad(a, c, a + around, c + around);
                    }
                }

                // Блик — маленький светлый ромб вверху спереди.
                var glint = center + new Vector3(radius * 0.30f, radius * 0.34f, side * radius * 0.56f);
                float g = radius * 0.20f;
                b.Polygon(new[]
                {
                    glint + new Vector3(-g, 0f, 0f), glint + new Vector3(0f, g, 0f), glint + new Vector3(g, 0f, 0f), glint + new Vector3(0f, -g, 0f),
                }, Color.white);
            }
        }

        // ---------- Мелочи ----------

        private static Vector2[] P(params float[] values)
        {
            var points = new Vector2[values.Length / 2];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector2(values[2 * i], values[2 * i + 1]);
            return points;
        }

        private static Color H(string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var color))
                throw new ArgumentException("Плохой цвет: " + hex);
            return color;
        }

        private sealed class Builder
        {
            private readonly List<Vector3> _positions = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<int> _triangles = new List<int>();

            public int Count => _positions.Count;

            public void Add(Vector3 position, Vector3 normal, Color color)
            {
                _positions.Add(position);
                _normals.Add(normal);
                color.a = 1f;
                _colors.Add(color);
            }

            public void Quad(int a, int b, int c, int d)
            {
                _triangles.Add(a);
                _triangles.Add(c);
                _triangles.Add(b);
                _triangles.Add(b);
                _triangles.Add(c);
                _triangles.Add(d);
            }

            /// <summary>Плоский выпуклый многоугольник веером (двусторонний: шейдер без отсечения граней).</summary>
            public void Polygon(Vector3[] points, Color color)
            {
                int start = Count;
                var normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]).normalized;
                if (normal == Vector3.zero)
                    normal = Vector3.forward;
                foreach (var p in points)
                    Add(p, normal, color);
                for (int i = 1; i < points.Length - 1; i++)
                {
                    _triangles.Add(start);
                    _triangles.Add(start + i);
                    _triangles.Add(start + i + 1);
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = "Fish_" + name };
                mesh.SetVertices(_positions);
                mesh.SetNormals(_normals);
                mesh.SetColors(_colors);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}

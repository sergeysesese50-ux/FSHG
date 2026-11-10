using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fishing.Unity
{
    /// <summary>
    /// Строит тестовое озеро кодом, без ассетов: вода, ближний берег-залив, дальний берег слоями силуэтов
    /// (три гряды холмов в дымке, четыре ряда леса, кусты у воды) и камыш в кадре у ног рыбака.
    /// Рыбак стоит в точке (0, 0, 0) на кромке воды и смотрит в +Z.
    /// Кромка ближнего берега изогнута заливом: z = 0,004·x²; дальний берег — z ≈ 170 м.
    /// </summary>
    public static class LakeBuilder
    {
        public const float FarShoreZ = 170f;

        /// <summary>Глаз рыбака: здесь стоит камера (TestLake), от неё считается край кадра для камыша.</summary>
        public static readonly Vector3 Eye = new Vector3(0f, 1.6f, -1.2f);

        // По этим числам вода рисует отражение леса и холмов (Water.shader): плоскость и высота.
        private const float TreeLineZ = 177f;
        private const float TreeLineHeight = 16f;
        private const float HillLineZ = 420f;
        private const float HillLineHeight = 44f;

        /// <summary>
        /// Облик места ловли: только вид (правила — глубина и рыба — в shared/data/locations.json).
        /// </summary>
        public struct SpotLook
        {
            /// <summary>Зерно расстановки леса и камыша: у каждого места свой берег.</summary>
            public int Seed;
            /// <summary>Стеблей камыша в кадре слева и справа.</summary>
            public int ReedsLeft;
            public int ReedsRight;
            /// <summary>Высота холмов относительно обычной.</summary>
            public float HillScale;
            /// <summary>Долина посередине кадра (в ней встаёт солнце); false — холмы стеной.</summary>
            public bool Valley;
            /// <summary>Куда смотрит рыбак (LakeClock.ViewAzimuthDeg).</summary>
            public float ViewAzimuthDeg;
        }

        /// <summary>Облик места по id из locations.json; незнакомое место выглядит как первое.</summary>
        public static SpotLook LookFor(string spotId)
        {
            switch (spotId)
            {
                case "reeds": return new SpotLook { Seed = 21, ReedsLeft = 110, ReedsRight = 90, HillScale = 0.7f, Valley = true, ViewAzimuthDeg = 25f };
                case "old_pier": return new SpotLook { Seed = 34, ReedsLeft = 14, ReedsRight = 6, HillScale = 1.15f, Valley = false, ViewAzimuthDeg = -60f };
                case "pit": return new SpotLook { Seed = 55, ReedsLeft = 0, ReedsRight = 0, HillScale = 1.5f, Valley = false, ViewAzimuthDeg = 120f };
                case "island": return new SpotLook { Seed = 144, ReedsLeft = 20, ReedsRight = 12, HillScale = 0.8f, Valley = true, ViewAzimuthDeg = 60f };
                case "center": return new SpotLook { Seed = 233, ReedsLeft = 0, ReedsRight = 0, HillScale = 0.7f, Valley = true, ViewAzimuthDeg = 180f };
                case "backwater": return new SpotLook { Seed = 377, ReedsLeft = 140, ReedsRight = 120, HillScale = 0.6f, Valley = false, ViewAzimuthDeg = -110f };
                case "stream": return new SpotLook { Seed = 89, ReedsLeft = 30, ReedsRight = 60, HillScale = 0.9f, Valley = true, ViewAzimuthDeg = -20f };
                default: return new SpotLook { Seed = 7, ReedsLeft = 48, ReedsRight = 30, HillScale = 1f, Valley = true, ViewAzimuthDeg = 0f };
            }
        }

        /// <summary>Расстояние от точки до воды по ближнему берегу (плюс — в озеро).</summary>
        public static float ShoreDistance(float x, float z) => Mathf.Min(z - NearShoreZ(x), FarShoreZ - z);

        public static float NearShoreZ(float x) => 0.004f * x * x;

        /// <param name="frameSlope">Тангенс половины горизонтального угла обзора: по нему камыш ставится у краёв кадра.</param>
        /// <param name="look">Облик места ловли.</param>
        /// <param name="created">Сюда складываются созданные объекты — чтобы снести их при смене места.</param>
        public static void Build(Transform parent, TestLake lake, float frameSlope, SpotLook look, List<GameObject> created)
        {
            var random = new System.Random(look.Seed);
            var water = lake.WaterMaterial;
            water.SetFloat("_ShoreZ", TreeLineZ);
            water.SetFloat("_ShoreHeight", TreeLineHeight);
            water.SetFloat("_HillZ", HillLineZ);
            water.SetFloat("_HillHeight", HillLineHeight);

            created.Add(AddMesh(parent, "Water", WaterMesh(), water));
            created.Add(AddMesh(parent, "Ground", GroundMesh(), lake.LitMaterial(Color.white)));
            created.Add(AddMesh(parent, "FarShore", FarShoreMesh(random, look), lake.SilhouetteMaterial()));
            var reeds = lake.LitMaterial(Color.white);
            reeds.SetFloat("_SwayAmp", 0.07f);
            created.Add(AddMesh(parent, "Reeds", ReedsMesh(random, frameSlope, look), reeds));
        }

        private static GameObject AddMesh(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static Mesh WaterMesh()
        {
            var mesh = new Mesh { name = "Water" };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-450, 0, -20), new Vector3(450, 0, -20), new Vector3(-450, 0, 600), new Vector3(450, 0, 600),
            });
            mesh.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetTriangles(new List<int> { 0, 2, 1, 1, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Ближний берег и дно залива. Дальний берег рисуют силуэты, поэтому сетка кончается в воде перед ним.</summary>
        private static Mesh GroundMesh()
        {
            const float minX = -230, maxX = 230, minZ = -40, maxZ = 166, stepX = 4, stepZ = 2;
            int cols = Mathf.RoundToInt((maxX - minX) / stepX) + 1;
            int rows = Mathf.RoundToInt((maxZ - minZ) / stepZ) + 1;
            var vertices = new List<Vector3>(cols * rows);
            var colors = new List<Color>(cols * rows);

            var sand = new Color(0.40f, 0.34f, 0.24f);
            var grass = new Color(0.27f, 0.35f, 0.15f);
            var darkGrass = new Color(0.19f, 0.26f, 0.11f);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float x = minX + c * stepX;
                    float z = minZ + r * stepZ;
                    float lake = ShoreDistance(x, z);
                    float noise = Mathf.PerlinNoise(x * 0.07f + 13f, z * 0.07f + 7f);
                    float y;
                    if (lake < 0f)
                    {
                        float land = -lake;
                        float hills = Mathf.PerlinNoise(x * 0.02f, z * 0.02f) * Mathf.Clamp01(land / 40f) * 9f;
                        y = 0.3f + land * 0.025f + noise * 0.35f + hills;
                    }
                    else
                    {
                        y = Mathf.Max(-2.5f, 0.3f - lake * 0.45f);
                    }

                    vertices.Add(new Vector3(x, y, z));
                    float shore = Mathf.Clamp01(-lake / 3f);
                    var color = Color.Lerp(sand, Color.Lerp(grass, darkGrass, noise), shore);
                    colors.Add(color * (0.9f + 0.2f * noise));
                }
            }

            return GridMesh("Ground", vertices, colors, cols, rows);
        }

        private static Mesh GridMesh(string name, List<Vector3> vertices, List<Color> colors, int cols, int rows)
        {
            var triangles = new List<int>((cols - 1) * (rows - 1) * 6);
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    int i = r * cols + c;
                    triangles.Add(i);
                    triangles.Add(i + cols);
                    triangles.Add(i + 1);
                    triangles.Add(i + 1);
                    triangles.Add(i + cols);
                    triangles.Add(i + cols + 1);
                }
            }

            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Дальний берег: плоские силуэты лицом к рыбаку. Цвет вершины — оттенок (умножается на цвет берега
        /// из LakeClock), альфа — доля дымки: чем дальше слой, тем сильнее он растворён в небе.
        /// Посередине холмы ниже — долина, из которой встаёт солнце.
        /// </summary>
        private static Mesh FarShoreMesh(System.Random random, SpotLook look)
        {
            var shape = new Silhouette();
            float hill = look.HillScale;
            float offset = look.Seed * 3.7f;              // у каждого места свои очертания холмов
            bool valley = look.Valley;
            float Dip(float x, float z) => valley ? Valley(x, z) : 1f;

            shape.Ridge(900f, 820f, 12f, x => Dip(x, 900f) * hill * (85f + 75f * Fbm(x * 0.0022f + 5f + offset)), 0.86f, 0.96f);
            shape.Ridge(560f, 560f, 6f, x => Dip(x, 560f) * hill * (45f + 50f * Fbm(x * 0.004f + 17f + offset)) + 2f * Mathf.PerlinNoise(x * 0.12f, 3f), 0.68f, 0.88f);
            shape.Ridge(330f, 380f, 2f, x => Dip(x, 330f) * hill * (22f + 22f * Fbm(x * 0.007f + 31f + offset)) + WoodedTop(x), 0.46f, 0.70f);

            float[] rowZ = { 214f, 200f, 188f, TreeLineZ };
            float[] rowHaze = { 0.30f, 0.22f, 0.14f, 0.06f };
            for (int row = 0; row < rowZ.Length; row++)
            {
                bool front = row == rowZ.Length - 1;
                float x = -235f + Range(random, 0f, 3f);
                while (x < 235f)
                {
                    float z = rowZ[row] + Range(random, -3f, 3f);
                    float ground = 0.2f + (z - FarShoreZ) * 0.03f;
                    float shade = Range(random, 0.82f, 1.15f);
                    float haze = rowHaze[row] + Range(random, -0.03f, 0.03f);
                    double kind = random.NextDouble();
                    if (kind < (front ? 0.40 : 0.62))
                    {
                        float height = Range(random, 11f, front ? 17f : 21f);
                        shape.Spruce(random, x, ground, z, height, height * Range(random, 0.30f, 0.40f), shade, haze);
                    }
                    else if (kind < (front ? 0.88 : 0.86))
                    {
                        float height = Range(random, 8f, 14f);
                        shape.Leafy(random, x, ground, z, height, height * Range(random, 0.55f, 0.8f), shade * 1.22f, haze);
                    }
                    else
                    {
                        float height = Range(random, 14f, 21f);
                        shape.Pine(random, x, ground, z, height, height * Range(random, 0.34f, 0.46f), shade * 1.06f, haze);
                    }

                    x += Range(random, 2.4f, front ? 5.2f : 4.4f);
                }
            }

            // Кусты и трава у самой воды закрывают стволы и полоску земли.
            shape.Ridge(FarShoreZ + 1.5f, 260f, 0.7f,
                x => 1.1f + 1.9f * Mathf.PerlinNoise(x * 0.35f, 11f) + 1.3f * Mathf.PerlinNoise(x * 1.3f, 23f), 0.05f, 0.14f, 0.85f, -0.4f);

            return shape.ToMesh("FarShore");
        }

        /// <summary>Долина посередине кадра: в секторе ±7° холмы прячутся за лесом — там встаёт и садится солнце; к ±12,5° — в полный рост.</summary>
        private static float Valley(float x, float z)
        {
            float angle = Mathf.Abs(Mathf.Atan2(x, z)) * Mathf.Rad2Deg;
            return Mathf.Lerp(0.36f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(7f, 12.5f, angle)));
        }

        /// <summary>Зубчатая кромка лесистого холма: верхушки деревьев.</summary>
        private static float WoodedTop(float x) =>
            2.2f * Mathf.PerlinNoise(x * 0.33f, 41f) + 1.6f * Mathf.Abs(Mathf.Sin(x * 0.9f + 3f * Mathf.PerlinNoise(x * 0.2f, 7f)));

        private static float Fbm(float x) =>
            Mathf.PerlinNoise(x, 0.5f) * 0.6f + Mathf.PerlinNoise(x * 2.3f, 9.5f) * 0.28f + Mathf.PerlinNoise(x * 5.1f, 21.5f) * 0.12f;

        /// <summary>
        /// Камыш. Две куртины стоят в кадре по самым краям: слева от рыбака густая, справа пореже и дальше —
        /// там удилище. Середина кадра свободна: заброс, поплавок и рыба у берега видны.
        /// Ещё две — по сторонам, для широких экранов.
        /// </summary>
        private static Mesh ReedsMesh(System.Random random, float frameSlope, SpotLook look)
        {
            var shape = new ReedShape();

            // Край кадра на расстоянии z от глаза: полуширина = frameSlope · (z − Eye.z).
            for (int i = 0; i < look.ReedsLeft; i++)
            {
                float z = Clump(random, 1.9f, look.ReedsLeft > 60 ? 9f : 6.2f, 5);
                float edge = frameSlope * (z - Eye.z);
                shape.Reed(random, -edge * Range(random, 0.86f, 1.45f), z, Range(random, 1.0f, 1.85f));
            }

            for (int i = 0; i < look.ReedsRight; i++)
            {
                float z = Clump(random, 3.0f, look.ReedsRight > 60 ? 10f : 7.4f, 4);
                float edge = frameSlope * (z - Eye.z);
                shape.Reed(random, edge * Range(random, 0.90f, 1.5f), z, Range(random, 1.0f, 1.9f));
            }

            for (int i = 0; i < 200; i++)
            {
                float x = i % 2 == 0 ? Range(random, -16f, -3.5f) : Range(random, 4f, 18f);
                shape.Reed(random, x, NearShoreZ(x) + Range(random, -0.3f, 1.8f), Range(random, 1.1f, 2.1f));
            }

            return shape.ToMesh("Reeds", new Vector3(0, 0.5f, -0.86f));
        }

        /// <summary>Стебли растут кучками: случайная кучка из count на отрезке и небольшой разброс вокруг неё.</summary>
        private static float Clump(System.Random random, float from, float to, int count)
        {
            float center = Mathf.Lerp(from, to, (random.Next(count) + 0.5f) / count);
            return center + Range(random, -0.35f, 0.35f);
        }

        private static float Range(System.Random random, float min, float max) =>
            min + (float)random.NextDouble() * (max - min);

        /// <summary>Сборщик камыша: изогнутые стебли, свисающие листья, початки рогоза.</summary>
        private sealed class ReedShape
        {
            private static readonly Color StemLow = new Color(0.10f, 0.14f, 0.06f);
            private static readonly Color StemHigh = new Color(0.21f, 0.30f, 0.13f);
            private static readonly Color Head = new Color(0.17f, 0.10f, 0.06f);

            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<int> _triangles = new List<int>();

            public Mesh ToMesh(string name, Vector3 normal)
            {
                var normals = new List<Vector3>(_vertices.Count);
                for (int i = 0; i < _vertices.Count; i++)
                    normals.Add(normal);
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(_vertices);
                mesh.SetColors(_colors);
                mesh.SetNormals(normals);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }

            public void Reed(System.Random random, float x, float z, float height)
            {
                float lean = Range(random, -0.22f, 0.22f) * height;
                float leanZ = Range(random, -0.08f, 0.08f) * height;
                float width = Range(random, 0.011f, 0.017f);
                var root = new Vector3(x, -0.25f, z);
                var top = new Vector3(lean, height + 0.25f, leanZ);

                Ribbon(root, top, Vector3.zero, width, 5, StemLow, StemHigh);

                double extra = random.NextDouble();
                if (extra < 0.28)
                    Cattail(random, root, top, width);

                int leaves = 1 + random.Next(2);
                for (int i = 0; i < leaves; i++)
                {
                    float t = Range(random, 0.22f, 0.62f);
                    var from = root + new Vector3(top.x * t * t, top.y * t, top.z * t * t);
                    float direction = random.NextDouble() < 0.5 ? -1f : 1f;
                    float length = Range(random, 0.35f, 0.7f);
                    var reach = new Vector3(direction * length * 0.35f, length * 0.9f, Range(random, -0.1f, 0.1f));
                    var droop = new Vector3(direction * length * 0.25f, -length * 0.35f, 0f);
                    Ribbon(from, reach, droop, width * 1.3f, 5, StemLow, StemHigh);
                }
            }

            /// <summary>
            /// Лента от from: точка(t) = from + reach·t (у стебля x, z — квадратично) + droop·t².
            /// Ширина откладывается поперёк ленты (в плоскости, обращённой к рыбаку): иначе пологие листья
            /// выходят тоньше пикселя и рябят пунктиром. К концу лента сужается, но не в ниточку.
            /// </summary>
            private void Ribbon(Vector3 from, Vector3 reach, Vector3 droop, float width, int segments, Color low, Color high)
            {
                bool stem = droop == Vector3.zero;
                int start = _vertices.Count;
                for (int k = 0; k <= segments; k++)
                {
                    float t = k / (float)segments;
                    Vector3 point;
                    Vector3 tangent;
                    if (stem)
                    {
                        point = from + new Vector3(reach.x * t * t, reach.y * t, reach.z * t * t);
                        tangent = new Vector3(2f * reach.x * t, reach.y, 0f);
                    }
                    else
                    {
                        point = from + reach * t + droop * (t * t);
                        tangent = new Vector3(reach.x + 2f * droop.x * t, reach.y + 2f * droop.y * t, 0f);
                    }

                    var across = tangent.sqrMagnitude > 1e-8f ? new Vector3(-tangent.y, tangent.x, 0f).normalized : Vector3.right;
                    float w = width * (1f - 0.65f * t);
                    _vertices.Add(point - across * w);
                    _vertices.Add(point + across * w);
                    var color = Color.Lerp(low, high, t);
                    _colors.Add(color);
                    _colors.Add(color);
                }

                for (int k = 0; k < segments; k++)
                {
                    int a = start + k * 2;
                    _triangles.Add(a);
                    _triangles.Add(a + 2);
                    _triangles.Add(a + 1);
                    _triangles.Add(a + 1);
                    _triangles.Add(a + 2);
                    _triangles.Add(a + 3);
                }
            }

            /// <summary>Початок рогоза: тёмное веретено чуть ниже верхушки стебля.</summary>
            private void Cattail(System.Random random, Vector3 root, Vector3 top, float width)
            {
                const float t = 0.80f;
                var center = root + new Vector3(top.x * t * t, top.y * t, top.z * t * t);
                var up = new Vector3(top.x * 2f * t, top.y, top.z * 2f * t).normalized;
                float half = Range(random, 0.06f, 0.10f);
                float thick = width * 2.2f + 0.008f;
                var side = Vector3.right * thick;

                int a = _vertices.Count;
                _vertices.Add(center - up * (half + thick));
                _vertices.Add(center - up * half * 0.7f - side);
                _vertices.Add(center - up * half * 0.7f + side);
                _vertices.Add(center + up * half * 0.7f - side);
                _vertices.Add(center + up * half * 0.7f + side);
                _vertices.Add(center + up * (half + thick));
                for (int k = 0; k < 6; k++)
                    _colors.Add(Head);
                int[] order = { 0, 1, 2, 1, 3, 2, 2, 3, 4, 3, 5, 4 };
                for (int k = 0; k < order.Length; k++)
                    _triangles.Add(a + order[k]);
            }
        }

        /// <summary>Сборщик меша силуэтов: гряды холмов и три вида деревьев.</summary>
        private sealed class Silhouette
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<int> _triangles = new List<int>();

            public Mesh ToMesh(string name)
            {
                var normals = new List<Vector3>(_vertices.Count);
                for (int i = 0; i < _vertices.Count; i++)
                    normals.Add(Vector3.back);
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(_vertices);
                mesh.SetColors(_colors);
                mesh.SetNormals(normals);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }

            /// <summary>Гряда: полоса от воды до height(x). Дымка внизу гуще — туман над водой.</summary>
            public void Ridge(float z, float halfWidth, float step, Func<float, float> height, float hazeTop, float hazeBottom, float shade = 1f, float bottom = -4f)
            {
                int count = Mathf.CeilToInt(2f * halfWidth / step);
                int start = _vertices.Count;
                for (int i = 0; i <= count; i++)
                {
                    float x = -halfWidth + i * step;
                    _vertices.Add(new Vector3(x, bottom, z));
                    _vertices.Add(new Vector3(x, height(x), z));
                    _colors.Add(new Color(shade, shade, shade, hazeBottom));
                    _colors.Add(new Color(shade, shade, shade, hazeTop));
                }

                for (int i = 0; i < count; i++)
                {
                    int a = start + i * 2;
                    Quad(a, a + 1, a + 3, a + 2);
                }
            }

            /// <summary>Ель: ствол и стопка треугольников-ярусов, каждый чуть уже и немного вразнобой.</summary>
            public void Spruce(System.Random random, float x, float ground, float z, float height, float width, float shade, float haze)
            {
                Trunk(x, ground, z, width * 0.07f, height * 0.25f, shade * 0.8f, haze);
                int tiers = 5 + random.Next(3);
                float crownBottom = ground + height * 0.10f;
                float crown = height * 0.90f;
                for (int i = 0; i < tiers; i++)
                {
                    float t0 = i / (float)tiers;
                    float t1 = Mathf.Min(1f, (i + 1.7f) / tiers);
                    float half = width * 0.5f * Mathf.Pow(1f - t0, 0.85f) * Range(random, 0.86f, 1.1f) + width * 0.04f;
                    float baseY = crownBottom + crown * t0 - crown / tiers * 0.18f;
                    float apexY = i == tiers - 1 ? ground + height : crownBottom + crown * t1;
                    float lean = Range(random, -0.08f, 0.08f) * half;
                    float bottomShade = shade * Mathf.Lerp(0.82f, 1.05f, t0);
                    float topShade = shade * Mathf.Lerp(0.95f, 1.16f, t1);
                    int a = _vertices.Count;
                    Add(x - half + lean, baseY, z, bottomShade, haze);
                    Add(x + half + lean, baseY, z, bottomShade, haze);
                    Add(x + lean * 0.3f, apexY, z, topShade, haze);
                    _triangles.Add(a);
                    _triangles.Add(a + 2);
                    _triangles.Add(a + 1);
                }
            }

            /// <summary>Лиственное: ствол и крона из нескольких неровных овалов.</summary>
            public void Leafy(System.Random random, float x, float ground, float z, float height, float width, float shade, float haze)
            {
                Trunk(x, ground, z, width * 0.05f, height * 0.5f, shade * 0.7f, haze);
                float centerY = ground + height * 0.62f;
                Blob(random, x, centerY, z, width * 0.5f, height * 0.38f, shade, haze);
                int extra = 2 + random.Next(2);
                for (int i = 0; i < extra; i++)
                {
                    float angle = Range(random, 0f, Mathf.PI * 2f);
                    float bx = x + Mathf.Cos(angle) * width * 0.30f;
                    float by = centerY + Mathf.Sin(angle) * height * 0.20f;
                    float size = Range(random, 0.45f, 0.68f);
                    Blob(random, bx, by, z, width * 0.5f * size, height * 0.38f * size, shade * Range(random, 0.92f, 1.1f), haze);
                }
            }

            /// <summary>Сосна: высокий голый ствол и приплюснутая крона наверху.</summary>
            public void Pine(System.Random random, float x, float ground, float z, float height, float width, float shade, float haze)
            {
                Trunk(x, ground, z, width * 0.055f, height * 0.8f, shade * 0.75f, haze);
                Blob(random, x, ground + height * 0.84f, z, width * 0.5f, height * 0.16f, shade, haze);
                Blob(random, x + Range(random, -0.25f, 0.25f) * width, ground + height * 0.70f, z, width * 0.34f, height * 0.10f, shade * 0.95f, haze);
            }

            private void Trunk(float x, float ground, float z, float half, float height, float shade, float haze)
            {
                int a = _vertices.Count;
                Add(x - half, ground - 0.5f, z, shade, haze);
                Add(x + half, ground - 0.5f, z, shade, haze);
                Add(x + half * 0.7f, ground + height, z, shade, haze);
                Add(x - half * 0.7f, ground + height, z, shade, haze);
                Quad(a, a + 3, a + 2, a + 1);
            }

            /// <summary>Неровный овал веером треугольников; верх светлее низа.</summary>
            private void Blob(System.Random random, float x, float y, float z, float radiusX, float radiusY, float shade, float haze)
            {
                const int points = 11;
                int center = _vertices.Count;
                Add(x, y, z, shade, haze);
                float phase = Range(random, 0f, 6.28f);
                for (int i = 0; i < points; i++)
                {
                    float angle = i * Mathf.PI * 2f / points;
                    float wobble = 0.86f + 0.14f * Mathf.Sin(angle * 3f + phase) + Range(random, -0.07f, 0.07f);
                    float up = Mathf.Sin(angle);
                    Add(x + Mathf.Cos(angle) * radiusX * wobble, y + up * radiusY * wobble, z, shade * (1f + 0.16f * up), haze);
                }

                for (int i = 0; i < points; i++)
                {
                    _triangles.Add(center);
                    _triangles.Add(center + 1 + (i + 1) % points);
                    _triangles.Add(center + 1 + i);
                }
            }

            private void Add(float x, float y, float z, float shade, float haze)
            {
                _vertices.Add(new Vector3(x, y, z));
                _colors.Add(new Color(shade, shade, shade, haze));
            }

            private void Quad(int a, int b, int c, int d)
            {
                _triangles.Add(a);
                _triangles.Add(b);
                _triangles.Add(c);
                _triangles.Add(a);
                _triangles.Add(c);
                _triangles.Add(d);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fishing.Unity
{
    /// <summary>
    /// Удилище: линия от рукояти к кончику. Изгиб «прогрессивный»: угол наклона растёт к кончику (∝ s²),
    /// кончик поворачивается к леске и немного вниз. Рукоять держится у камеры (в руках рыбака).
    /// </summary>
    public sealed class RodView
    {
        private const int Points = 14;
        private const float Length = 2.7f;

        /// <summary>Угол кончика при bend = 1, градусы: «дуга» удилища.</summary>
        private const float MaxBendDeg = 100f;

        /// <summary>Насколько кончик может загнуться дальше направления лески, градусы.</summary>
        private const float BendPastLineDeg = 20f;

        private readonly LineRenderer _line;
        private readonly Vector3[] _positions = new Vector3[Points];

        public Vector3 Tip { get; private set; }

        public RodView(Transform parent, Material material)
        {
            _line = Lines.Create(parent, "Rod", material, new Color(0.12f, 0.09f, 0.07f), 0.045f, 0.007f);
            _line.positionCount = Points;
        }

        /// <param name="target">Куда смотрит удилище и куда тянет леска (поплавок или рыба).</param>
        /// <param name="lift">Подъём удилища 0–1.</param>
        /// <param name="bend">Изгиб 0–1: доля от наибольшего (0,05 — без нагрузки, 0,8 — на пределе удилища).</param>
        /// <param name="pitchOffsetDeg">Кивок всем удилищем, градусы: минус — кончик вниз (телеграф), плюс — вверх.</param>
        public void Update(Camera camera, Vector3 target, float lift, float bend, float pitchOffsetDeg)
        {
            var cam = camera.transform;
            var butt = cam.position + cam.right * 0.30f - cam.up * 0.48f + cam.forward * 0.35f;
            var flat = target - butt;
            flat.y = 0;
            if (flat.sqrMagnitude < 0.01f)
            {
                flat = cam.forward;
                flat.y = 0;
            }
            if (flat.sqrMagnitude < 1e-6f)
                flat = Vector3.forward;
            flat.Normalize();

            float elevation = (Mathf.Lerp(18f, 58f, lift) + pitchOffsetDeg) * Mathf.Deg2Rad;
            var straight = flat * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation);

            // Плоскость изгиба: от прямого удилища к леске и чуть вниз — кончик «кланяется» рыбе.
            var toLine = target - (butt + straight * Length);
            if (toLine.sqrMagnitude < 1e-6f)
                toLine = Vector3.down;
            toLine.Normalize();
            var pull = toLine + Vector3.down * 0.25f;
            var normal = pull - straight * Vector3.Dot(pull, straight);
            if (normal.sqrMagnitude < 1e-6f)
                normal = Vector3.down - straight * Vector3.Dot(Vector3.down, straight);
            normal.Normalize();

            // Опущенное удилище смотрит почти вдоль лески: его кончик гнётся меньше — как у настоящего.
            float tipDeg = Mathf.Min(Mathf.Max(0f, bend) * MaxBendDeg, Vector3.Angle(straight, toLine) + BendPastLineDeg);
            float tipRad = tipDeg * Mathf.Deg2Rad;

            float step = Length / (Points - 1);
            var p = butt;
            _positions[0] = p;
            for (int i = 1; i < Points; i++)
            {
                float s = (i - 0.5f) / (Points - 1);
                float phi = tipRad * s * s;
                p += (straight * Mathf.Cos(phi) + normal * Mathf.Sin(phi)) * step;
                _positions[i] = p;
            }
            _line.SetPositions(_positions);
            Tip = p;
        }
    }

    /// <summary>
    /// Леска от кончика до поплавка: натянутая — прямая и яркая, со слабиной — провисает и тускнеет.
    /// На экране не тоньше 1,5 px на любом расстоянии.
    /// </summary>
    public sealed class FishingLineView
    {
        private const int Points = 18;
        private const float TipWidthM = 0.006f;
        private const float EndWidthM = 0.006f;
        private const float MinPixels = 1.5f;
        private const float LoadPixels = 0.7f;

        private static readonly Color Dim = new Color(0.78f, 0.76f, 0.70f, 0.5f);
        private static readonly Color Bright = new Color(1f, 1f, 0.96f, 1f);

        private readonly LineRenderer _line;
        private readonly Vector3[] _positions = new Vector3[Points];

        public FishingLineView(Transform parent, Material material)
        {
            _line = Lines.Create(parent, "Line", material, new Color(1f, 0.97f, 0.9f, 0.75f), TipWidthM, EndWidthM);
            _line.positionCount = Points;
        }

        public void SetVisible(bool visible)
        {
            if (_line.enabled != visible)
                _line.enabled = visible;
        }

        /// <param name="sag">Провис 0–1: 0 — струна, 1 — леска лежит на воде.</param>
        /// <param name="load">Нагрузка 0–1 (пиковое натяжение / самое слабое звено): ярче, прямее и чуть толще.</param>
        public void Update(Camera camera, Vector3 tip, Vector3 end, float sag, float load)
        {
            sag = Mathf.Clamp01(sag);
            load = Mathf.Clamp01(load);
            float length = Vector3.Distance(tip, end);
            var middle = Vector3.Lerp(tip, end, 0.55f);
            // Натянутая леска чуть провисает от своего веса; под нагрузкой — почти струна.
            float droop = length * 0.015f * (1f - sag) * (1f - 0.75f * load);
            middle.y = Mathf.Lerp(middle.y, Mathf.Min(tip.y, end.y) - 0.02f, sag) - droop;
            for (int i = 0; i < Points; i++)
            {
                float t = i / (Points - 1f);
                _positions[i] = Vector3.Lerp(Vector3.Lerp(tip, middle, t), Vector3.Lerp(middle, end, t), t);
            }
            _line.SetPositions(_positions);

            // Ширина по краям: не меньше MinPixels на экране (2d·tan(fov/2)/высота экрана — метров в пикселе).
            float pixels = MinPixels + LoadPixels * load;
            _line.startWidth = Mathf.Max(TipWidthM, pixels * ScreenMetrics.WorldPerPixel(camera, tip));
            _line.endWidth = Mathf.Max(EndWidthM, pixels * ScreenMetrics.WorldPerPixel(camera, end));

            var color = Color.Lerp(Dim, Bright, Mathf.SmoothStep(0f, 1f, load));
            _line.startColor = color;
            _line.endColor = color;
        }
    }

    /// <summary>
    /// Поплавок. В ожидании качается на ряби, при поклёвке топится и ложится. На вываживании держится на
    /// поверхности: наклоняется к удилищу по натяжению, притапливается на рывке и уходе в глубину,
    /// выскакивает, когда рыба встаёт. На экране не меньше 10 px — на дальней дистанции растёт.
    /// </summary>
    public sealed class FloatView
    {
        /// <summary>Видимая часть поплавка (шар и колпачок над водой), м — по ней считается «не меньше 10 px».</summary>
        private const float BodyHeightM = 0.16f;
        private const float MinPixels = 10f;
        private const float BiteSinkM = 0.16f;
        private const float BiteTiltDeg = 35f;
        private const float TowedSinkM = 0.1f;
        private const float MaxTiltDeg = 20f;
        private const float TiltSmoothSeconds = 0.12f;

        // Пружина по высоте: ~2 Гц, недодемпфирована — притопленный поплавок выскакивает с подскоком.
        private const float SpringOmega = 13f;
        private const float SpringDamping = 0.4f;
        private const float PopSpeed = 0.9f;
        private const float MaxPopM = 0.08f;
        private const float SpringStep = 1f / 60f;

        private readonly Transform _root;
        private float _dip;
        private float _sink;
        private float _sinkVelocity;
        private float _tilt;
        private float _scale = 1f;

        public Vector3 Position { get; set; }

        /// <summary>Куда крепится леска (низ поплавка после всех смещений).</summary>
        public Vector3 LineAttach { get; private set; }

        public FloatView(Transform parent, TestLake lake)
        {
            _root = new GameObject("Float").transform;
            _root.SetParent(parent, false);
            Part(PrimitiveType.Sphere, new Vector3(0, 0.02f, 0), new Vector3(0.09f, 0.13f, 0.09f), lake.LitMaterial(new Color(0.95f, 0.93f, 0.88f)));
            Part(PrimitiveType.Capsule, new Vector3(0, 0.13f, 0), new Vector3(0.07f, 0.07f, 0.07f), lake.LitMaterial(new Color(0.95f, 0.25f, 0.15f)));
            Part(PrimitiveType.Cylinder, new Vector3(0, 0.23f, 0), new Vector3(0.012f, 0.06f, 0.012f), lake.LitMaterial(new Color(0.1f, 0.08f, 0.06f)));
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (_root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);
        }

        /// <summary>Заброс, ожидание, поклёвка.</summary>
        /// <param name="bite">Поклёвка: 0 — спокойно, 1 — поплавок утоплен.</param>
        public void UpdateFree(Camera camera, float dt, float time, float wind, float bite)
        {
            _dip = Mathf.MoveTowards(_dip, bite, dt * 6f);
            Spring(dt, 0f);
            _tilt = Mathf.MoveTowards(_tilt, 0f, dt * 60f);
            UpdateScale(camera);

            float sway = 2f + wind;
            var rotation = Quaternion.Euler(Mathf.Sin(time * 1.7f) * sway + _dip * BiteTiltDeg, 0, Mathf.Sin(time * 1.3f) * sway);
            Place(time, wind, rotation);
        }

        /// <summary>Вываживание: поплавок на поверхности над рыбой.</summary>
        /// <param name="rodTip">Кончик удилища — к нему наклоняется поплавок.</param>
        /// <param name="tensionShare">Натяжение / сила рыбы (T/F): наклон до 20° при 1.</param>
        /// <param name="underwater">Рывок или уход в глубину — поплавок притоплен на 0,1 м.</param>
        /// <param name="pop">Рыба встала (начало Rest) — поплавок выскакивает.</param>
        public void UpdateTowed(Camera camera, float dt, float time, float wind, Vector3 rodTip, float tensionShare,
            bool underwater, bool pop)
        {
            _dip = Mathf.MoveTowards(_dip, 0f, dt * 6f);
            if (pop && !underwater)
                _sinkVelocity += PopSpeed;
            Spring(dt, underwater ? -TowedSinkM : 0f);
            float tiltTarget = MaxTiltDeg * Mathf.Clamp01(tensionShare);
            _tilt += (tiltTarget - _tilt) * (1f - Mathf.Exp(-dt / TiltSmoothSeconds));
            UpdateScale(camera);

            // Верх поплавка наклоняется к удилищу: поворот вокруг Cross(up, к удилищу).
            var toRod = rodTip - Position;
            toRod.y = 0f;
            if (toRod.sqrMagnitude < 1e-4f)
                toRod = new Vector3(-Position.x, 0f, -Position.z);
            if (toRod.sqrMagnitude < 1e-4f)
                toRod = Vector3.back;
            var axis = Vector3.Cross(Vector3.up, toRod.normalized);
            float sway = (2f + wind) * 0.5f;
            var wobble = Quaternion.Euler(Mathf.Sin(time * 1.7f) * sway + _dip * BiteTiltDeg, 0, Mathf.Sin(time * 1.3f) * sway);
            Place(time, wind, Quaternion.AngleAxis(_tilt, axis) * wobble);
        }

        private void Place(float time, float wind, Quaternion rotation)
        {
            float bob = Mathf.Sin(time * 2.4f) * (0.012f + wind * 0.004f) + Mathf.Sin(time * 5.1f) * 0.004f;
            float height = (bob + _sink - _dip * BiteSinkM) * _scale;
            var position = Position + new Vector3(0, height, 0);
            _root.SetPositionAndRotation(position, rotation);
            LineAttach = position + new Vector3(0, 0.03f * _scale, 0);
        }

        /// <summary>Пружина высоты (м, в масштабе 1): к target, мелкими шагами — устойчиво при любом dt.</summary>
        private void Spring(float dt, float target)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / SpringStep), 1, 8);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float accel = -SpringOmega * SpringOmega * (_sink - target) - 2f * SpringDamping * SpringOmega * _sinkVelocity;
                _sinkVelocity += accel * h;
                _sink += _sinkVelocity * h;
            }
            if (_sink > MaxPopM)
            {
                _sink = MaxPopM;
                _sinkVelocity = Mathf.Min(_sinkVelocity, 0f);
            }
        }

        private void UpdateScale(Camera camera)
        {
            float wpp = ScreenMetrics.WorldPerPixel(camera, Position);
            _scale = Mathf.Max(1f, MinPixels * wpp / BodyHeightM);
            _root.localScale = new Vector3(_scale, _scale, _scale);
        }

        private void Part(PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;

            // У встроенных примитивов нет цвета вершин, а шейдер умножает на него — даём белый.
            var filter = go.GetComponent<MeshFilter>();
            var mesh = Object.Instantiate(filter.sharedMesh);
            var white = new Color[mesh.vertexCount];
            for (int i = 0; i < white.Length; i++)
                white[i] = Color.white;
            mesh.colors = white;
            filter.sharedMesh = mesh;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    /// <summary>
    /// Круги на воде: всплеск заброса, поклёвка, усы за поплавком, бьющаяся рыба, всплески фаз.
    /// Пул колец создаётся один раз; новое кольцо занимает самое «старое» место.
    /// </summary>
    public sealed class RippleField
    {
        private const int Segments = 40;
        private const float MinPixels = 1.2f;

        private sealed class Ripple
        {
            public LineRenderer Line;
            public Vector3 Center;
            public float Age;
            public float Duration;
            public float Radius;
            public float Alpha;
            public float Width;
        }

        private readonly List<Ripple> _ripples = new List<Ripple>();
        private readonly Vector3[] _positions = new Vector3[Segments];

        public RippleField(Transform parent, Material material, int count = 40)
        {
            for (int i = 0; i < count; i++)
            {
                var line = Lines.Create(parent, "Ripple", material, Color.white, 0.025f, 0.025f);
                line.positionCount = Segments;
                line.loop = true;
                line.enabled = false;
                _ripples.Add(new Ripple { Line = line, Age = 1f, Duration = 1f, Alpha = 0.55f, Width = 0.025f });
            }
        }

        /// <param name="radius">Радиус к концу жизни кольца, м.</param>
        /// <param name="duration">Время жизни, с.</param>
        /// <param name="alpha">Непрозрачность в начале (гаснет к концу).</param>
        /// <param name="width">Толщина кольца, м (на экране не тоньше 1,2 px).</param>
        public void Spawn(Vector3 center, float radius, float duration, float alpha = 0.55f, float width = 0.025f)
        {
            // Свободное кольцо, а если все заняты — то, что ближе всех к концу жизни.
            Ripple slot = _ripples[0];
            float oldest = -1f;
            for (int i = 0; i < _ripples.Count; i++)
            {
                var ripple = _ripples[i];
                if (!ripple.Line.enabled)
                {
                    slot = ripple;
                    break;
                }
                float progress = ripple.Age / ripple.Duration;
                if (progress > oldest)
                {
                    oldest = progress;
                    slot = ripple;
                }
            }

            slot.Center = new Vector3(center.x, 0.01f, center.z);
            slot.Age = 0f;
            slot.Duration = Mathf.Max(0.05f, duration);
            slot.Radius = radius;
            slot.Alpha = alpha;
            slot.Width = width;
            slot.Line.enabled = true;
        }

        public void Update(float deltaTime, Camera camera)
        {
            for (int r = 0; r < _ripples.Count; r++)
            {
                var ripple = _ripples[r];
                if (!ripple.Line.enabled)
                    continue;
                ripple.Age += deltaTime;
                float t = ripple.Age / ripple.Duration;
                if (t >= 1f)
                {
                    ripple.Line.enabled = false;
                    continue;
                }

                float radius = Mathf.Lerp(0.05f, ripple.Radius, 1f - (1f - t) * (1f - t));
                for (int i = 0; i < Segments; i++)
                {
                    float a = i / (float)Segments * Mathf.PI * 2f;
                    _positions[i] = ripple.Center + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius * 0.92f);
                }
                ripple.Line.SetPositions(_positions);
                float width = Mathf.Max(ripple.Width, MinPixels * ScreenMetrics.WorldPerPixel(camera, ripple.Center));
                ripple.Line.startWidth = width;
                ripple.Line.endWidth = width;
                var color = new Color(1f, 0.97f, 0.9f, ripple.Alpha * (1f - t));
                ripple.Line.startColor = color;
                ripple.Line.endColor = color;
            }
        }
    }

    /// <summary>Размеры на экране: сколько метров мира приходится на пиксель на глубине точки.</summary>
    internal static class ScreenMetrics
    {
        /// <summary>
        /// Метров мира в одном пикселе экрана на глубине point (по оси взгляда камеры):
        /// 2·d·tan(fov/2) / высота экрана. Умножить на нужные пиксели — получится наименьший размер в метрах.
        /// </summary>
        public static float WorldPerPixel(Camera camera, Vector3 point)
        {
            var view = camera.transform;
            float depth = Mathf.Max(camera.nearClipPlane, Vector3.Dot(point - view.position, view.forward));
            float height = Mathf.Max(1, Screen.height);
            return 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / height;
        }
    }

    /// <summary>
    /// Прицел заброса, пока палец тянет вверх: дуга полёта поплавка от кончика удилища до воды (по ней к цели
    /// бежит светлый блик) и кольца на воде там, куда он упадёт. Дуга — та же, что у настоящего заброса.
    /// </summary>
    public sealed class CastAimView
    {
        private const int ArcPoints = 26;
        private const int RingPoints = 30;

        private readonly LineRenderer _arc;
        private readonly LineRenderer _ring;
        private readonly LineRenderer _wave;
        private readonly Vector3[] _arcPositions = new Vector3[ArcPoints];
        private readonly Vector3[] _ringPositions = new Vector3[RingPoints];
        private readonly Vector3[] _wavePositions = new Vector3[RingPoints];
        private readonly Gradient _gradient = new Gradient();
        private readonly GradientColorKey[] _colors = { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) };
        private readonly GradientAlphaKey[] _alphas = new GradientAlphaKey[5];
        private bool _shown;

        public CastAimView(Transform parent, Material material)
        {
            _arc = Lines.Create(parent, "CastAimArc", material, Color.white, 0.02f, 0.05f);
            _ring = Lines.Create(parent, "CastAimRing", material, new Color(1f, 0.85f, 0.45f, 0.95f), 0.05f, 0.05f);
            _wave = Lines.Create(parent, "CastAimWave", material, new Color(1f, 1f, 1f, 0.5f), 0.03f, 0.03f);
            _arc.positionCount = ArcPoints;
            _ring.positionCount = RingPoints;
            _wave.positionCount = RingPoints;
            _ring.loop = true;
            _wave.loop = true;
            Hide();
        }

        /// <param name="from">Кончик удилища.</param>
        /// <param name="to">Точка на воде, куда упадёт поплавок.</param>
        /// <param name="eye">Камера — толщина линий растёт с расстоянием, чтобы вдали они не терялись.</param>
        public void Show(Vector3 from, Vector3 to, Vector3 eye, float time)
        {
            if (!_shown)
            {
                _shown = true;
                _arc.enabled = true;
                _ring.enabled = true;
                _wave.enabled = true;
            }

            float distance = Vector3.Distance(from, to);
            float far = Mathf.Max(1f, Vector3.Distance(eye, to));
            var apex = Vector3.Lerp(from, to, 0.5f) + Vector3.up * (2f + distance * 0.22f);
            for (int i = 0; i < ArcPoints; i++)
            {
                float t = i / (ArcPoints - 1f);
                _arcPositions[i] = Vector3.Lerp(Vector3.Lerp(from, apex, t), Vector3.Lerp(apex, to, t), t);
            }

            _arc.SetPositions(_arcPositions);
            _arc.startWidth = 0.012f;
            _arc.endWidth = 0.006f * far;

            // Блик бежит по дуге к цели: бледная линия, на ней яркое пятно.
            float head = Mathf.Repeat(time * 0.9f, 1.2f) - 0.1f;
            _alphas[0] = new GradientAlphaKey(0.10f, 0f);
            _alphas[1] = new GradientAlphaKey(0.22f, Mathf.Clamp01(head - 0.14f));
            _alphas[2] = new GradientAlphaKey(0.95f, Mathf.Clamp01(head));
            _alphas[3] = new GradientAlphaKey(0.22f, Mathf.Clamp01(head + 0.10f));
            _alphas[4] = new GradientAlphaKey(0.45f, 1f);
            _gradient.SetKeys(_colors, _alphas);
            _arc.colorGradient = _gradient;

            // Кольцо-цель и расходящаяся от него волна.
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 6f);
            Circle(_ringPositions, to, 0.42f + 0.05f * pulse);
            _ring.SetPositions(_ringPositions);
            _ring.startWidth = _ring.endWidth = 0.0045f * far;

            float grow = Mathf.Repeat(time * 0.8f, 1f);
            Circle(_wavePositions, to, 0.5f + 1.1f * grow);
            _wave.SetPositions(_wavePositions);
            _wave.startWidth = _wave.endWidth = 0.003f * far;
            var color = new Color(1f, 1f, 1f, 0.55f * (1f - grow));
            _wave.startColor = color;
            _wave.endColor = color;
        }

        public void Hide()
        {
            _shown = false;
            _arc.enabled = false;
            _ring.enabled = false;
            _wave.enabled = false;
        }

        private static void Circle(Vector3[] points, Vector3 center, float radius)
        {
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / points.Length;
                points[i] = new Vector3(center.x + Mathf.Cos(angle) * radius, 0.03f, center.z + Mathf.Sin(angle) * radius);
            }
        }
    }

    internal static class Lines
    {
        public static LineRenderer Create(Transform parent, string name, Material material, Color color, float startWidth, float endWidth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.startColor = color;
            line.endColor = color;
            line.startWidth = startWidth;
            line.endWidth = endWidth;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
    }
}

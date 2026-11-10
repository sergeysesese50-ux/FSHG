using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>
    /// Подсачек в кадре. Когда рыба подошла на длину подсачека (кнопка «Подсачек» на экране), он сам выезжает
    /// снизу кадра и зависает над водой между рыболовом и рыбой; когда рыбу можно брать — подаётся к ней и опускается
    /// к самой воде. По кнопке: удача — подсачек подныривает под рыбу и поднимает её; рано — шлёпает по воде и
    /// возвращается.
    /// Нарисован линиями, как удилище и леска: ручка, обод, сетка из дуг и поясок.
    /// Правила (когда брать можно) — в FightModel; здесь только картинка.
    /// </summary>
    public sealed class LandingNetView
    {
        private const int HoopSegments = 28;
        private const int RibPoints = 9;
        private const int Ribs = 4;
        private const float HoopRadius = 0.27f;
        private const float BagDepth = 0.30f;
        private const float AppearSeconds = 0.35f;
        private const float ScoopHitSeconds = 0.16f;
        private const float ScoopSeconds = 0.75f;
        private const float MissSeconds = 0.5f;

        private readonly LineRenderer _handle;
        private readonly LineRenderer _hoop;
        private readonly LineRenderer _belt;
        private readonly LineRenderer[] _ribs = new LineRenderer[Ribs];
        private readonly Vector3[] _ring = new Vector3[HoopSegments];
        private readonly Vector3[] _rib = new Vector3[RibPoints];

        private float _show;            // 0 — убран, 1 — вынут
        private float _ready;           // 0 — ждёт, 1 — подан к рыбе
        private float _scoop = -1f;     // секунды с нажатия; < 0 — не машет
        private bool _scoopHit;
        private Vector3 _lastFish;

        public LandingNetView(Transform parent, Material material)
        {
            _handle = Lines.Create(parent, "NetHandle", material, new Color(0.30f, 0.22f, 0.15f), 0.030f, 0.022f);
            _handle.positionCount = 2;
            _hoop = Lines.Create(parent, "NetHoop", material, new Color(0.86f, 0.88f, 0.90f), 0.020f, 0.020f);
            _hoop.positionCount = HoopSegments;
            _hoop.loop = true;
            _belt = Lines.Create(parent, "NetBelt", material, new Color(1f, 1f, 1f, 0.45f), 0.008f, 0.008f);
            _belt.positionCount = HoopSegments;
            _belt.loop = true;
            for (int i = 0; i < Ribs; i++)
            {
                _ribs[i] = Lines.Create(parent, "NetRib", material, new Color(1f, 1f, 1f, 0.50f), 0.008f, 0.008f);
                _ribs[i].positionCount = RibPoints;
            }

            SetVisible(false);
        }

        /// <summary>Подсачек сейчас машет (удачно или мимо) — его надо дорисовать, даже если вываживание уже кончилось.</summary>
        public bool Scooping => _scoop >= 0f;

        /// <summary>Нажата кнопка «Подсачек». hit — рыбу можно было брать.</summary>
        public void Scoop(bool hit)
        {
            _scoop = 0f;
            _scoopHit = hit;
        }

        public void Hide()
        {
            _show = 0f;
            _ready = 0f;
            _scoop = -1f;
            SetVisible(false);
        }

        /// <param name="fish">Точка на воде над рыбой.</param>
        /// <param name="shown">Рыба в зоне подсачека: он вынут.</param>
        /// <param name="ready">Рыбу можно брать: подсачек подан к ней.</param>
        public void Update(Camera camera, Vector3 fish, bool shown, bool ready, float dt, float time)
        {
            if (_scoop >= 0f)
            {
                _scoop += dt;
                if (_scoop >= (_scoopHit ? ScoopSeconds : MissSeconds))
                {
                    // Удачный взмах заканчивается тем, что подсачек убирают; промах — возвратом в ожидание.
                    _scoop = -1f;
                    if (_scoopHit)
                        _show = 0f;
                }
            }
            else
            {
                _lastFish = fish;
            }

            bool wanted = shown || _scoop >= 0f;
            _show = Mathf.MoveTowards(_show, wanted ? 1f : 0f, dt / AppearSeconds);
            _ready = Mathf.MoveTowards(_ready, ready && wanted ? 1f : 0f, dt * 3.5f);
            if (_show <= 0.001f || camera == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            var cam = camera.transform;
            var forward = cam.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);

            // Рука с подсачеком — под нижним краем кадра, чуть правее середины (над кнопкой «Подсачек»).
            var hand = cam.position + forward * 0.9f + right * 0.18f - Vector3.up * 0.95f;
            var feet = new Vector3(cam.position.x, 0f, cam.position.z) + forward * 1.5f;
            var target = new Vector3(_lastFish.x, 0f, _lastFish.z);

            // Где обод: ждёт на полпути к рыбе над водой; готов — у самой рыбы, у воды.
            var hover = Vector3.Lerp(feet, target, Mathf.Lerp(0.50f, 0.86f, _ready));
            hover.y = Mathf.Lerp(0.34f, 0.07f, _ready) + 0.012f * Mathf.Sin(time * 2.4f);
            float tilt = Mathf.Lerp(6f, 2f, _ready);

            if (_scoop >= 0f && _scoopHit)
            {
                // Удача: нырок под рыбу и подъём к рыболову.
                if (_scoop < ScoopHitSeconds)
                {
                    float k = _scoop / ScoopHitSeconds;
                    hover = Vector3.Lerp(hover, new Vector3(target.x, -0.16f, target.z), Smooth(k));
                    tilt = Mathf.Lerp(tilt, 0f, k);
                }
                else
                {
                    float k = Mathf.Clamp01((_scoop - ScoopHitSeconds) / (ScoopSeconds - ScoopHitSeconds));
                    var under = new Vector3(target.x, -0.16f, target.z);
                    var lifted = Vector3.Lerp(target, feet, 0.55f) + Vector3.up * 0.62f;
                    hover = Vector3.Lerp(under, lifted, Smooth(k));
                    tilt = Mathf.Lerp(0f, -14f, k);     // поднимают горлом к себе, чтобы рыба не выпала
                }
            }
            else if (_scoop >= 0f)
            {
                // Рано: шлепок по воде у рыбы и обратно.
                float k = Mathf.Sin(Mathf.Clamp01(_scoop / MissSeconds) * Mathf.PI);
                hover = Vector3.Lerp(hover, new Vector3(target.x, 0.0f, target.z), 0.8f * k);
                tilt = Mathf.Lerp(tilt, 2f, k);
            }

            // Выезд снизу кадра.
            float appear = Smooth(_show);
            var center = Vector3.Lerp(hand + forward * 0.25f, hover, appear);

            // Обод: почти горизонтальный — сверху, с места рыболова, он виден открытым овалом.
            // tilt > 0 — дальний край ниже ближнего, tilt < 0 — выше.
            var toHoop = center - hand;
            toHoop.y = 0f;
            var along = toHoop.sqrMagnitude > 1e-4f ? toHoop.normalized : forward;
            var side = Vector3.Cross(Vector3.up, along);
            var slope = Quaternion.AngleAxis(tilt, side) * along;
            var normal = Vector3.Cross(slope, side).normalized;       // вверх от плоскости обода
            for (int i = 0; i < HoopSegments; i++)
            {
                float a = i / (float)HoopSegments * Mathf.PI * 2f;
                _ring[i] = center + side * (Mathf.Cos(a) * HoopRadius) + slope * (Mathf.Sin(a) * HoopRadius);
            }

            _hoop.SetPositions(_ring);
            _handle.SetPosition(0, hand);
            _handle.SetPosition(1, center - slope * HoopRadius);

            // Сетка: дуги от края обода через дно мешка к противоположному краю; мешок висит вниз.
            var bottom = center - normal * (BagDepth * 0.35f) - Vector3.up * (BagDepth * 0.65f);
            for (int r = 0; r < Ribs; r++)
            {
                float a = r / (float)Ribs * Mathf.PI;
                var from = center + side * (Mathf.Cos(a) * HoopRadius) + slope * (Mathf.Sin(a) * HoopRadius);
                var to = center - side * (Mathf.Cos(a) * HoopRadius) - slope * (Mathf.Sin(a) * HoopRadius);
                for (int p = 0; p < RibPoints; p++)
                {
                    float t = p / (float)(RibPoints - 1);
                    _rib[p] = (1 - t) * (1 - t) * from + 2 * (1 - t) * t * (2f * bottom - 0.5f * (from + to)) + t * t * to;
                }

                _ribs[r].SetPositions(_rib);
            }

            // Поясок сетки на полпути ко дну.
            var beltCenter = Vector3.Lerp(center, bottom, 0.55f);
            for (int i = 0; i < HoopSegments; i++)
            {
                float a = i / (float)HoopSegments * Mathf.PI * 2f;
                _ring[i] = beltCenter + side * (Mathf.Cos(a) * HoopRadius * 0.68f) + slope * (Mathf.Sin(a) * HoopRadius * 0.68f);
            }

            _belt.SetPositions(_ring);

            // Готов брать — обод зеленеет, как кнопка.
            var hoopColor = Color.Lerp(new Color(0.86f, 0.88f, 0.90f), new Color(0.55f, 0.92f, 0.50f), _ready);
            _hoop.startColor = hoopColor;
            _hoop.endColor = hoopColor;
        }

        private static float Smooth(float x) => x * x * (3f - 2f * x);

        private void SetVisible(bool visible)
        {
            if (_handle.enabled == visible)
                return;
            _handle.enabled = visible;
            _hoop.enabled = visible;
            _belt.enabled = visible;
            foreach (var rib in _ribs)
                rib.enabled = visible;
        }
    }
}

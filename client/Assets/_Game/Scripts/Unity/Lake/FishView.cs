using Fishing.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fishing.Unity
{
    /// <summary>
    /// Рыба на экране (docs/specs/fight-v2.md, §4). Читает только FightFrame.
    /// Видна ближе 8 м (у границы — глубже, «всплывает» по мере подхода), на свечке — на любой дистанции:
    /// рыба выходит к поверхности — показываются спина и голова, середина тела не выше 4% длины над водой, размер настоящий. Позы:
    /// - бросок (Surge): разворот от берега и рывок вглубь;
    /// - голова над водой (HeadUp): нос вверх на 20°, скользит к рыбаку;
    /// - силы меньше 0,15: на боку;
    /// - силы больше 0,45 и не отдыхает: бьётся, круги каждые 0,18/(0,4 + S) с;
    /// - иначе стоит ровно и вяло шевелит хвостом.
    /// Всплески (кольца по весу) — на любой дистанции: начало ухода, рывок, вершина свечки, бросок.
    /// После поимки показывается крупным планом перед камерой.
    /// </summary>
    public sealed class FishView
    {
        public const float VisibleDistanceM = 8f;

        private const float FadeStartM = 6.5f;
        /// <summary>
        /// Свечка — это выход к поверхности, а не полёт: середина тела поднимается от JumpSinkShare длины под водой
        /// до JumpPeakShare над ней. Из воды показываются спина и голова; брюхо и хвост остаются в воде всегда.
        /// </summary>
        private const float JumpSinkShare = 0.06f;
        private const float JumpPeakShare = 0.04f;
        private const float JumpPitchDeg = 22f;
        private const float JumpRiseGuessSeconds = 0.42f;
        private const float JumpMaxSeconds = 1.6f;

        private static readonly int BendAmpId = Shader.PropertyToID("_BendAmp");
        private static readonly int BendSpeedId = Shader.PropertyToID("_BendSpeed");

        private readonly Transform _root;
        private readonly MeshFilter _filter;
        private readonly Material _material;

        // Поза (сглаженная).
        private float _yaw;
        private float _roll;
        private float _pitch;
        private float _bend;
        private float _bendSpeed = 6f;
        private float _depth;
        private float _wobbleAmp;
        private float _wobbleRate = 1.5f;
        private float _wobblePhase;
        private float _nextRipple;
        private bool _posed;

        // Свечка: подъём до вершины (событие JumpApex), спуск за то же время.
        private bool _jumping;
        private float _jumpStart;
        private float _jumpTime;
        private float _apexAt;
        private float _jumpHalf;
        private float _apexHeight;
        private float _jumpHeight;
        private float _jumpArc;
        private float _jumpShow;

        private float _splash = 1f;

        public FishView(Transform parent, TestLake lake)
        {
            var go = new GameObject("Fish");
            go.transform.SetParent(parent, false);
            _root = go.transform;
            _filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            _material = lake.LitMaterial(Color.white);
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(false);
        }

        public float LengthM { get; private set; }

        public void Setup(string speciesId, int weightG)
        {
            _filter.sharedMesh = FishMeshBuilder.Build(speciesId);
            LengthM = FishMeshBuilder.LengthMeters(speciesId, weightG);
            _root.localScale = Vector3.one * LengthM;
            // Размер всплесков: растёт как кубический корень веса (≈0,9 м плотва, 1,5 м лещ 2 кг, 2 м щука 7 кг).
            _splash = 0.5f + 0.8f * Mathf.Pow(Mathf.Max(1, weightG) / 1000f, 1f / 3f);
            _posed = false;
            _roll = 0f;
            _pitch = 0f;
            _bend = 0.1f;
            _bendSpeed = 6f;
            _depth = -0.12f;
            _wobbleAmp = 0f;
            _nextRipple = 0f;
            _jumping = false;
            _jumpHeight = 0f;
            _jumpShow = 0f;
        }

        public void Hide() => SetActive(false);

        /// <summary>
        /// Разовые всплески по событиям кадра — на любой дистанции. Звать в каждом кадре вываживания и в кадре
        /// его конца (Frame.EndedThisFrame): там же последний всплеск — рыбу вытащили или она ушла.
        /// </summary>
        public void Splashes(FightFrame f, RippleField ripples)
        {
            var e = f.Events;
            if (e == FightEvents.None && !f.EndedThisFrame)
                return;
            var at = f.FishWorldPosition;
            float s = _splash;

            if ((e & FightEvents.PhaseStart) != 0)
            {
                if (f.Phase == FishPhase.Run)
                {
                    // Бурун: рыба развернулась и пошла.
                    ripples.Spawn(at, 0.9f * s, 1.2f, 0.75f, 0.035f);
                    ripples.Spawn(at, 0.5f * s, 0.8f, 0.6f, 0.03f);
                }
                else if (f.Phase == FishPhase.Jerk)
                {
                    ripples.Spawn(at, 0.7f * s, 0.9f, 0.7f, 0.03f);
                }
            }
            if ((e & FightEvents.SurgeStart) != 0)
            {
                ripples.Spawn(at, 1.5f, 1.3f, 0.8f, 0.035f);
                ripples.Spawn(at, 0.8f * s, 0.9f, 0.65f, 0.03f);
            }
            if ((e & FightEvents.JumpApex) != 0)
            {
                ripples.Spawn(at, 1.0f * s, 1.3f, 0.85f, 0.04f);
                ripples.Spawn(at, 0.6f * s, 0.9f, 0.7f, 0.035f);
            }
            if (f.EndedThisFrame)
                ripples.Spawn(at, 0.6f * s, 1.0f, 0.7f, 0.03f);
        }

        /// <summary>Рыба на крючке. velocity — сглаженная скорость точки рыбы по воде, м/с.</summary>
        public void UpdateFight(FightFrame f, Vector3 velocity, Camera camera, RippleField ripples, float dt, float time)
        {
            UpdateJump(f, ripples);
            _jumpShow = _jumping ? 1f : Mathf.MoveTowards(_jumpShow, 0f, dt * 4f);

            float distance = f.FishDistanceCm / 100f;
            bool visible = distance < VisibleDistanceM || _jumping;
            SetActive(visible);
            if (!visible)
            {
                _posed = false;
                return;
            }

            float stamina = f.Stamina;
            bool surge = f.Phase == FishPhase.Surge;
            bool exhausted = stamina < 0.15f;
            bool active = f.Phase != FishPhase.Rest;
            bool thrash = !surge && !f.HeadUp && !exhausted && stamina > 0.45f && active;
            var away = f.Direction;

            // Цели позы.
            bool faceAway;
            float roll, pitch, bend, speed, wobble, wobbleRate, depth, rippleEvery, rippleRadius, rippleTime, turnRate;
            if (surge)
            {
                faceAway = true; roll = 0f; pitch = -8f; bend = 0.22f; speed = 22f; wobble = 10f; wobbleRate = 12f;
                depth = -0.2f; rippleEvery = 0.35f; rippleRadius = 0.5f + LengthM; rippleTime = 0.7f; turnRate = 540f;
            }
            else if (f.HeadUp)
            {
                faceAway = false; roll = 0f; pitch = 20f; bend = 0.03f; speed = 4f; wobble = 3f; wobbleRate = 1.2f;
                depth = 0.02f; rippleEvery = 0.5f; rippleRadius = 0.25f + LengthM * 0.5f; rippleTime = 0.8f; turnRate = 160f;
            }
            else if (exhausted)
            {
                faceAway = false; roll = 78f; pitch = 0f; bend = 0.025f; speed = 3f; wobble = 4f; wobbleRate = 1.5f;
                depth = -0.01f; rippleEvery = 1.6f; rippleRadius = 0.3f + LengthM * 0.5f; rippleTime = 1.4f; turnRate = 120f;
            }
            else if (thrash)
            {
                faceAway = true; roll = 0f; pitch = 0f; bend = f.Phase == FishPhase.Jerk ? 0.22f : 0.15f; speed = 17f;
                wobble = 28f; wobbleRate = 9f; depth = -0.1f; rippleEvery = 0.18f / (0.4f + stamina);
                rippleRadius = 0.6f + LengthM; rippleTime = 0.7f; turnRate = 300f;
            }
            else
            {
                // Устала (0,15–0,45) или свежая отдыхает: стоит ровно, вяло работает хвостом.
                faceAway = active; roll = 0f; pitch = 0f; bend = 0.06f; speed = 6f; wobble = 6f; wobbleRate = 1.5f;
                depth = -0.12f; rippleEvery = stamina < 0.45f ? 1.4f : 0.8f; rippleRadius = 0.35f + LengthM * 0.5f;
                rippleTime = 1.4f; turnRate = 200f;
            }

            // У границы видимости рыба глубже — «проявляется» из воды по мере подхода.
            depth -= 0.35f * Mathf.Clamp01((distance - FadeStartM) / (VisibleDistanceM - FadeStartM)) * (1f - _jumpShow);

            if (_jumping)
            {
                // Свечка: нос вверх на взлёте, горизонтально на вершине, вниз на входе в воду; трясёт головой.
                pitch = JumpPitchDeg * Mathf.Cos(Mathf.PI * _jumpArc);
                roll = 20f * Mathf.Sin(time * 14f);
                bend = 0.25f; speed = 20f; wobble = 12f; wobbleRate = 10f; turnRate = 0f;
            }

            // Куда смотрит: от берега или к рыбаку, с поворотом в сторону бокового хода.
            var facing = faceAway ? away : -away;
            float sideSpeed = Vector3.Dot(velocity, f.Right);
            facing += f.Right * (Mathf.Clamp(sideSpeed / 0.6f, -1f, 1f) * 0.8f);
            float targetYaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;

            if (!_posed)
            {
                _posed = true;
                _yaw = targetYaw;
                _roll = roll;
                _pitch = pitch;
                _depth = depth;
                _wobbleAmp = wobble;
                _wobbleRate = wobbleRate;
            }
            _yaw = Mathf.MoveTowardsAngle(_yaw, targetYaw, turnRate * dt);
            _roll = _jumping ? roll : Mathf.MoveTowards(_roll, roll, dt * 60f);
            _pitch = _jumping ? pitch : Mathf.MoveTowards(_pitch, pitch, dt * 90f);
            _bend = Mathf.MoveTowards(_bend, bend, dt * 0.6f);
            _bendSpeed = Mathf.MoveTowards(_bendSpeed, speed, dt * 20f);
            _depth = Mathf.MoveTowards(_depth, depth, dt * 0.6f);
            _wobbleAmp = Mathf.MoveTowards(_wobbleAmp, wobble, dt * 60f);
            _wobbleRate = Mathf.MoveTowards(_wobbleRate, wobbleRate, dt * 20f);
            _wobblePhase = Mathf.Repeat(_wobblePhase + dt * _wobbleRate, Mathf.PI * 2f);
            _material.SetFloat(BendAmpId, _bend);
            _material.SetFloat(BendSpeedId, _bendSpeed);

            // Размер всегда настоящий. Раньше в прыжке рыбу растягивали до 40 px на экране — на дальнем забросе
            // получалась огромная рыба, вылетающая из озера.
            var waterPoint = f.FishWorldPosition;
            float length = LengthM;
            _root.localScale = new Vector3(length, length, length);

            // Рыба чуть дальше поплавка (за ним, от берега), чтобы поплавок не протыкал её.
            float wobbleDeg = Mathf.Sin(_wobblePhase) * _wobbleAmp;
            var rotation = Quaternion.Euler(0, _yaw, 0) * Quaternion.Euler(0, -90f + wobbleDeg, 0)
                * Quaternion.Euler(0, 0, _pitch) * Quaternion.Euler(_roll, 0, 0);
            // В свечке рыба стартует чуть под водой и поднимается на _jumpHeight — но не выше JumpPeakShare длины.
            float height = _depth * length * (1f - _jumpShow) - JumpSinkShare * length * _jumpShow + _jumpHeight;
            var center = waterPoint + away * (0.15f + 0.45f * length);
            _root.SetPositionAndRotation(new Vector3(center.x, height, center.z), rotation);

            if (_jumping)
                return;
            _nextRipple -= dt;
            if (_nextRipple <= 0f)
            {
                ripples.Spawn(center, rippleRadius, rippleTime);
                _nextRipple = rippleEvery;
            }
        }

        /// <summary>
        /// Дуга свечки. Начало — PhaseStart с Phase = Jump; вершина — событие JumpApex (средний шаг фазы);
        /// спуск длится столько же, сколько подъём. Кончается кольцом входа в воду.
        /// Время — модельное (как у плавных полей: на шаг позади, с долей Alpha), а не время кадра:
        /// после зависания дуга не убегает вперёд модели.
        /// </summary>
        private void UpdateJump(FightFrame f, RippleField ripples)
        {
            var e = f.Events;
            float now = ModelTime(f);
            if ((e & FightEvents.PhaseStart) != 0 && f.Phase == FishPhase.Jump)
            {
                _jumping = true;
                _jumpStart = now;
                _apexAt = -1f;
                _jumpHalf = JumpRiseGuessSeconds;
                _jumpHeight = 0f;
                _jumpArc = 0f;
            }
            if (!_jumping)
            {
                _jumpHeight = 0f;
                return;
            }

            _jumpTime = Mathf.Max(0f, now - _jumpStart);
            if (_apexAt < 0f && ((e & FightEvents.JumpApex) != 0 || _jumpTime > JumpMaxSeconds * 0.5f))
            {
                _apexAt = _jumpTime;
                _jumpHalf = Mathf.Clamp(_jumpTime, 0.2f, 0.7f);
                _apexHeight = _jumpHeight;
            }

            // Рыба не вылетает из воды: над поверхностью показываются голова и спина, брюхо и хвост остаются в воде.
            float leap = (JumpSinkShare + JumpPeakShare) * LengthM;
            if (_apexAt < 0f)
            {
                float u = Mathf.Clamp01(_jumpTime / _jumpHalf);
                _jumpHeight = leap * Mathf.Sin(u * Mathf.PI * 0.5f);
                _jumpArc = 0.5f * u;
                return;
            }

            float down = (_jumpTime - _apexAt) / _jumpHalf;
            if (down >= 1f)
            {
                _jumping = false;
                _jumpHeight = 0f;
                _jumpArc = 1f;
                ripples.Spawn(f.FishWorldPosition, 0.7f * _splash, 1.0f, 0.75f, 0.035f);
                return;
            }
            _jumpHeight = _apexHeight * Mathf.Cos(down * Mathf.PI * 0.5f);
            _jumpArc = 0.5f + 0.5f * down;
        }

        /// <summary>Время вываживания на момент плавных полей снимка: последний шаг минус (1 − Alpha) шага, с.</summary>
        private static float ModelTime(FightFrame f)
        {
            if (f.Tick <= 0)
                return 0f;
            float tick = f.ElapsedSeconds / f.Tick;
            return f.ElapsedSeconds - (1f - f.Alpha) * tick;
        }

        /// <summary>Пойманная рыба крупным планом перед камерой.</summary>
        public void UpdateShowcase(Camera camera, float time)
        {
            SetActive(true);
            _jumping = false;
            _posed = false;
            _root.localScale = new Vector3(LengthM, LengthM, LengthM);
            var cam = camera.transform;
            float distance = 0.55f + LengthM * 1.5f;
            var position = cam.position + cam.forward * distance - cam.up * (0.05f * distance);
            var rotation = Quaternion.LookRotation(cam.right, cam.up) * Quaternion.Euler(0, -90 + Mathf.Sin(time * 0.8f) * 14f, Mathf.Sin(time * 1.3f) * 5f);
            _root.SetPositionAndRotation(position, rotation);
            _material.SetFloat(BendAmpId, 0.03f + Mathf.Max(0f, Mathf.Sin(time * 2.2f)) * 0.06f);
            _material.SetFloat(BendSpeedId, 9f);
        }

        private void SetActive(bool active)
        {
            if (_root.gameObject.activeSelf != active)
                _root.gameObject.SetActive(active);
        }
    }
}

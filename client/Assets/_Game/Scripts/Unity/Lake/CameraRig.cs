using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>
    /// Камера вываживания (docs/specs/fight-v2.md, §4). Стоит на месте, меняет только угол обзора и поворот,
    /// и только по состоянию вываживания (FishingController.Frame):
    /// - FOV: 38° пока рыба дальше 12 м, к 5 м и ближе — 50° (между ними линейно), вне вываживания — исходный (55°);
    ///   успокаивается примерно за 0,8 с;
    /// - поворот вбок: atan(Lateral / Distance), не больше ±8°, успокаивается примерно за 0,6 с;
    /// - ближе 5 м камера наклоняется на 6° вниз (плавно с 6,5 м);
    /// - FOV не сужается так, чтобы рыба ушла за край кадра (если в исходном кадре она была видна).
    /// followFight = false — камера плавно возвращается в исходное положение и стоит.
    ///
    /// Порядок: Update до FishingController (−10), чтобы удилище, привязанное к камере, и ширина лески
    /// считались уже по новому положению камеры. Снимок читается прошлого кадра — на сглаживании
    /// 0,6–0,8 с это незаметно.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class CameraRig : MonoBehaviour
    {
        private const float FarFov = 38f;
        private const float NearFov = 50f;
        private const float FarDistanceM = 12f;
        private const float NearDistanceM = 5f;
        private const float PitchStartM = 6.5f;
        private const float NearPitchDeg = 6f;
        private const float MaxYawDeg = 8f;
        private const float EdgeMarginDeg = 3f;

        // SmoothDamp почти доходит до цели (~90%) примерно за 2 × smoothTime.
        private const float FovSmoothSeconds = 0.8f / 2f;
        private const float YawSmoothSeconds = 0.6f / 2f;
        private const float PitchSmoothSeconds = 0.8f / 2f;

        /// <summary>Камера следует за вываживанием. Выключено — плавно возвращается в исходное положение.</summary>
        [Tooltip("Камера следует за вываживанием: приближение, поворот за рыбой, наклон у берега")]
        public bool followFight = true;

        private FishingController _game;
        private Camera _camera;
        private float _baseFov;
        private float _basePitch;
        private float _baseYaw;
        private float _baseRoll;
        private float _fov;
        private float _fovVelocity;
        private float _yaw;
        private float _yawVelocity;
        private float _pitch;
        private float _pitchVelocity;

        /// <summary>Вызывает TestLake после настройки камеры: текущие FOV и поворот камеры — исходные.</summary>
        public void Init(FishingController game, Camera camera)
        {
            _game = game;
            _camera = camera;
            if (camera == null)
                return;
            _baseFov = camera.fieldOfView;
            var euler = camera.transform.rotation.eulerAngles;
            _basePitch = euler.x;
            _baseYaw = euler.y;
            _baseRoll = euler.z;
            _fov = _baseFov;
            _yaw = 0f;
            _pitch = 0f;
        }

        private void Update()
        {
            if (_game == null || _camera == null)
                return;

            float targetFov = _baseFov;
            float targetYaw = 0f;
            float targetPitch = 0f;
            if (followFight && _game.State == LakeState.Fight)
            {
                var frame = _game.Frame;
                float distance = frame.FishDistanceCm / 100f;
                targetFov = Mathf.Lerp(NearFov, FarFov, Mathf.InverseLerp(NearDistanceM, FarDistanceM, distance));
                float side = Mathf.Atan2(frame.LateralCm, Mathf.Max(1f, frame.FishDistanceCm)) * Mathf.Rad2Deg;
                targetYaw = Mathf.Clamp(side, -MaxYawDeg, MaxYawDeg);
                targetPitch = NearPitchDeg * Mathf.Clamp01((PitchStartM - distance) / (PitchStartM - NearDistanceM));
                targetFov = KeepInFrame(frame.FishWorldPosition, targetFov, targetYaw, targetPitch);
            }

            float dt = Time.deltaTime;
            _fov = Mathf.SmoothDamp(_fov, targetFov, ref _fovVelocity, FovSmoothSeconds, Mathf.Infinity, dt);
            _yaw = Mathf.SmoothDamp(_yaw, targetYaw, ref _yawVelocity, YawSmoothSeconds, Mathf.Infinity, dt);
            _pitch = Mathf.SmoothDamp(_pitch, targetPitch, ref _pitchVelocity, PitchSmoothSeconds, Mathf.Infinity, dt);

            _camera.fieldOfView = _fov;
            _camera.transform.rotation = Quaternion.Euler(_basePitch + _pitch, _baseYaw + _yaw, _baseRoll);
        }

        /// <summary>
        /// Наименьший FOV, при котором точка рыбы остаётся в кадре с запасом 3° (по горизонтали — с учётом
        /// узкого портретного кадра). Шире исходного FOV не раскрывается.
        /// </summary>
        private float KeepInFrame(Vector3 fish, float fov, float yaw, float pitch)
        {
            var to = fish - _camera.transform.position;
            float flat = Mathf.Sqrt(to.x * to.x + to.z * to.z);
            if (flat < 0.01f)
                return fov;
            float fishYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float fishPitch = Mathf.Atan2(-to.y, flat) * Mathf.Rad2Deg;
            float h = Mathf.Min(80f, Mathf.Abs(Mathf.DeltaAngle(_baseYaw + yaw, fishYaw)) + EdgeMarginDeg);
            float v = Mathf.Min(80f, Mathf.Abs(Mathf.DeltaAngle(_basePitch + pitch, fishPitch)) + EdgeMarginDeg);
            float aspect = Mathf.Max(0.1f, _camera.aspect);
            float needH = 2f * Mathf.Atan(Mathf.Tan(h * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
            float needV = 2f * v;
            return Mathf.Max(fov, Mathf.Min(Mathf.Max(needH, needV), _baseFov));
        }
    }
}

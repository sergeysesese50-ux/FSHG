using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fishing.Unity
{
    /// <summary>
    /// Постобработка озера, собранная кодом (сцену править не нужно): мягкое свечение ярких мест —
    /// солнца, луны, искр на воде; чуть больше контраста и насыщенности; лёгкое затемнение по краям кадра.
    /// Кривая яркости (tonemapping) выключена намеренно.
    /// Bloom — в том же режиме (качественная фильтрация), что и в Assets/Settings/SampleSceneProfile.asset:
    /// по профилям проекта URP решает, какие варианты шейдеров оставить в сборке.
    /// </summary>
    public sealed class LakePost : MonoBehaviour
    {
        private VolumeProfile _profile;

        public void Init(Camera camera)
        {
            if (camera != null)
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "LakePost";

            var bloom = _profile.Add<Bloom>(true);
            bloom.threshold.value = 1f;
            bloom.intensity.value = 0.75f;
            bloom.scatter.value = 0.6f;
            bloom.tint.value = new Color(1f, 0.94f, 0.84f);
            bloom.highQualityFiltering.value = true;
            if (Application.isMobilePlatform)
            {
                bloom.downscale.value = BloomDownscaleMode.Quarter;
                bloom.maxIterations.value = 4;
            }

            // Профиль по умолчанию у ассетов конвейера (SampleSceneProfile) включает кривую Neutral — она серит
            // закат. Здесь она выключена явно: цвета озера подобраны по макету как есть на экране.
            var tonemapping = _profile.Add<Tonemapping>(true);
            tonemapping.mode.value = TonemappingMode.None;

            var color = _profile.Add<ColorAdjustments>(true);
            color.contrast.value = 6f;
            color.saturation.value = 6f;

            var vignette = _profile.Add<Vignette>(true);
            vignette.intensity.value = 0.26f;
            vignette.smoothness.value = 0.5f;

            var volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = _profile;
        }

        private void OnDestroy()
        {
            if (_profile != null)
                Destroy(_profile);
        }
    }
}

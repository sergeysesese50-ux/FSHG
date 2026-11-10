using UnityEngine;
using UnityEngine.Rendering;

namespace Fishing.Unity
{
    /// <summary>
    /// Игровое время и погода озера. Солнце встаёт впереди-справа, обходит рыбака за спиной и садится
    /// впереди-слева — на рассвете и закате оно в кадре, и по воде к рыбаку идёт дорожка. Ночью впереди
    /// низко идёт луна. Вид озера (небо, свет, дымка, цвет берега и воды) собирается из пяти «обликов»
    /// по высоте солнца: ночь, сумерки, золотой час, утро, день. Облик уходит в шейдеры глобальными
    /// свойствами (Shaders/LakeCommon.hlsl). Ветер — направление и сила, их читают вода, камыш и снос
    /// поплавка. Игровые сутки = 2 реальных часа.
    /// </summary>
    public sealed class LakeClock : MonoBehaviour
    {
        private const float GameSecondsPerRealSecond = 12f;
        private const string SkyShaderName = "Fishing/LakeSky";
        private const float FogDensity = 0.0022f;
        private static readonly float[] WindSteps = { 0f, 2f, 5f, 8f };

        /// <summary>Облик озера при одной высоте солнца. Цвета — как в макете, в sRGB.</summary>
        private struct Look
        {
            public float Elevation;
            public Color Horizon;
            public Color Mid;
            public Color Zenith;
            public Color Glow;
            public float GlowPower;
            public Color Light;
            public float LightPower;
            public Color Ambient;
            public Color Shore;
            public Color Water;
            public float Haze;
        }

        private static readonly Look[] Looks =
        {
            new Look
            {
                Elevation = -14f, Horizon = Hex(0x101C30), Mid = Hex(0x0A1324), Zenith = Hex(0x050A16),
                Glow = Hex(0x000000), GlowPower = 0f, Light = Hex(0xFF9060), LightPower = 0f,
                Ambient = new Color(0.07f, 0.09f, 0.15f), Shore = Hex(0x070C12), Water = Hex(0x050D14), Haze = 0.9f,
            },
            new Look
            {
                Elevation = -5f, Horizon = Hex(0xD07E5E), Mid = Hex(0x5E5C86), Zenith = Hex(0x1D2A4F),
                Glow = Hex(0xFF8A4A), GlowPower = 0.55f, Light = Hex(0xFF9060), LightPower = 0f,
                Ambient = new Color(0.26f, 0.25f, 0.33f), Shore = Hex(0x1A151C), Water = Hex(0x0E2430), Haze = 1f,
            },
            new Look
            {
                Elevation = 3f, Horizon = Hex(0xF2A15F), Mid = Hex(0xF9DDB6), Zenith = Hex(0xA9B9D6),
                Glow = Hex(0xFFC27A), GlowPower = 0.6f, Light = Hex(0xFFA468), LightPower = 1.05f,
                Ambient = new Color(0.50f, 0.40f, 0.36f), Shore = Hex(0x3E2B2B), Water = Hex(0x163C44), Haze = 1f,
            },
            new Look
            {
                Elevation = 14f, Horizon = Hex(0xF3DDBB), Mid = Hex(0xBCD4E8), Zenith = Hex(0x5F90D2),
                Glow = Hex(0xFFE6BC), GlowPower = 0.38f, Light = Hex(0xFFE0B8), LightPower = 1.2f,
                Ambient = new Color(0.47f, 0.46f, 0.47f), Shore = Hex(0x2E3A2C), Water = Hex(0x134650), Haze = 0.9f,
            },
            new Look
            {
                Elevation = 32f, Horizon = Hex(0xCFE2F1), Mid = Hex(0x8DBBE9), Zenith = Hex(0x3E79CF),
                Glow = Hex(0xFFFFFF), GlowPower = 0.18f, Light = Hex(0xFFF7EA), LightPower = 1.25f,
                Ambient = new Color(0.44f, 0.48f, 0.52f), Shore = Hex(0x24402B), Water = Hex(0x12505C), Haze = 0.75f,
            },
        };

        private static readonly int HorizonId = Shader.PropertyToID("_LakeHorizon");
        private static readonly int SkyMidId = Shader.PropertyToID("_LakeSkyMid");
        private static readonly int ZenithId = Shader.PropertyToID("_LakeZenith");
        private static readonly int GlowId = Shader.PropertyToID("_LakeGlow");
        private static readonly int SunDirId = Shader.PropertyToID("_LakeSunDir");
        private static readonly int MoonDirId = Shader.PropertyToID("_LakeMoonDir");
        private static readonly int SunDiscId = Shader.PropertyToID("_LakeSunDisc");
        private static readonly int MoonDiscId = Shader.PropertyToID("_LakeMoonDisc");
        private static readonly int AmbientId = Shader.PropertyToID("_LakeAmbient");
        private static readonly int WindId = Shader.PropertyToID("_LakeWind");
        private static readonly int ShoreTintId = Shader.PropertyToID("_LakeShoreTint");
        private static readonly int WaterBodyId = Shader.PropertyToID("_LakeWaterBody");
        private static readonly int GlintDirId = Shader.PropertyToID("_LakeGlintDir");
        private static readonly int GlintColorId = Shader.PropertyToID("_LakeGlintColor");
        private static readonly int NightId = Shader.PropertyToID("_LakeNight");

        private Light _light;
        private Material _sky;
        private int _windStep = 1;

        public float Hour { get; private set; } = 6.4f;

        /// <summary>Ночь 0…1 — та же, что гасит небо и свет (солнце ниже −2°…−10°): по ней звук озера мягчеет и поют сверчки.</summary>
        public float Night { get; private set; }

        /// <summary>Высота солнца над горизонтом, градусы (ночью отрицательная).</summary>
        public float SunElevation { get; private set; }
        /// <summary>
        /// Куда смотрит рыбак на этом месте озера, градусы: 0 — солнце встаёт и садится прямо в кадре,
        /// другое значение уводит восход, закат и луну в сторону или за спину.
        /// </summary>
        public float ViewAzimuthDeg { get; set; }

        public bool Fast { get; private set; }
        public float WindSpeed => WindSteps[_windStep];
        public Vector2 WindDirection { get; private set; } = new Vector2(0.85f, 0.35f).normalized;
        public Vector3 Wind3 => new Vector3(WindDirection.x, 0, WindDirection.y) * WindSpeed;

        public string ClockText
        {
            get
            {
                int minutes = Mathf.FloorToInt(Hour * 60f) % (24 * 60);
                return $"{minutes / 60:00}:{minutes % 60:00}";
            }
        }

        public string WeatherText => WindSpeed < 0.5f ? "ясно · штиль" : $"ясно · ветер {WindSpeed:0} м/с";

        private void Start()
        {
            _light = RenderSettings.sun != null ? RenderSettings.sun : FindAnyObjectByType<Light>();
            if (_light != null)
            {
                RenderSettings.sun = _light;
                _light.shadows = LightShadows.None;
            }

            // Небо сцены — материал Art/LakeSky.mat с шейдером Fishing/LakeSky. Если в сцене остался
            // старый материал (процедурное небо Unity), берём шейдер по имени.
            var sceneSky = RenderSettings.skybox;
            if (sceneSky != null && sceneSky.shader != null && sceneSky.shader.name == SkyShaderName)
            {
                _sky = new Material(sceneSky);
            }
            else
            {
                var shader = Shader.Find(SkyShaderName);
                if (shader != null)
                    _sky = new Material(shader);
            }

            if (_sky != null)
                RenderSettings.skybox = _sky;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            Apply();
        }

        private void Update()
        {
            float speed = GameSecondsPerRealSecond * (Fast ? 10f : 1f);
            Hour = Mathf.Repeat(Hour + Time.deltaTime * speed / 3600f, 24f);
            Apply();
        }

        public void ToggleFast() => Fast = !Fast;
        public void SkipHours(float hours) => Hour = Mathf.Repeat(Hour + hours, 24f);
        public void CycleWind() => _windStep = (_windStep + 1) % WindSteps.Length;

        private void Apply()
        {
            // Солнце: восход в 6:00 на азимуте +6° (впереди-справа), дальше — дугой за спину,
            // закат в 18:00 на азимуте −6° (впереди-слева). У горизонта азимут почти стоит,
            // поэтому золотой час солнце проводит в узком портретном кадре.
            float dayT = (Hour - 6f) / 12f;
            float elevation = 55f * Mathf.Sin(Mathf.PI * dayT);
            float azimuth = 180f - 174f * Mathf.Cos(Mathf.PI * dayT) - ViewAzimuthDeg;
            var sunDirection = Direction(elevation, azimuth);
            SunElevation = elevation;

            // Луна: восходит в 18:00 впереди-справа, за ночь низко проходит перед рыбаком, заходит в 6:00 слева.
            float nightT = Mathf.Repeat(Hour - 18f, 24f) / 12f;
            float moonElevation = -4f + 15f * Mathf.Sin(Mathf.PI * nightT);
            float moonAzimuth = Mathf.Lerp(10f, -10f, Mathf.Clamp01(nightT)) - ViewAzimuthDeg;
            var moonDirection = Direction(moonElevation, moonAzimuth);

            float night = 1f - Smooth(-10f, -2f, elevation);
            Night = night;
            float sunPower = Smooth(-2f, 5f, elevation);
            float moonPower = night * Smooth(-1f, 5f, moonElevation);

            var look = Evaluate(elevation);
            var moonColor = new Color(0.80f, 0.87f, 1f);

            if (_light != null)
            {
                if (sunPower > 0.001f)
                {
                    _light.color = look.Light;
                    _light.intensity = look.LightPower * sunPower;
                    _light.transform.rotation = Quaternion.LookRotation(-sunDirection);
                }
                else
                {
                    _light.color = new Color(0.62f, 0.72f, 1f);
                    _light.intensity = 0.3f * moonPower;
                    _light.transform.rotation = Quaternion.LookRotation(-moonDirection);
                }
            }

            RenderSettings.fogColor = Color.Lerp(look.Horizon, look.Mid, 0.3f);
            RenderSettings.ambientSkyColor = Color.Lerp(look.Mid, look.Zenith, 0.5f);
            RenderSettings.ambientEquatorColor = look.Horizon;
            RenderSettings.ambientGroundColor = look.Ambient * 0.5f;

            // Диск солнца у горизонта краснеет и тускнеет; яркость выше 1 подхватывает bloom.
            var sunDisc = Color.Lerp(Hex(0xFF8A3C), Hex(0xFFF1CF), Smooth(0f, 12f, elevation));
            float sunDiscPower = Mathf.Lerp(3.2f, 7f, Smooth(0f, 20f, elevation));
            float sunVisible = Smooth(-4f, -1f, elevation);

            Set(HorizonId, look.Horizon);
            Set(SkyMidId, look.Mid);
            Set(ZenithId, look.Zenith);
            Set(GlowId, look.Glow, look.GlowPower);
            Set(SunDiscId, sunDisc, sunDiscPower, sunVisible);
            Set(MoonDiscId, moonColor, 2.4f * moonPower, moonPower);
            Set(AmbientId, look.Ambient);
            Set(ShoreTintId, look.Shore, 1f, look.Haze);
            Set(WaterBodyId, look.Water);
            Shader.SetGlobalVector(SunDirId, sunDirection);
            Shader.SetGlobalVector(MoonDirId, moonDirection);
            Shader.SetGlobalFloat(NightId, night);

            // Дорожка на воде: от солнца, пока оно над горизонтом, ночью — от луны.
            float sunGlint = Smooth(-1f, 3f, elevation);
            if (sunGlint > 0.001f)
            {
                Shader.SetGlobalVector(GlintDirId, sunDirection);
                Set(GlintColorId, sunDisc, sunDiscPower * 0.85f * sunGlint);
            }
            else
            {
                Shader.SetGlobalVector(GlintDirId, moonDirection);
                Set(GlintColorId, moonColor, 1.5f * moonPower);
            }

            Shader.SetGlobalVector(WindId, new Vector4(WindDirection.x, WindDirection.y, WindSpeed / 8f, 0));
        }

        /// <summary>Облик для высоты солнца: плавный переход между двумя соседними.</summary>
        private static Look Evaluate(float elevation)
        {
            if (elevation <= Looks[0].Elevation)
                return Looks[0];
            for (int i = 1; i < Looks.Length; i++)
            {
                if (elevation > Looks[i].Elevation)
                    continue;
                var a = Looks[i - 1];
                var b = Looks[i];
                float t = Smooth(a.Elevation, b.Elevation, elevation);
                return new Look
                {
                    Elevation = elevation,
                    Horizon = Color.Lerp(a.Horizon, b.Horizon, t),
                    Mid = Color.Lerp(a.Mid, b.Mid, t),
                    Zenith = Color.Lerp(a.Zenith, b.Zenith, t),
                    Glow = Color.Lerp(a.Glow, b.Glow, t),
                    GlowPower = Mathf.Lerp(a.GlowPower, b.GlowPower, t),
                    Light = Color.Lerp(a.Light, b.Light, t),
                    LightPower = Mathf.Lerp(a.LightPower, b.LightPower, t),
                    Ambient = Color.Lerp(a.Ambient, b.Ambient, t),
                    Shore = Color.Lerp(a.Shore, b.Shore, t),
                    Water = Color.Lerp(a.Water, b.Water, t),
                    Haze = Mathf.Lerp(a.Haze, b.Haze, t),
                };
            }

            return Looks[Looks.Length - 1];
        }

        /// <summary>
        /// Цвет макета (sRGB) в глобальное свойство шейдера: переводится в линейное пространство проекта
        /// и только потом умножается на яркость — так значения выше 1 остаются предсказуемыми.
        /// </summary>
        private static void Set(int id, Color color, float power = 1f, float alpha = 1f)
        {
            var c = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            Shader.SetGlobalVector(id, new Vector4(c.r * power, c.g * power, c.b * power, alpha));
        }

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        private static Vector3 Direction(float elevation, float azimuth)
        {
            float e = elevation * Mathf.Deg2Rad;
            float a = azimuth * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(e) * Mathf.Sin(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Cos(a));
        }

        private static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
    }
}

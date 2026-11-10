using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>
    /// Точка входа тестового озера: настраивает камеру, строит окружение кодом
    /// и запускает часы, интерфейс, рыбалку, звук, вибрацию и постобработку. Ссылки на шейдеры проставляет меню
    /// Fishing → Open Test Lake, чтобы шейдеры попали в сборку.
    /// </summary>
    public sealed class TestLake : MonoBehaviour
    {
        public Shader waterShader;
        public Shader litShader;
        public Shader unlitShader;

        /// <summary>Соотношение сторон кадра для рендера вне Play Mode (LakePreview); 0 — брать у камеры.</summary>
        [System.NonSerialized] public float aspectOverride;

        private Shader _lit;
        private float _frameSlope;
        private LakeClock _clock;
        private readonly System.Collections.Generic.List<GameObject> _scenery = new System.Collections.Generic.List<GameObject>();

        public Material WaterMaterial { get; private set; }
        public Material LineMaterial { get; private set; }

        public Material LitMaterial(Color tint)
        {
            var material = new Material(_lit);
            material.SetColor("_Color", tint);
            return material;
        }

        /// <summary>Материал силуэтов дальнего берега: тот же шейдер в варианте без света, с дымкой.</summary>
        public Material SilhouetteMaterial()
        {
            var material = new Material(_lit);
            material.EnableKeyword("_SILHOUETTE");
            return material;
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            _lit = litShader != null ? litShader : Shader.Find("Fishing/VertexColorLit");
            WaterMaterial = new Material(waterShader != null ? waterShader : Shader.Find("Fishing/Water"));
            LineMaterial = new Material(unlitShader != null ? unlitShader : Shader.Find("Fishing/UnlitTransparent"));

            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(LakeBuilder.Eye, Quaternion.Euler(9f, 0f, 0f));
                camera.fieldOfView = 55f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 1500f;
                camera.clearFlags = CameraClearFlags.Skybox;
            }

            // Камыш ставится у краёв кадра, поэтому берём его настоящую ширину (узкие и складные экраны — уже).
            float aspect = aspectOverride > 0f ? aspectOverride : camera != null ? camera.aspect : 0.46f;
            _frameSlope = Mathf.Tan(55f * 0.5f * Mathf.Deg2Rad) * Mathf.Clamp(aspect, 0.3f, 1f);

            var clock = gameObject.AddComponent<LakeClock>();
            _clock = clock;
            var hud = gameObject.AddComponent<LakeHud>();
            var game = gameObject.AddComponent<FishingController>();
            var feedback = gameObject.AddComponent<FightFeedback>();
            game.Init(this, clock, hud);
            feedback.Init(game, clock);
            hud.Init(game, clock, feedback);
            gameObject.AddComponent<CameraRig>().Init(game, camera);
            gameObject.AddComponent<LakePost>().Init(camera);

            // Озеро строит FishingController.Init — под место из профиля. Если данные не загрузились, строим первое место.
            if (_scenery.Count == 0)
                ApplySpot(null);
        }

        /// <summary>
        /// Перестраивает берег под место ловли (LakeBuilder.LookFor): другой лес и холмы, другой камыш,
        /// другое направление взгляда (где встаёт солнце). Вода и рыбак остаются на месте.
        /// </summary>
        public void ApplySpot(string spotId)
        {
            foreach (var go in _scenery)
            {
                if (go == null)
                    continue;
                var filter = go.GetComponent<MeshFilter>();
                var renderer = go.GetComponent<MeshRenderer>();
                if (filter != null && filter.sharedMesh != null)
                    DestroyNow(filter.sharedMesh);
                if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial != WaterMaterial)
                    DestroyNow(renderer.sharedMaterial);
                DestroyNow(go);
            }

            _scenery.Clear();
            var look = LakeBuilder.LookFor(spotId);
            LakeBuilder.Build(transform, this, _frameSlope, look, _scenery);
            if (_clock != null)
                _clock.ViewAzimuthDeg = look.ViewAzimuthDeg;
        }

        /// <summary>В игре — Destroy, в редакторе вне Play Mode (LakePreview) — DestroyImmediate.</summary>
        private static void DestroyNow(Object target)
        {
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}

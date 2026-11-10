using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Fishing.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fishing.Editor
{
    /// <summary>
    /// Рендер кадров тестового озера без Play Mode — чтобы смотреть картинку вне открытого редактора
    /// (в копии проекта, в пакетном режиме). Собирает озеро так же, как TestLake при запуске,
    /// выставляет игровой час и сохраняет PNG.
    ///
    /// Unity -batchmode -projectPath &lt;копия&gt; -executeMethod Fishing.Editor.LakePreview.RenderFromCommandLine
    ///       -previewOut &lt;папка&gt; [-previewHours 6.4,9,13,18.6,22.5] [-previewSize 1080x2340]
    ///       [-previewFish — лист моделей рыб fish_models.png вместо кадров озера]
    ///       [-previewNet — кадры подсачека net_*.png]
    ///       [-previewSpots maple,reeds,old_pier,pit,stream] -quit
    /// </summary>
    public static class LakePreview
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void RenderFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string output = Arg(args, "-previewOut") ?? Path.GetFullPath("PreviewRenders");
            string hoursText = Arg(args, "-previewHours") ?? "6.4,9,13,18.6,22.5";
            string sizeText = Arg(args, "-previewSize") ?? "1080x2340";
            var hours = hoursText.Split(',').Select(h => float.Parse(h, CultureInfo.InvariantCulture)).ToArray();
            var size = sizeText.Split('x').Select(int.Parse).ToArray();
            string spotsText = Arg(args, "-previewSpots");
            // -previewFish — вместо озера лист с моделями рыб (сбоку и в три четверти).
            if (Array.IndexOf(args, "-previewFish") >= 0)
            {
                RenderFish(output);
                return;
            }

            // -previewNet — кадры подсачека: ждёт, подан к рыбе, взмах (удача и мимо).
            if (Array.IndexOf(args, "-previewNet") >= 0)
            {
                RenderNet(output, size[0], size[1]);
                return;
            }

            Render(output, hours, size[0], size[1], spotsText != null ? spotsText.Split(',') : null);
        }

        /// <summary>
        /// Кадры подсачека (LandingNetView) с места рыболова, полдень: рыба в 2,6 м и в 1,4 м от берега;
        /// подсачек ждёт, подан к рыбе, середина удачного взмаха, середина промаха.
        /// </summary>
        public static void RenderNet(string outputFolder, int width, int height)
        {
            Directory.CreateDirectory(outputFolder);
            SharedDataSync.Sync(false);
            EditorSceneManager.OpenScene(TestLakeMenu.ScenePath);
            var lake = UnityEngine.Object.FindAnyObjectByType<TestLake>();
            if (lake == null)
                throw new InvalidOperationException("В сцене нет TestLake");
            lake.aspectOverride = width / (float)height;
            Invoke(lake, "Awake");
            var clock = lake.GetComponent<LakeClock>();
            Invoke(clock, "Start");
            typeof(LakeClock).GetProperty("Hour", Any).SetValue(clock, 13f);
            Invoke(clock, "Apply");
            Shader.SetGlobalFloat("_PreviewTime", 13f);

            var camera = Camera.main;
            // Рыба рядом: камера наклонена к воде, как делает CameraRig.
            camera.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);

            // Имя кадра, готовность, взмах (0 — нет, 1 — удача, 2 — мимо), сколько секунд взмаха прошло.
            var shots = new (string name, bool ready, int scoop, float at)[]
            {
                ("wait", false, 0, 0f), ("ready", true, 0, 0f), ("hit_dip", true, 1, 0.14f), ("hit_lift", true, 1, 0.50f), ("miss", false, 2, 0.25f),
            };
            foreach (float distance in new[] { 2.6f, 1.4f })
            foreach (var shot in shots)
            {
                var net = new LandingNetView(lake.transform, lake.LineMaterial);
                var fish = new Vector3(0.25f, 0f, distance);
                for (int i = 0; i < 30; i++)
                    net.Update(camera, fish, true, shot.ready, 0.05f, 0f);
                if (shot.scoop != 0)
                {
                    net.Scoop(shot.scoop == 1);
                    net.Update(camera, fish, true, shot.ready, shot.at, 0f);
                }

                RenderCamera(camera, target);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                RenderTexture.active = null;
                string name = $"net_{distance.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', '_')}m_{shot.name}.png";
                File.WriteAllBytes(Path.Combine(outputFolder, name), image.EncodeToPNG());
                Debug.Log("[LakePreview] " + Path.Combine(outputFolder, name));
                net.Hide();
            }

            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
        }

        public static void Render(string outputFolder, float[] hours, int width, int height, string[] spots = null)
        {
            Directory.CreateDirectory(outputFolder);
            SharedDataSync.Sync(false);
            EditorSceneManager.OpenScene(TestLakeMenu.ScenePath);

            var lake = UnityEngine.Object.FindAnyObjectByType<TestLake>();
            if (lake == null)
                throw new InvalidOperationException("В сцене нет TestLake");
            lake.aspectOverride = width / (float)height;
            Invoke(lake, "Awake");

            var clock = lake.GetComponent<LakeClock>();
            Invoke(clock, "Start");

            var camera = Camera.main;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);

            // Без списка мест — одно, текущее (из профиля); со списком — каждое место по очереди.
            foreach (string spot in spots ?? new string[] { null })
            foreach (float hour in hours)
            {
                if (spot != null)
                    lake.ApplySpot(spot);
                typeof(LakeClock).GetProperty("Hour", Any).SetValue(clock, hour);
                Invoke(clock, "Apply");
                Shader.SetGlobalFloat("_PreviewTime", hour);

                RenderCamera(camera, target);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                RenderTexture.active = null;

                string name = $"lake_{(spot != null ? spot + "_" : "")}{hour.ToString("00.0", CultureInfo.InvariantCulture).Replace('.', '_')}h.png";
                File.WriteAllBytes(Path.Combine(outputFolder, name), image.EncodeToPNG());
                Debug.Log("[LakePreview] " + Path.Combine(outputFolder, name));
            }

            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
        }

        /// <summary>
        /// Лист моделей рыб: каждая рыба сбоку (слева) и в три четверти (справа), свет — полдень на озере.
        /// Рыбы стоят высоко над сценой перед своей камерой, чтобы в кадр не попало озеро.
        /// </summary>
        public static void RenderFish(string outputFolder)
        {
            Directory.CreateDirectory(outputFolder);
            SharedDataSync.Sync(false);
            EditorSceneManager.OpenScene(TestLakeMenu.ScenePath);
            var lake = UnityEngine.Object.FindAnyObjectByType<TestLake>();
            if (lake == null)
                throw new InvalidOperationException("В сцене нет TestLake");
            Invoke(lake, "Awake");
            var clock = lake.GetComponent<LakeClock>();
            Invoke(clock, "Start");
            typeof(LakeClock).GetProperty("Hour", Any).SetValue(clock, 13f);
            Invoke(clock, "Apply");

            string[] ids = { "roach", "crucian", "perch", "bream", "zander", "pike" };
            const int width = 1800, height = 2400;
            var origin = new Vector3(0f, 400f, 0f);
            var material = lake.LitMaterial(Color.white);
            for (int i = 0; i < ids.Length; i++)
            {
                for (int view = 0; view < 2; view++)
                {
                    var fish = new GameObject("Fish_" + ids[i] + "_" + view);
                    fish.AddComponent<MeshFilter>().sharedMesh = FishMeshBuilder.Build(ids[i]);
                    fish.AddComponent<MeshRenderer>().sharedMaterial = material;
                    fish.transform.position = origin + new Vector3(view == 0 ? -0.72f : 0.86f, 1.78f - 0.71f * i, 0f);
                    fish.transform.rotation = view == 0 ? Quaternion.identity : Quaternion.Euler(-18f, 38f, 6f);
                }
            }

            var cameraObject = new GameObject("FishPreviewCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2.25f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.80f, 0.88f, 0.93f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 40f;
            camera.transform.position = origin + new Vector3(0f, 0f, -12f);
            camera.transform.rotation = Quaternion.identity;

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderCamera(camera, target);
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            string path = Path.Combine(outputFolder, "fish_models.png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            Debug.Log("[LakePreview] " + path);
            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
        }

        private static void RenderCamera(Camera camera, RenderTexture target)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                return;
            }

            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
        }

        private static void Invoke(object target, string method)
        {
            var info = target.GetType().GetMethod(method, Any);
            if (info == null)
                throw new MissingMethodException(target.GetType().Name, method);
            info.Invoke(target, null);
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}

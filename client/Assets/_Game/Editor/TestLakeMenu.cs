using System.IO;
using System.Linq;
using Fishing.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fishing.Editor
{
    /// <summary>Меню Fishing → Open Test Lake: создаёт и открывает сцену тестового озера.</summary>
    public static class TestLakeMenu
    {
        public const string ScenePath = "Assets/_Game/Scenes/TestLake.unity";
        private const string SkyPath = "Assets/_Game/Art/LakeSky.mat";
        private const string ShaderFolder = "Assets/_Game/Shaders/";

        [MenuItem("Fishing/Open Test Lake")]
        private static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (!File.Exists(ScenePath))
                CreateScene();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Fishing/Recreate Test Lake Scene")]
        private static void Recreate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            CreateScene();
            EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>
        /// Создаёт сцену: камера, солнце, небо и объект TestLake, который строит
        /// всё остальное при запуске. Сцена ставится первой в списке сборки.
        /// </summary>
        public static void CreateScene()
        {
            SharedDataSync.Sync(false);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Directory.CreateDirectory(Path.GetDirectoryName(SkyPath));

            // Небо — свой шейдер Fishing/LakeSky; цвета и светила ему задаёт LakeClock.
            var skyShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderFolder + "LakeSky.shader");
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (sky == null)
            {
                sky = new Material(skyShader);
                AssetDatabase.CreateAsset(sky, SkyPath);
            }
            else if (sky.shader != skyShader)
            {
                sky.shader = skyShader;
                EditorUtility.SetDirty(sky);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.skybox = sky;
            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null)
            {
                sun.name = "Sun";
                RenderSettings.sun = sun;
            }

            var lake = new GameObject("TestLake").AddComponent<TestLake>();
            lake.waterShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderFolder + "Water.shader");
            lake.litShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderFolder + "VertexColorLit.shader");
            lake.unlitShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderFolder + "UnlitTransparent.shader");

            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[TestLake] Сцена создана: " + ScenePath);
        }
    }
}

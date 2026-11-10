using System.IO;
using System.Linq;
using Fishing.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fishing.Editor
{
    /// <summary>Меню Fishing → Open Fight Sandbox: открывает сцену песочницы вываживания.</summary>
    public static class FightSandboxMenu
    {
        public const string ScenePath = "Assets/_Game/Scenes/FightSandbox.unity";

        [MenuItem("Fishing/Open Fight Sandbox")]
        private static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (!File.Exists(ScenePath))
                CreateScene();
            EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>
        /// Создаёт сцену песочницы и ставит её первой в списке сборки, чтобы на телефоне
        /// сразу открывалась она. Можно вызвать из командной строки:
        /// Unity -batchmode -projectPath client -executeMethod Fishing.Editor.FightSandboxMenu.CreateScene -quit
        /// </summary>
        public static void CreateScene()
        {
            SharedDataSync.Sync(false);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("FightSandbox").AddComponent<FightSandbox>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[FightSandbox] Сцена создана: " + ScenePath);
        }
    }
}

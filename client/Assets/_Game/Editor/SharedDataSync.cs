using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fishing.Editor
{
    /// <summary>
    /// Копирует shared/data/*.json (единый источник правды для клиента и сервера)
    /// в Assets/_Game/Generated/Resources/SharedData, откуда их читает игра.
    /// Папка Generated в git не попадает. Синхронизация идёт при открытии редактора,
    /// перед входом в Play Mode, перед сборкой и по меню Fishing → Sync Shared Data.
    /// </summary>
    [InitializeOnLoad]
    public static class SharedDataSync
    {
        private const string TargetAssetPath = "Assets/_Game/Generated/Resources/SharedData";

        static SharedDataSync()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                    Sync(false);
            };

            if (!Application.isBatchMode)
                EditorApplication.delayCall += () => Sync(false);
        }

        [MenuItem("Fishing/Sync Shared Data")]
        private static void SyncFromMenu()
        {
            Sync(true);
        }

        public static void Sync(bool verbose)
        {
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "shared", "data"));
            if (!Directory.Exists(source))
            {
                Debug.LogWarning($"[SharedDataSync] Не найдена папка {source}");
                return;
            }

            string target = Path.GetFullPath(Path.Combine(Application.dataPath, "..", TargetAssetPath));
            Directory.CreateDirectory(target);

            var sourceFiles = Directory.GetFiles(source, "*.json");
            int changed = 0;

            foreach (string file in sourceFiles)
            {
                string destination = Path.Combine(target, Path.GetFileName(file));
                byte[] bytes = File.ReadAllBytes(file);
                if (File.Exists(destination) && File.ReadAllBytes(destination).SequenceEqual(bytes))
                    continue;
                File.WriteAllBytes(destination, bytes);
                changed++;
            }

            var sourceNames = new HashSet<string>(sourceFiles.Select(Path.GetFileName));
            foreach (string stale in Directory.GetFiles(target, "*.json"))
            {
                if (sourceNames.Contains(Path.GetFileName(stale)))
                    continue;
                File.Delete(stale);
                File.Delete(stale + ".meta");
                changed++;
            }

            if (changed > 0)
                AssetDatabase.Refresh();
            if (verbose || changed > 0)
                Debug.Log($"[SharedDataSync] Общие данные синхронизированы, изменено файлов: {changed}");
        }
    }

    /// <summary>Перед каждой сборкой игры копирует свежие общие данные.</summary>
    internal sealed class SharedDataBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            SharedDataSync.Sync(false);
        }
    }
}

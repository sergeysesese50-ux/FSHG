using System;
using Fishing.Core;
using UnityEngine;

namespace Fishing.Unity
{
    /// <summary>
    /// Загрузка общих данных в игре. Файлы из shared/data копирует в
    /// Assets/_Game/Generated/Resources/SharedData редакторный скрипт SharedDataSync;
    /// руками их не правим.
    /// </summary>
    public static class SharedData
    {
        public const string ResourcesFolder = "SharedData";

        public static FishDatabase LoadFish()
        {
            return new FishDatabase(JsonUtility.FromJson<FishFile>(Read("fish")));
        }

        public static TackleDatabase LoadTackle()
        {
            return new TackleDatabase(JsonUtility.FromJson<TackleFile>(Read("tackle")));
        }

        public static LocationDatabase LoadLocations(FishDatabase fish)
        {
            return new LocationDatabase(JsonUtility.FromJson<LocationFile>(Read("locations")), fish);
        }

        public static TransportDatabase LoadTransport()
        {
            return new TransportDatabase(JsonUtility.FromJson<TransportFile>(Read("transport")));
        }

        public static LevelRewardDatabase LoadLevelRewards(TackleDatabase tackle)
        {
            return new LevelRewardDatabase(JsonUtility.FromJson<LevelRewardFile>(Read("levels")), tackle);
        }

        private static string Read(string name)
        {
            var asset = Resources.Load<TextAsset>(ResourcesFolder + "/" + name);
            if (asset == null)
                throw new InvalidOperationException(
                    $"Нет Resources/SharedData/{name}.json — выполни в редакторе меню Fishing → Sync Shared Data");
            return asset.text;
        }
    }
}

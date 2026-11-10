using System.IO;
using Fishing.Core;
using UnityEngine;

namespace Fishing.Tests
{
    /// <summary>Заготовки данных для тестов, независимые от настройки баланса в fish.json.</summary>
    internal static class TestFish
    {
        public static FishSpecies Species(string id = "testfish", float biteRatePerMin = 1f, float trophyChance = 0.01f,
            int strength = 3, int stamina = 3, string fightStyle = "steady")
        {
            return new FishSpecies
            {
                id = id,
                weightG = new WeightRange { min = 100, typical = 300, qualifying = 600, trophy = 1000, max = 3000 },
                trophyChance = trophyChance,
                depthCm = new IntRange { min = 100, max = 300 },
                activeHours = new[] { 6, 7, 8 },
                baits = new[] { new BaitTaste { id = "worm", taste = 100 } },
                biteRatePerMin = biteRatePerMin,
                maxLineMm = 0.15f,
                strength = strength,
                stamina = stamina,
                fightStyle = fightStyle,
                pricePerKg = 100,
            };
        }

        public static FishFile File(params FishSpecies[] species)
        {
            return new FishFile { version = FishDatabase.SupportedVersion, species = species };
        }

        public static FishDatabase Database(params FishSpecies[] species)
        {
            return new FishDatabase(File(species));
        }

        /// <summary>Идеальные условия для TestFish.Species.</summary>
        public static BiteConditions Ideal()
        {
            return new BiteConditions { Hour = 7.5f, DepthCm = 200f, BaitId = "worm", LineDiameterMm = 0.12f };
        }

        /// <summary>Настоящий shared/data/fish.json из репозитория.</summary>
        public static FishDatabase RealDatabase()
        {
            return new FishDatabase(JsonUtility.FromJson<FishFile>(ReadShared("fish.json")));
        }

        /// <summary>Настоящий shared/data/tackle.json из репозитория.</summary>
        public static TackleDatabase RealTackle()
        {
            return new TackleDatabase(JsonUtility.FromJson<TackleFile>(ReadShared("tackle.json")));
        }

        /// <summary>Настоящий shared/data/locations.json из репозитория.</summary>
        public static LocationDatabase RealLocations()
        {
            return new LocationDatabase(JsonUtility.FromJson<LocationFile>(ReadShared("locations.json")), RealDatabase());
        }

        /// <summary>Настоящий shared/data/transport.json из репозитория.</summary>
        public static TransportDatabase RealTransport()
        {
            return new TransportDatabase(JsonUtility.FromJson<TransportFile>(ReadShared("transport.json")));
        }

        /// <summary>Настоящий shared/data/levels.json из репозитория.</summary>
        public static LevelRewardDatabase RealLevelRewards()
        {
            return new LevelRewardDatabase(JsonUtility.FromJson<LevelRewardFile>(ReadShared("levels.json")), RealTackle());
        }

        private static string ReadShared(string fileName)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "shared", "data", fileName));
            return System.IO.File.ReadAllText(path);
        }
    }
}

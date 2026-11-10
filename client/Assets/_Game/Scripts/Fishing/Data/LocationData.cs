using System;
using System.Collections.Generic;

namespace Fishing.Core
{
    // Классы для чтения shared/data/locations.json. Имена полей совпадают с ключами JSON.
    // Описание полей — shared/data/README.md, TypeScript-двойник — server/modules/src/data.ts.

    [Serializable]
    public sealed class LocationFile
    {
        public int version;
        public LakeLocation[] locations;
    }

    /// <summary>Водоём: несколько мест ловли на одном озере.</summary>
    [Serializable]
    public sealed class LakeLocation
    {
        public string id;
        public int unlockPrice;
        public LakeSpot[] spots;

        /// <summary>
        /// С какого уровня игроку есть куда плыть: наименьший minLevel среди мест на воде. 0 — таких мест нет.
        /// Пока уровень ниже, аренда лодки — деньги на ветер, её не дают.
        /// </summary>
        public int BoatUsefulFromLevel()
        {
            int level = 0;
            if (spots == null)
                return level;
            foreach (var spot in spots)
            {
                if (spot != null && spot.needsBoat && (level == 0 || spot.minLevel < level))
                    level = spot.minLevel;
            }

            return level;
        }
    }

    /// <summary>
    /// Место ловли: своя глубина и свой набор рыбы. Глубина растёт от берега:
    /// shoreDepthCm у кромки, дальше — на slopeCmPerM за каждый метр, но не глубже maxDepthCm.
    /// </summary>
    [Serializable]
    public sealed class LakeSpot
    {
        public string id;
        /// <summary>С какого уровня игрока место открыто (1 — сразу).</summary>
        public int minLevel;
        /// <summary>Место на воде: с берега не добраться, нужна лодка.</summary>
        public bool needsBoat;
        public int shoreDepthCm;
        public int slopeCmPerM;
        public int maxDepthCm;
        public string[] fish;

        /// <summary>Что мешает игроку попасть на место: сначала уровень, потом лодка.</summary>
        public SpotAccess AccessFor(int level, bool hasBoat)
        {
            if (level < minLevel)
                return SpotAccess.NeedsLevel;
            return needsBoat && !hasBoat ? SpotAccess.NeedsBoat : SpotAccess.Open;
        }

        /// <summary>Глубина в точке, см. shoreDistanceM — расстояние от ближнего берега, м (на берегу — 0).</summary>
        public float DepthCm(float shoreDistanceM)
        {
            float depth = shoreDepthCm + Math.Max(0f, shoreDistanceM) * slopeCmPerM;
            return Math.Max(shoreDepthCm, Math.Min(maxDepthCm, depth));
        }
    }

    /// <summary>Доступ игрока к месту ловли.</summary>
    public enum SpotAccess
    {
        Open,
        /// <summary>Уровень игрока ниже minLevel места.</summary>
        NeedsLevel,
        /// <summary>Уровня хватает, но место на воде, а лодки нет.</summary>
        NeedsBoat,
    }

    /// <summary>Проверенные водоёмы и места из locations.json.</summary>
    public sealed class LocationDatabase
    {
        public const int SupportedVersion = 4;

        private readonly List<LakeLocation> _all = new List<LakeLocation>();

        /// <param name="fish">Виды рыб: каждое место может называть только существующие.</param>
        public LocationDatabase(LocationFile file, FishDatabase fish)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (fish == null)
                throw new ArgumentNullException(nameof(fish));
            if (file.version != SupportedVersion)
                throw new FormatException("locations.json: ожидалась версия " + SupportedVersion + ", а в файле " + file.version);
            if (file.locations == null || file.locations.Length == 0)
                throw new FormatException("locations.json: список locations пуст");

            foreach (var location in file.locations)
            {
                if (location == null || string.IsNullOrEmpty(location.id))
                    throw new FormatException("locations.json: у водоёма нет id");
                if (Find(location.id) != null)
                    throw new FormatException("locations.json: водоём «" + location.id + "» описан дважды");
                if (location.unlockPrice < 0)
                    throw new FormatException("locations.json: «" + location.id + "»: unlockPrice не может быть меньше 0");
                if (location.spots == null || location.spots.Length == 0)
                    throw new FormatException("locations.json: «" + location.id + "»: нет мест (spots)");

                var seen = new HashSet<string>();
                bool open = false;
                foreach (var spot in location.spots)
                {
                    Validate(location.id, spot, seen, fish);
                    open |= spot.minLevel == 1 && !spot.needsBoat;
                }

                if (!open)
                    throw new FormatException("locations.json: «" + location.id + "»: нет места, открытого с первого уровня без лодки");
                _all.Add(location);
            }
        }

        public IReadOnlyList<LakeLocation> All => _all;

        public LakeLocation Get(string id)
        {
            var location = Find(id);
            if (location == null)
                throw new KeyNotFoundException("Нет водоёма «" + id + "» в locations.json");
            return location;
        }

        /// <summary>Место по id; null — такого нет (например, в старом сохранении).</summary>
        public LakeSpot FindSpot(string locationId, string spotId)
        {
            var location = Find(locationId);
            if (location == null || spotId == null)
                return null;
            foreach (var spot in location.spots)
            {
                if (spot.id == spotId)
                    return spot;
            }
            return null;
        }

        private LakeLocation Find(string id)
        {
            foreach (var location in _all)
            {
                if (location.id == id)
                    return location;
            }
            return null;
        }

        private static void Validate(string locationId, LakeSpot spot, HashSet<string> seen, FishDatabase fish)
        {
            string where = "locations.json: «" + locationId + "»: ";
            if (spot == null || string.IsNullOrEmpty(spot.id))
                throw new FormatException(where + "у места нет id");
            where += "место «" + spot.id + "»: ";
            if (!seen.Add(spot.id))
                throw new FormatException(where + "описано дважды");
            if (spot.minLevel < 1 || spot.minLevel > LevelRules.MaxLevel)
                throw new FormatException(where + "minLevel должен быть от 1 до " + LevelRules.MaxLevel);
            if (spot.shoreDepthCm <= 0)
                throw new FormatException(where + "shoreDepthCm должен быть больше 0");
            if (spot.slopeCmPerM <= 0)
                throw new FormatException(where + "slopeCmPerM должен быть больше 0");
            if (spot.maxDepthCm < spot.shoreDepthCm)
                throw new FormatException(where + "maxDepthCm не может быть меньше shoreDepthCm");
            if (spot.fish == null || spot.fish.Length == 0)
                throw new FormatException(where + "нет рыбы (fish)");
            foreach (string id in spot.fish)
            {
                if (!fish.TryGet(id, out _))
                    throw new FormatException(where + "неизвестный вид рыбы «" + id + "»");
            }
        }
    }
}

using System;

namespace Fishing.Core
{
    // Классы для чтения shared/data/fish.json. Имена полей совпадают с ключами JSON,
    // поэтому они в camelCase. Описание полей — shared/data/README.md,
    // TypeScript-двойник — server/modules/src/data.ts.

    [Serializable]
    public sealed class FishFile
    {
        public int version;
        public FishSpecies[] species;
    }

    [Serializable]
    public sealed class FishSpecies
    {
        public string id;
        public WeightRange weightG;
        public float trophyChance;
        public IntRange depthCm;
        public int[] activeHours;
        /// <summary>На что берёт вид и насколько охотно.</summary>
        public BaitTaste[] baits;
        public float biteRatePerMin;
        public float maxLineMm;
        public int strength;
        public int stamina;
        public string fightStyle;
        public int pricePerKg;

        /// <summary>Вкус наживки для вида, 0…100: 0 — не берёт вовсе, 100 — лучшая наживка.</summary>
        public int TasteFor(string baitId)
        {
            if (baits == null || baitId == null)
                return 0;
            foreach (var bait in baits)
            {
                if (bait != null && bait.id == baitId)
                    return bait.taste;
            }

            return 0;
        }
    }

    /// <summary>Наживка вида: id из tackle.json и вкус 1…100 — доля от полного клёва в процентах.</summary>
    [Serializable]
    public sealed class BaitTaste
    {
        public string id;
        public int taste;
    }

    [Serializable]
    public sealed class IntRange
    {
        public int min;
        public int max;
    }

    [Serializable]
    public sealed class WeightRange
    {
        public int min;
        public int typical;
        /// <summary>Зачётный вес: с него рыба считается зачётной (значения — как в «Русской рыбалке 3»).</summary>
        public int qualifying;
        public int trophy;
        public int max;
    }
}

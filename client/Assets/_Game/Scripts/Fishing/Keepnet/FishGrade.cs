using System;

namespace Fishing.Core
{
    /// <summary>Разряд пойманной рыбы по весу — что написать на карточке улова.</summary>
    public enum FishGrade
    {
        /// <summary>Малёк: легче половины обычного веса вида. Забрать — минус к карме.</summary>
        Juvenile,
        /// <summary>Обычная рыба.</summary>
        Regular,
        /// <summary>Зачётная: не легче зачётного веса вида (weightG.qualifying) — пороги как в «Русской рыбалке 3».</summary>
        Qualifying,
        /// <summary>Трофей.</summary>
        Trophy,
    }

    public static class FishGrades
    {
        public static FishGrade Of(FishSpecies species, int weightG, bool trophy)
        {
            if (species == null)
                throw new ArgumentNullException(nameof(species));
            if (trophy)
                return FishGrade.Trophy;
            if (KarmaRules.IsJuvenile(species, weightG))
                return FishGrade.Juvenile;
            return weightG >= species.weightG.qualifying ? FishGrade.Qualifying : FishGrade.Regular;
        }
    }

    /// <summary>Чем примечателен улов для самого игрока — по его статистике на момент поимки.</summary>
    public readonly struct CatchMarks
    {
        /// <summary>Побит личный рекорд вида (первая рыба вида рекордом не считается).</summary>
        public readonly bool SpeciesRecord;
        /// <summary>Прежний рекорд вида, г; 0 — этот вид раньше не ловил.</summary>
        public readonly int PreviousBestG;
        /// <summary>Первая рыба этого вида.</summary>
        public readonly bool FirstOfSpecies;
        /// <summary>Самая тяжёлая рыба игрока за всё время (самая первая рыба так не отмечается).</summary>
        public readonly bool BiggestEver;

        public CatchMarks(bool speciesRecord, int previousBestG, bool firstOfSpecies, bool biggestEver)
        {
            SpeciesRecord = speciesRecord;
            PreviousBestG = previousBestG;
            FirstOfSpecies = firstOfSpecies;
            BiggestEver = biggestEver;
        }
    }
}

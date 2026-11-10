using System;

namespace Fishing.Core
{
    /// <summary>Ранг игрока: название для группы уровней.</summary>
    public enum PlayerRank
    {
        /// <summary>Уровни 1–4.</summary>
        Novice,
        /// <summary>Уровни 5–9.</summary>
        Amateur,
        /// <summary>Уровни 10–14.</summary>
        Angler,
        /// <summary>Уровни 15–19.</summary>
        Experienced,
        /// <summary>Уровень 20 и выше.</summary>
        Master,
    }

    /// <summary>Опыт за одну пойманную рыбу: из чего сложился и поднял ли уровень.</summary>
    public readonly struct XpGain
    {
        /// <summary>Опыт за саму рыбу: вес × ценность вида.</summary>
        public readonly int Base;
        /// <summary>Множитель разряда рыбы: 1 — обычная, 3 — зачётная, 6 — трофей.</summary>
        public readonly int Factor;
        /// <summary>Надбавки: первая рыба вида, личный рекорд вида.</summary>
        public readonly int Bonus;
        public readonly int LevelBefore;
        public readonly int LevelAfter;

        public XpGain(int baseXp, int factor, int bonus, int levelBefore, int levelAfter)
        {
            Base = baseXp;
            Factor = factor;
            Bonus = bonus;
            LevelBefore = levelBefore;
            LevelAfter = levelAfter;
        }

        /// <summary>Сколько опыта получено всего.</summary>
        public int Total => Base * Factor + Bonus;

        public bool LeveledUp => LevelAfter > LevelBefore;
    }

    /// <summary>
    /// Уровни и опыт. Уровень — номер, как разряд в «Русской рыбалке 3», пороги первых тридцати взяты оттуда.
    /// Опыт дают за пойманную рыбу — чем она ценнее, тем больше; зачётная и трофей — в разы больше.
    /// Опыт начисляется при поимке: отпустил рыбу или забрал — неважно.
    /// </summary>
    public static class LevelRules
    {
        public const int MaxLevel = 30;

        /// <summary>Зачётная рыба даёт втрое больше опыта, трофей — вшестеро.</summary>
        public const int QualifyingFactor = 3;
        public const int TrophyFactor = 6;

        /// <summary>Надбавка за первую рыбу вида и за побитый личный рекорд вида.</summary>
        public const int FirstOfSpeciesBonus = 50;
        public const int SpeciesRecordBonus = 15;

        // Опыт всего, нужный для уровня (индекс 0 — уровень 1). Только для чтения.
        private static readonly long[] Thresholds =
        {
            0, 100, 200, 350, 600, 850, 1200, 1600, 2600, 3700,
            4800, 5900, 6900, 8000, 10000, 12600, 16000, 20000, 24000, 30000,
            36500, 85000, 145000, 210000, 290000, 490000, 895000, 1498000, 2200000, 2950000,
        };

        /// <summary>Сколько опыта всего нужно для уровня (1…MaxLevel).</summary>
        public static long XpForLevel(int level)
        {
            return Thresholds[Math.Max(1, Math.Min(MaxLevel, level)) - 1];
        }

        public static int LevelFor(long xp)
        {
            int level = 1;
            while (level < MaxLevel && xp >= Thresholds[level])
                level++;
            return level;
        }

        /// <summary>
        /// Путь до следующего уровня: into — набрано сверх порога текущего, span — всего между порогами.
        /// На последнем уровне span = 0.
        /// </summary>
        public static void Progress(long xp, out int level, out long into, out long span)
        {
            level = LevelFor(xp);
            long floor = XpForLevel(level);
            into = Math.Max(0L, xp - floor);
            span = level < MaxLevel ? XpForLevel(level + 1) - floor : 0L;
        }

        public static PlayerRank RankFor(int level)
        {
            if (level >= 20) return PlayerRank.Master;
            if (level >= 15) return PlayerRank.Experienced;
            if (level >= 10) return PlayerRank.Angler;
            if (level >= 5) return PlayerRank.Amateur;
            return PlayerRank.Novice;
        }

        public static int FactorFor(FishGrade grade)
        {
            switch (grade)
            {
                case FishGrade.Trophy: return TrophyFactor;
                case FishGrade.Qualifying: return QualifyingFactor;
                default: return 1;
            }
        }

        /// <summary>Опыт за саму рыбу без множителя и надбавок: вес × цена вида за килограмм, не меньше 1.</summary>
        public static int BaseXp(FishSpecies species, int weightG)
        {
            if (species == null)
                throw new ArgumentNullException(nameof(species));
            if (weightG <= 0)
                return 0;
            return (int)Math.Max(1L, Math.Min(int.MaxValue / TrophyFactor, (long)weightG * species.pricePerKg / 1000L));
        }

        /// <summary>Опыт за пойманную рыбу с учётом её разряда и отметок улова; уровни — до и после (xpBefore — опыт до рыбы).</summary>
        public static XpGain ForCatch(FishSpecies species, int weightG, bool trophy, CatchMarks marks, long xpBefore)
        {
            int baseXp = BaseXp(species, weightG);
            int factor = FactorFor(FishGrades.Of(species, weightG, trophy));
            int bonus = (marks.FirstOfSpecies ? FirstOfSpeciesBonus : 0) + (marks.SpeciesRecord ? SpeciesRecordBonus : 0);
            int total = baseXp * factor + bonus;
            return new XpGain(baseXp, factor, bonus, LevelFor(xpBefore), LevelFor(xpBefore + total));
        }
    }
}

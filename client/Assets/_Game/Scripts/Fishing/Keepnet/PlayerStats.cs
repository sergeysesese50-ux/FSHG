using System;
using System.Collections.Generic;

namespace Fishing.Core
{
    /// <summary>Статистика по одному виду рыбы в сохранении. Имена полей — ключи JSON.</summary>
    [Serializable]
    public sealed class SpeciesStats
    {
        public string speciesId;
        /// <summary>Сколько вытащено на берег (и оставленных, и отпущенных).</summary>
        public int caught;
        public long totalWeightG;
        /// <summary>Рекорд вида: вес и длина самой тяжёлой рыбы.</summary>
        public int bestWeightG;
        public int bestLengthCm;
        public int trophies;
        public int released;
        public int sold;

        public int AverageWeightG => caught > 0 ? (int)(totalWeightG / caught) : 0;
    }

    /// <summary>Сходы по причинам. Имена полей — ключи JSON.</summary>
    [Serializable]
    public sealed class LossStats
    {
        public int lineBroke;
        public int rodBroke;
        public int escaped;
        public int spoolEmpty;

        public int Total => lineBroke + rodBroke + escaped + spoolEmpty;
    }

    /// <summary>
    /// Статистика ловли игрока: по видам (сколько, общий вес, рекорд, трофеи, отпущено, продано), сходы по причинам,
    /// заработанные деньги. Виды идут в порядке первой поимки.
    /// </summary>
    public sealed class PlayerStats
    {
        private readonly List<SpeciesStats> _species = new List<SpeciesStats>();

        public IReadOnlyList<SpeciesStats> Species => _species;
        public LossStats Losses { get; private set; } = new LossStats();
        public long MoneyEarned { get; private set; }

        public int FishCaught
        {
            get
            {
                int sum = 0;
                foreach (var s in _species)
                    sum += s.caught;
                return sum;
            }
        }

        public long TotalWeightG
        {
            get
            {
                long sum = 0;
                foreach (var s in _species)
                    sum += s.totalWeightG;
                return sum;
            }
        }

        public int Trophies => Sum(s => s.trophies);
        public int Released => Sum(s => s.released);
        public int Sold => Sum(s => s.sold);

        /// <summary>Вид с самой тяжёлой рыбой за всё время; null — ещё ничего не поймано.</summary>
        public SpeciesStats Biggest
        {
            get
            {
                SpeciesStats best = null;
                foreach (var s in _species)
                {
                    if (s.bestWeightG > 0 && (best == null || s.bestWeightG > best.bestWeightG))
                        best = s;
                }
                return best;
            }
        }

        public SpeciesStats Find(string speciesId)
        {
            foreach (var s in _species)
            {
                if (s.speciesId == speciesId)
                    return s;
            }
            return null;
        }

        /// <summary>Рыба вытащена: в статистику. Возвращает, чем улов примечателен для игрока (рекорд вида, первая рыба вида…).</summary>
        public CatchMarks RecordCatch(string speciesId, int weightG, int lengthCm, bool trophy)
        {
            if (string.IsNullOrEmpty(speciesId) || weightG <= 0)
                return default;
            var biggest = Biggest;
            bool biggestEver = FishCaught > 0 && (biggest == null || weightG > biggest.bestWeightG);
            var s = GetOrAdd(speciesId);
            bool first = s.caught == 0;
            int previous = s.bestWeightG;
            bool record = !first && weightG > previous;
            s.caught++;
            s.totalWeightG += weightG;
            if (trophy)
                s.trophies++;
            if (weightG > s.bestWeightG)
            {
                s.bestWeightG = weightG;
                s.bestLengthCm = Math.Max(0, lengthCm);
            }
            return new CatchMarks(record, previous, first, biggestEver);
        }

        public void RecordRelease(string speciesId)
        {
            if (!string.IsNullOrEmpty(speciesId))
                GetOrAdd(speciesId).released++;
        }

        public void RecordSale(string speciesId, long money)
        {
            if (!string.IsNullOrEmpty(speciesId))
                GetOrAdd(speciesId).sold++;
            MoneyEarned += Math.Max(0L, money);
        }

        public void RecordLoss(FightOutcome outcome)
        {
            switch (outcome)
            {
                case FightOutcome.LineBroke: Losses.lineBroke++; break;
                case FightOutcome.RodBroke: Losses.rodBroke++; break;
                case FightOutcome.Escaped: Losses.escaped++; break;
                case FightOutcome.SpoolEmpty: Losses.spoolEmpty++; break;
            }
        }

        public SpeciesStats[] SpeciesState() => _species.ToArray();

        /// <summary>Восстановление из сохранения: отрицательные числа обнуляются, повторы и пустые записи пропускаются.</summary>
        public static PlayerStats FromState(SpeciesStats[] species, LossStats losses, long moneyEarned)
        {
            var stats = new PlayerStats { MoneyEarned = Math.Max(0L, moneyEarned) };
            if (species != null)
            {
                foreach (var s in species)
                {
                    if (s == null || string.IsNullOrEmpty(s.speciesId) || stats.Find(s.speciesId) != null)
                        continue;
                    stats._species.Add(new SpeciesStats
                    {
                        speciesId = s.speciesId,
                        caught = Math.Max(0, s.caught),
                        totalWeightG = Math.Max(0L, s.totalWeightG),
                        bestWeightG = Math.Max(0, s.bestWeightG),
                        bestLengthCm = Math.Max(0, s.bestLengthCm),
                        trophies = Math.Max(0, s.trophies),
                        released = Math.Max(0, s.released),
                        sold = Math.Max(0, s.sold),
                    });
                }
            }

            if (losses != null)
            {
                stats.Losses = new LossStats
                {
                    lineBroke = Math.Max(0, losses.lineBroke),
                    rodBroke = Math.Max(0, losses.rodBroke),
                    escaped = Math.Max(0, losses.escaped),
                    spoolEmpty = Math.Max(0, losses.spoolEmpty),
                };
            }

            return stats;
        }

        private SpeciesStats GetOrAdd(string speciesId)
        {
            var s = Find(speciesId);
            if (s != null)
                return s;
            s = new SpeciesStats { speciesId = speciesId };
            _species.Add(s);
            return s;
        }

        private int Sum(Func<SpeciesStats, int> value)
        {
            int sum = 0;
            foreach (var s in _species)
                sum += value(s);
            return sum;
        }
    }
}
